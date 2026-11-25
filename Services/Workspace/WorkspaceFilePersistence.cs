using System.Security.AccessControl;
using System.Text;
using RoslynMcpServer.Services.Models;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>Stages complete bytes beside the destination before replacing it.</summary>
internal static class WorkspaceFilePersistence
{
    public static Task WriteTextAsync(
        WorkspaceInputSession? session,
        string fullPath,
        string text,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        return WriteTextAsync(session, fullPath, text, encoding, null, cancellationToken);
    }

    public static void WriteText(
        WorkspaceInputSession? session,
        string fullPath,
        string text,
        Encoding encoding,
        Action? afterStagingPrefix = null)
    {
        WriteCoreAsync(
            session,
            fullPath,
            text,
            encoding,
            useAsync: false,
            requireAbsent: false,
            afterStagingPrefix,
            CancellationToken.None).GetAwaiter().GetResult();
    }

    internal static Task WriteTextAsync(
        WorkspaceInputSession? session,
        string fullPath,
        string text,
        Encoding encoding,
        Action? afterStagingPrefix,
        CancellationToken cancellationToken)
    {
        return WriteCoreAsync(
            session, fullPath, text, encoding, useAsync: true, requireAbsent: false, afterStagingPrefix, cancellationToken);
    }

    internal static Task CreateTextAsync(
        WorkspaceInputSession? session,
        string fullPath,
        string text,
        Encoding encoding,
        Action? afterStagingPrefix,
        CancellationToken cancellationToken)
    {
        return WriteCoreAsync(
            session, fullPath, text, encoding, useAsync: true, requireAbsent: true, afterStagingPrefix, cancellationToken);
    }

    internal static bool IsStagingPath(WorkspaceInputSession session, string path)
    {
        // Windows watcher notifications can spell the same path with different casing.
        return Path.GetFileName(path).StartsWith(StagingPrefix(session), InputPathCanon.Comparison)
            && Path.GetExtension(path).Equals(".tmp", InputPathCanon.Comparison);
    }

    private static async Task WriteCoreAsync(
        WorkspaceInputSession? session,
        string fullPath,
        string text,
        Encoding encoding,
        bool useAsync,
        bool requireAbsent,
        Action? afterStagingPrefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var canonical = Path.GetFullPath(fullPath);
        var file = new FileInfo(canonical);
        var destination = requireAbsent || file.LinkTarget is null
            ? file
            : file.ResolveLinkTarget(returnFinalTarget: true)!;
        var staged = Path.Combine(
            Path.GetDirectoryName(destination.FullName)!,
            $"{StagingPrefix(session)}{Guid.NewGuid():N}.tmp");
        var payload = PersistedPayload.FromText(text, encoding);
        var begun = session?.BeginOwnWrite(canonical) == true;
        var ownsStagedFile = false;
        var committed = false;
        try
        {
            var existed = !requireAbsent && destination.Exists;
            var unixMode = ReadDestinationMode(destination, existed);
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Options = useAsync ? FileOptions.Asynchronous : FileOptions.None,
            };

            if (!OperatingSystem.IsWindows() && existed)
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(staged, options))
            {
                ownsStagedFile = true;
                if (OperatingSystem.IsWindows() && existed)
                {
                    new FileInfo(staged).SetAccessControl(new FileInfo(destination.FullName)
                        .GetAccessControl(AccessControlSections.Access));
                }

                await StageAsync(stream, payload, useAsync, afterStagingPrefix, cancellationToken).ConfigureAwait(false);
            }

            if (!OperatingSystem.IsWindows() && unixMode is { } mode)
            {
                File.SetUnixFileMode(staged, mode);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (existed)
            {
                // A writable parent directory does not authorize replacing an unwritable file.
                File.Open(destination.FullName, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)
                    .Dispose();
                ReplaceStagedFile(staged, destination.FullName);
                committed = true;
            }
            else
            {
                // Creating the link is the no-overwrite commit, including for aliased directories.
                File.CreateHardLink(destination.FullName, staged);
                committed = true;
                DeleteStagedFile(staged);
            }
            if (begun)
            {
                session!.CommitOwnWrite(canonical, payload, notifyEvenWhenUnchanged: true);
            }
        }
        finally
        {
            if (!committed && begun)
            {
                session!.AbandonOwnWrite(canonical);
            }

            if (!committed && ownsStagedFile)
            {
                DeleteStagedFile(staged);
            }
        }
    }

    private static UnixFileMode? ReadDestinationMode(FileSystemInfo destination, bool existed)
    {
        if (!existed)
        {
            return null;
        }

        return OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(destination.FullName);
    }

    private static async Task StageAsync(
        FileStream stream,
        ReadOnlyMemory<byte> payload,
        bool useAsync,
        Action? afterStagingPrefix,
        CancellationToken cancellationToken)
    {
        if (afterStagingPrefix is not null)
        {
            var prefix = Math.Min(1, payload.Length);
            stream.Write(payload.Span[..prefix]);
            stream.Flush();
            afterStagingPrefix();
            payload = payload[prefix..];
        }

        if (useAsync)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            stream.Write(payload.Span);
        }
    }

    private static void ReplaceStagedFile(string staged, string destination)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.Replace(staged, destination, destinationBackupFileName: null);
            return;
        }

        // ReplaceFileW can move the original out of the way before reporting a rename failure.
        // Keep its recovery name until the replacement is confirmed.
        var backup = Path.ChangeExtension(staged, ".backup.tmp");
        try
        {
            File.Replace(staged, destination, backup);
        }
        catch (Exception replaceError) when ((replaceError is IOException or UnauthorizedAccessException) && File.Exists(backup))
        {
            try
            {
                File.Move(backup, destination);
            }
            catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"Could not replace {destination}. The original is available at {backup}.",
                    new AggregateException(replaceError, restoreError));
            }

            throw;
        }

        DeleteStagedFile(backup);
    }

    private static void DeleteStagedFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the write failure visible when cleanup is also prevented by the filesystem.
        }
    }

    private static string StagingPrefix(WorkspaceInputSession? session) =>
        $".roslyn-mcp-write-{session?.Generation:N}-";
}
