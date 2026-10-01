using RoslynMcpServer.Services.Models;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Compares queued content events to the committed bytes of the same generation.
/// The read runs here, not on the watcher callback.
/// </summary>
internal static class WorkspaceInputReconciler
{
    public static IReadOnlyList<InputContentReconcileResult> Reconcile(WorkspaceInputSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var pending = session.CopyPendingContentPaths();
        if (pending.Count == 0)
        {
            return Array.Empty<InputContentReconcileResult>();
        }

        var results = new List<InputContentReconcileResult>(pending.Count);
        foreach (var path in pending)
        {
            var started = session.EventRevision(path);
            if (!session.IsPendingContent(path))
            {
                continue;
            }

            var read = StableFileBytes.Read(path);
            var outcome = session.ApplyContentObservation(path, read, started);
            results.Add(new InputContentReconcileResult(path, outcome));
        }

        return results;
    }
}
