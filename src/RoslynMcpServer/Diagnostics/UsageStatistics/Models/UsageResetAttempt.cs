namespace RoslynMcpServer.Diagnostics.UsageStatistics.Models;

internal sealed class UsageResetAttempt
{
    internal DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
    internal string? OldKey { get; set; }
    internal bool Success { get; set; }
    internal bool Recorded { get; set; }
}
