namespace ParallelLoad.Models;

internal sealed record CostProfile(
    string Status,
    string? Error,
    double BaselineWallMs,
    string ReaderVersion,
    int BinlogCount,
    ProjectCost[] Projects)
{
    public CostModel? Model { get; init; }
    public BinlogSession[] Sessions { get; init; } = [];
    public double LoadSolutionMs { get; init; }
    public double ReconciliationError { get; init; }
}
