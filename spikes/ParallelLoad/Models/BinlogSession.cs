namespace ParallelLoad.Models;

internal sealed record BinlogSession(
    string File,
    int FormatVersion,
    Dictionary<string, int> EventCounts,
    string[] RecoverableErrors,
    int TopLevelBuilds,
    double FirstMs,
    double WeightMs,
    double OwnBuildMs)
{
    public ProjectBuildInterval[] Intervals { get; init; } = [];
}
