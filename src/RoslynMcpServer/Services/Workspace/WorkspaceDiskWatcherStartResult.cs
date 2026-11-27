namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Watchers that were actually enabled, plus the directory and recursion of each, and the descriptors whose
/// watcher failed to start with the exception message. A missing directory is not a failure.
/// </summary>
internal sealed class WorkspaceDiskWatcherStartResult
{
    public IReadOnlyList<FileSystemWatcher> Watchers { get; }

    public IReadOnlyList<(string Directory, bool IncludeSubdirectories)> Subscriptions { get; }

    public IReadOnlyList<(string Directory, string Reason)> Failures { get; }

    public WorkspaceDiskWatcherStartResult(
        IReadOnlyList<FileSystemWatcher> watchers,
        IReadOnlyList<(string Directory, bool IncludeSubdirectories)> subscriptions,
        IReadOnlyList<(string Directory, string Reason)> failures)
    {
        ArgumentNullException.ThrowIfNull(watchers);
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(failures);
        Watchers = watchers;
        Subscriptions = subscriptions;
        Failures = failures;
    }
}
