namespace ParallelLoad.Models;

internal sealed record PartitionPlan(
    string Status,
    int Workers,
    string[][] RequestedProjects,
    string[][] Closures,
    double[] PredictedGroupMs,
    int RepeatedProjects,
    string[] SharedCore,
    string[] RepeatedMarkup);
