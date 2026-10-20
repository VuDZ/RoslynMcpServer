using ParallelLoad.Models;

namespace ParallelLoad;

internal sealed class BuildHostObservation(double firstObservedMs, double cpuMs)
{
    public BuildHostProcessMetrics Capture(string identity, double totalCpuMs)
    {
        var lifetime = _lastObservedMs - _firstObservedMs;
        return new BuildHostProcessMetrics(identity, totalCpuMs, _firstObservedMs, _lastObservedMs,
            lifetime, _zeroCpuMs, lifetime > 0 ? _zeroCpuMs / lifetime : null, _observations,
            "First/last successful 100ms polling observations; start and exit boundaries are censored. CPU is a sampled lower bound; zero-CPU share covers observed intervals only, not all waiting or I/O.");
    }

    public void Observe(double observedMs, double currentCpuMs)
    {
        if (currentCpuMs == _previousCpuMs)
        {
            _zeroCpuMs += Math.Max(0, observedMs - _lastObservedMs);
        }

        _previousCpuMs = currentCpuMs;
        _lastObservedMs = observedMs;
        _observations++;
    }

    private readonly double _firstObservedMs = firstObservedMs;
    private double _lastObservedMs = firstObservedMs;
    private double _previousCpuMs = cpuMs;
    private double _zeroCpuMs;
    private int _observations = 1;
}
