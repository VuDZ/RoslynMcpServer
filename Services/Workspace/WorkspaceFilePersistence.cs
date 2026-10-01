using System.Text;
using RoslynMcpServer.Services.Models;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Writes text and records the own-write protocol on the watcher session that is current for the caller.
/// The pending record is registered before I/O. A failed write stores no echo bytes.
/// </summary>
internal static class WorkspaceFilePersistence
{
    public static async Task WriteTextAsync(
        WorkspaceInputSession? session,
        string fullPath,
        string text,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        var canonical = CanonicalOrOriginal(fullPath);
        var begun = session?.BeginOwnWrite(canonical) == true;
        try
        {
            var payload = PersistedPayload.FromText(text, encoding);
            await File.WriteAllBytesAsync(canonical, payload, cancellationToken).ConfigureAwait(false);
            if (begun)
            {
                session!.CommitOwnWrite(canonical, payload, notifyEvenWhenUnchanged: true);
            }
        }
        catch
        {
            if (begun)
            {
                session!.AbandonOwnWrite(canonical);
            }

            throw;
        }
    }

    public static void WriteText(
        WorkspaceInputSession? session,
        string fullPath,
        string text,
        Encoding encoding)
    {
        var canonical = CanonicalOrOriginal(fullPath);
        var begun = session?.BeginOwnWrite(canonical) == true;
        try
        {
            var payload = PersistedPayload.FromText(text, encoding);
            File.WriteAllBytes(canonical, payload);
            if (begun)
            {
                session!.CommitOwnWrite(canonical, payload, notifyEvenWhenUnchanged: true);
            }
        }
        catch
        {
            if (begun)
            {
                session!.AbandonOwnWrite(canonical);
            }

            throw;
        }
    }

    private static string CanonicalOrOriginal(string fullPath)
    {
        return InputPathCanon.TryCanonicalize(fullPath) ?? fullPath;
    }
}
