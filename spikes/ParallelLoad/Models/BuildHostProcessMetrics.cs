namespace ParallelLoad.Models;

internal sealed record BuildHostProcessMetrics(
    string Identity,
    double CpuMs,
    double FirstObservedMs,
    double LastObservedMs,
    double SampledLifetimeMs,
    double SampledZeroCpuMs,
    double? SampledZeroCpuShare,
    int Observations,
    string BoundaryMeaning);
