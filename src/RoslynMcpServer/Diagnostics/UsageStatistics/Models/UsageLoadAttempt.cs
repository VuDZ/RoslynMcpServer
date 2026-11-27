using System.Diagnostics;

namespace RoslynMcpServer.Diagnostics.UsageStatistics.Models;

internal sealed class UsageLoadAttempt
{
    internal required string Source { get; init; }
    internal required DateTimeOffset Started { get; init; }
    internal bool ResidentCaptured { get; set; }
    internal bool Recorded { get; set; }
    internal bool Faulted { get; set; }
    internal string? RequestedKey { get; set; }
    internal string? ResidentKey { get; set; }
    internal bool CacheHit { get; set; }
    internal bool GraphStale { get; set; }
    internal bool Published { get; set; }
    internal int Projects { get; set; }
    internal int? Watchers { get; set; }
    internal int? WatcherFailures { get; set; }
    internal long? WaitStarted { get; set; }
    internal double? WaitMilliseconds { get; set; }
    internal Stopwatch Elapsed { get; } = Stopwatch.StartNew();
}
