using System.Security.Cryptography;
using System.Text.Json;
using ParallelLoad.Models;

namespace ParallelLoad;

internal static class RefusalPolicy
{
    public static RefusalDecision Decide(RunOptions options, CoreFirstPlan plan, double planningMs)
    {
        var bytes = File.ReadAllBytes(options.BaselineEstimatePath!);
        using var document = JsonDocument.Parse(bytes);
        var baseline = document.RootElement;
        if (baseline.GetProperty("Status").GetString() != "ok" ||
            baseline.GetProperty("Mode").GetString() is not ("baseline" or "baseline-estimate") ||
            !baseline.GetProperty("Solution").GetString()!.EndsWith(Path.GetFileName(options.SolutionPath), StringComparison.OrdinalIgnoreCase) ||
            baseline.GetProperty("GlobalProperties").GetString() != options.GlobalProperties)
        {
            throw new InvalidDataException("Prediction requires a successful ordinary baseline of this series with matching solution and properties.");
        }
        return Decide(plan, baseline.GetProperty("WallMs").GetDouble(),
            baseline.GetProperty("Process").GetProperty("PeakBuildHostWorkingSet").GetInt64(), planningMs,
            options.ForceParallel, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    internal static RefusalDecision Decide(CoreFirstPlan plan, double baselineMs, long baselinePeakBytes,
        double planningMs, bool forceParallel, string baselineSha256 = "synthetic")
    {
        if (!double.IsFinite(baselineMs) || baselineMs <= 0 || baselinePeakBytes <= 0 ||
            !double.IsFinite(planningMs) || planningMs < 0)
        {
            throw new InvalidDataException("The baseline must have a positive wall time and sampled BuildHost memory.");
        }
        var workers = plan.Groups.Length;
        double? share = plan.TotalCost > 0 ? plan.CoreCost / plan.TotalCost : null;
        var decision = new RefusalDecision(null, [], baselineMs, baselinePeakBytes, null,
            baselinePeakBytes * (1 + workers / 4d), planningMs, share, workers, baselineSha256);
        var service = Math.Max(0, baselineMs - decision.H1Ms);
        double? prediction = share is { } coreShare && double.IsFinite(coreShare) && plan.GroupCosts.Sum() > 0
            ? planningMs + decision.PreparationMs + decision.H1Ms + coreShare * service +
                decision.Concurrency * (decision.ResidualMs + decision.Balance * (1 - coreShare) * service / workers) + decision.MergeMs
            : null;
        var reasons = new List<string>();
        if (plan.InfeasibilityProven) { reasons.Add("balance-infeasible"); }
        else if (plan.PlanningStatus != "ok" || !plan.BalanceSatisfied) { reasons.Add("planning-budget"); }
        if (prediction >= 0.9 * baselineMs) { reasons.Add("no-expected-gain"); }
        if (decision.PredictedPeakBytes > decision.MemoryBudgetBytes) { reasons.Add("memory-budget"); }
        var bypassed = forceParallel ? reasons.Where(reason => reason is "no-expected-gain" or "memory-budget").ToArray() : [];
        return decision with
        {
            Reason = reasons.Except(bypassed).FirstOrDefault(), BypassedReasons = bypassed, PredictedParallelMs = prediction,
            ModelUnavailableReason = plan.PlanningStatus == "ok" && prediction is null
                ? "The admissible plan has no positive non-core profile service cost; the frozen model is unavailable." : null,
        };
    }
}
