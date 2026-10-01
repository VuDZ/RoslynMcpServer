namespace RoslynMcpServer.Services.Workspace;

/// <summary>Watchers that were actually enabled, plus the directory and recursion of each.</summary>
internal sealed class WorkspaceDiskWatcherStartResult
{
    public IReadOnlyList<FileSystemWatcher> Watchers { get; }

    public IReadOnlyList<(string Directory, bool IncludeSubdirectories)> Subscriptions { get; }

    public WorkspaceDiskWatcherStartResult(
        IReadOnlyList<FileSystemWatcher> watchers,
        IReadOnlyList<(string Directory, bool IncludeSubdirectories)> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(watchers);
        ArgumentNullException.ThrowIfNull(subscriptions);
        Watchers = watchers;
        Subscriptions = subscriptions;
    }
}
