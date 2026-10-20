using Microsoft.CodeAnalysis;

namespace ParallelLoad.Models;

internal sealed class CoreFirstGroup(int index, string? aggregatorPath, ProjectInfo[] projects, PhaseLoadProgress progress)
{
    public int Index { get; } = index;
    public string? AggregatorPath { get; } = aggregatorPath;
    public ProjectInfo[] Projects { get; set; } = projects;
    public PhaseLoadProgress Progress { get; } = progress;
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
}
