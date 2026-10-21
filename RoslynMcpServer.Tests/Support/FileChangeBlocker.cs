namespace RoslynMcpServer.Tests.Support;

/// <summary>
/// Makes writing or deleting one file fail until disposal, so a test can inject an I/O failure on any OS.
/// </summary>
/// <remarks>
/// Windows blocks both with an open handle that shares only reading. Unix has no mandatory file locks:
/// an open handle blocks neither writing nor unlinking, so permissions are used instead. Writing needs the
/// file's write bit; deleting needs the write bit of the parent directory. Root ignores both, so the
/// blocker refuses to run in a privileged process rather than let the test pass without a failure.
/// </remarks>
internal sealed class FileChangeBlocker : IDisposable
{
    private FileChangeBlocker(FileStream? handle, string? restorePath, UnixFileMode restoreMode)
    {
        _handle = handle;
        _restorePath = restorePath;
        _restoreMode = restoreMode;
    }

    /// <summary>Writing <paramref name="path"/> fails; the file itself stays readable.</summary>
    public static FileChangeBlocker BlockWrite(string path) => Block(path, path);

    /// <summary>Deleting <paramref name="path"/> fails, which also makes deleting its directory tree fail.</summary>
    public static FileChangeBlocker BlockDelete(string path) => Block(path, Path.GetDirectoryName(path)!);

    public void Dispose()
    {
        _handle?.Dispose();
        if (_restorePath is not null && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_restorePath, _restoreMode);
        }
    }

    private static FileChangeBlocker Block(string path, string unixTarget)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileChangeBlocker(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), null, default);
        }

        if (Environment.IsPrivilegedProcess)
        {
            throw new InvalidOperationException("Permission-based I/O failure injection does not work for root.");
        }

        var mode = File.GetUnixFileMode(unixTarget);
        File.SetUnixFileMode(unixTarget, mode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
        return new FileChangeBlocker(null, unixTarget, mode);
    }

    private readonly FileStream? _handle;
    private readonly string? _restorePath;
    private readonly UnixFileMode _restoreMode;
}
