namespace ParallelLoad.Models;

internal sealed record CostModel(double HostMs, Dictionary<int, double> Competition, bool Validated);
