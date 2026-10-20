namespace ParallelLoad.Models;

internal sealed record ProjectCost(
    string Key,
    string FilePath,
    string Name,
    string[] References,
    double? EvaluationMs,
    double? OwnBuildMs,
    int? BuildHostOrder,
    bool Markup,
    string[] MarkupEvidence)
{
    public double? WeightMs { get; init; }
    public double FirstMs { get; init; }
}
