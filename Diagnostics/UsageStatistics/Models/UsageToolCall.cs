namespace RoslynMcpServer.Diagnostics.UsageStatistics.Models;

internal sealed class UsageToolCall
{
    internal required long Id { get; init; }
    internal required string Name { get; init; }
    internal required DateTimeOffset Started { get; init; }
    internal required long Timestamp { get; init; }
    internal string? WorkspaceKey { get; init; }
    internal bool RequiresWorkspace { get; init; }
    internal bool LazyLoad { get; set; }
    internal bool Unsuccessful { get; set; }
    internal UsageToolCall? Parent { get; init; }
    internal long WorkspaceReadLockWaitTicks => Interlocked.Read(ref _workspaceReadLockWaitTicks);

    internal void AddWorkspaceReadLockWait(long ticks) =>
        Interlocked.Add(ref _workspaceReadLockWaitTicks, ticks);

    private long _workspaceReadLockWaitTicks;
}
