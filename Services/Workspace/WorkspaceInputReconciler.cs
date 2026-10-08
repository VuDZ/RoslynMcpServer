using RoslynMcpServer.Services.Models;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Compares queued content events to the committed bytes of the same generation.
/// The read runs here, not on the watcher callback.
/// </summary>
internal static class WorkspaceInputReconciler
{
    public static IReadOnlyList<InputContentReconcileResult> Reconcile(
        WorkspaceInputSession session,
        Action<string>? duringRead = null)
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

            // The two reads do not hold the session gate. A watcher callback can queue a newer
            // event while they run. Callers of ApplyContentObservation still see LeftPending;
            // this promotion is what tells the flush to read again instead of publishing.
            duringRead?.Invoke(path);
            var read = StableFileBytes.Read(path);
            var outcome = session.ApplyContentObservation(path, read, started);
            if (outcome == ContentReconcileOutcome.LeftPending
                && session.EventRevision(path) != started)
            {
                outcome = ContentReconcileOutcome.RevisionMoved;
            }

            results.Add(new InputContentReconcileResult(path, outcome));
        }

        return results;
    }
}
