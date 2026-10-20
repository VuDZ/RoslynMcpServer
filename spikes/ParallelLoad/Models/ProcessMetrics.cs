namespace ParallelLoad.Models;

internal sealed record ProcessMetrics(
    int BuildHostCount,
    int PeakConcurrentBuildHosts,
    long PeakBuildHostWorkingSet,
    double RootCpuMs,
    double ChildCpuMs,
    double BusyCores,
    double MaxSampleGapMs,
    int Samples,
    string[] Errors)
{
    public BuildHostProcessMetrics[] BuildHosts { get; init; } = [];
}
