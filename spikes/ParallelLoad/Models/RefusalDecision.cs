namespace ParallelLoad.Models;

internal sealed record RefusalDecision(string? Reason, string[] BypassedReasons, double BaselineMs,
    long BaselinePeakBytes, double? PredictedParallelMs, double PredictedPeakBytes, double PlanningMs,
    double? CoreShare, int Workers, string BaselineSha256)
{
    public string? ModelUnavailableReason { get; init; }
    public bool ParallelAllowed => Reason is null && ModelUnavailableReason is null;
    public string Model => "task071-frozen-depth1";
    public double H1Ms => 592.9752;
    public double ResidualMs => 4947.748723477786;
    public double Concurrency => 1.1756123129598963;
    public double Balance => 1.1359945429941485;
    public double PreparationMs => 5.5108;
    public double MergeMs => 120.3719;
    public double MemoryBudgetBytes => 4_000_000_000;
    public string MemoryFormula => "baseline summed BuildHost working-set peak * (1 + workers / 4.0); heuristic, not an upper bound";
}
