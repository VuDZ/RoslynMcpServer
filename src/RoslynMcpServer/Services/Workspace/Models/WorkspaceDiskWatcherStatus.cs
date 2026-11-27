namespace RoslynMcpServer.Services.Workspace.Models;

/// <summary>
/// Disk watchers of the current workspace session. <see cref="Failures"/> lists directories whose watcher
/// could not start, with the exception message, for example the Linux inotify instance limit.
/// </summary>
public sealed record WorkspaceDiskWatcherStatus(
    int Started,
    int Recursive,
    IReadOnlyList<(string Directory, string Reason)> Failures)
{
    public static WorkspaceDiskWatcherStatus None { get; } = new(0, 0, Array.Empty<(string Directory, string Reason)>());
}
