using ParallelLoad.Models;

namespace ParallelLoad;

internal sealed class SamplerFinalization(Func<double, double, Task<ProcessMetrics>> stop)
{
    public string? Error { get; private set; }

    public Task<ProcessMetrics> StopAsync(double wallMs, double rootCpuMs) =>
        _completion ??= CompleteAsync(wallMs, rootCpuMs);

    private async Task<ProcessMetrics> CompleteAsync(double wallMs, double rootCpuMs)
    {
        try
        {
            return await stop(wallMs, rootCpuMs);
        }
        catch (Exception ex)
        {
            // Stopping is irreversible. Preserve its first failure rather than trying to recreate sampler.stop.
            Error = ex.ToString();
            return new ProcessMetrics(0, 0, 0, rootCpuMs, 0, 0, 0, 0, [$"Sampler finalization failed: {Error}"]);
        }
    }

    private Task<ProcessMetrics>? _completion;
}
