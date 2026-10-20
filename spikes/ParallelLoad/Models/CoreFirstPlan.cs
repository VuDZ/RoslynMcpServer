namespace ParallelLoad.Models;

internal sealed record CoreFirstPlan(
    string Graph,
    string CostSource,
    string[] Core,
    string[][] Roots,
    string[][] Groups,
    double CoreCost,
    double TotalCost,
    double[] GroupCosts,
    bool DownwardClosed,
    bool SharedDirectoriesSafe)
{
    public string Partition { get; init; } = "greedy";
    public double BalanceEpsilon { get; init; } = 0.15;
    public double BalanceRatio { get; init; }
    public bool BalanceSatisfied { get; init; } = true;
    public string PlanningStatus { get; init; } = "ok";
    public string? PlanningReason { get; init; }
    public double SearchMs { get; init; }
    public int EvaluatedCandidates { get; init; }
    public bool SearchBudgetExpired { get; init; }
    public bool InfeasibilityProven { get; init; }
    public string? InterruptedStage { get; init; }
}
