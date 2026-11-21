using System.Diagnostics;
using RoslynMcpServer.Diagnostics.UsageStatistics.Models;

namespace RoslynMcpServer.Diagnostics.UsageStatistics;

internal readonly struct UsageWorkspaceReadLockWait(UsageToolCall call) : IDisposable
{
    // The semaphore may already be held at disposal. Keep this path to a clock read and an atomic add.
    public void Dispose() => call.AddWorkspaceReadLockWait(Stopwatch.GetTimestamp() - _started);

    private readonly long _started = Stopwatch.GetTimestamp();
}
