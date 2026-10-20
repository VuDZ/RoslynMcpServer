using ParallelLoad.Models;

namespace ParallelLoad;

internal static class FailureEvidenceCapture
{
    public static string CoreFirstStatus(bool cancelled, bool hasViolations, bool infeasible)
    {
        // Ownership violations remain explicit even when another group fails or cancellation races with the violation.
        return hasViolations ? "plan-violation" : cancelled ? "timeout" : infeasible ? "infeasible-search" : "failed";
    }

    public static async Task<CoreFirstFailureEvidence> CaptureAsync(
        Exception failure,
        SamplerFinalization finalization,
        double wallMs,
        double rootCpuMs)
    {
        var metrics = await finalization.StopAsync(wallMs, rootCpuMs);
        return new CoreFirstFailureEvidence(failure.ToString(), metrics, finalization.Error);
    }
}
