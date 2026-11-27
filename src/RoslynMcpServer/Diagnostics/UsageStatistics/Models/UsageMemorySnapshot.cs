namespace RoslynMcpServer.Diagnostics.UsageStatistics.Models;

internal sealed record UsageMemorySnapshot(
    long? MemoryBytes,
    long? BuildHostBytes,
    int? BuildHostCount,
    long? WorkingSetBytes,
    long? ManagedHeapBytes,
    long? CommittedBytes);
