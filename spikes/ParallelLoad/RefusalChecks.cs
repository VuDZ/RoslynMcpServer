using System.Text.Json;
using System.Text.Json.Nodes;
using ParallelLoad.Models;

namespace ParallelLoad;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class RefusalChecks
{
    public static Task<int> RunAsync()
    {
        CheckReasons();
        CheckSearch();
        CheckFallback();
        Console.WriteLine("Refusal self-checks passed: reasons, force scope, budget, exhaustive proof, deterministic search, failed fallback evidence.");
        return Task.FromResult(0);
    }

    private static void CheckReasons()
    {
        var plan = new CoreFirstPlan("synthetic", "profile", [], [["A"], ["B"]], [["A"], ["B"]],
            0, 100, [50, 50], true, true) { BalanceSatisfied = true };
        Require(RefusalPolicy.Decide(plan, 100_000, 100_000_000, 10, false).Reason is null, "A beneficial balanced plan must run.");
        Require(RefusalPolicy.Decide(plan, 1000, 100_000_000, 10, false).Reason == "no-expected-gain", "Expected-gain refusal missing.");
        Require(RefusalPolicy.Decide(plan, 100_000, 3_000_000_000, 10, false).Reason == "memory-budget", "Memory refusal missing.");
        var infeasible = plan with { BalanceSatisfied = false, PlanningStatus = "balance-infeasible", InfeasibilityProven = true };
        Require(RefusalPolicy.Decide(infeasible, 1000, 3_000_000_000, 10, true).Reason == "balance-infeasible",
            "Force must not bypass a proven infeasible balance.");
        var budget = plan with { BalanceSatisfied = false, PlanningStatus = "planning-budget" };
        Require(RefusalPolicy.Decide(budget, 1000, 3_000_000_000, 10, true).Reason == "planning-budget",
            "Force must not bypass failure to find an admissible plan.");
        var forced = RefusalPolicy.Decide(plan, 1000, 3_000_000_000, 10, true);
        Require(forced.Reason is null && forced.BypassedReasons.SequenceEqual(["no-expected-gain", "memory-budget"]),
            "Force must retain both bypassed diagnostic reasons.");
        var unavailable = RefusalPolicy.Decide(plan with { TotalCost = 0, GroupCosts = [0, 0] }, 100_000, 100_000_000, 10, true);
        Require(unavailable.Reason is null && unavailable.ModelUnavailableReason is not null && !unavailable.ParallelAllowed,
            "An admissible zero-cost plan must retain model unavailability separately from the four refusal reasons.");
    }

    private static void CheckSearch()
    {
        var paths = Enumerable.Range(0, 6).Select(index => Path.GetFullPath($"Synthetic/{index}/{index}.csproj")).ToArray();
        var graph = paths.ToDictionary(path => path, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        graph[paths[0]].Add(paths[4]);
        graph[paths[1]].Add(paths[4]);
        graph[paths[2]].Add(paths[5]);
        graph[paths[3]].Add(paths[5]);
        var costs = paths.ToDictionary(path => path, _ => 1d, StringComparer.OrdinalIgnoreCase);
        var first = FastCoreFirstPlanner.Create(graph, costs, 2, "synthetic", "profile", "cluster");
        var second = FastCoreFirstPlanner.Create(graph, costs, 2, "synthetic", "profile", "cluster");
        Require(first.PlanningStatus == "ok" && first.BalanceSatisfied && first.DownwardClosed && first.SharedDirectoriesSafe,
            "The fast search must retain an admissible closed plan.");
        Require(JsonSerializer.Serialize(first with { SearchMs = 0 }) == JsonSerializer.Serialize(second with { SearchMs = 0 }),
            "Deterministic search must reproduce ownership and candidate count.");
        var singleGraph = new Dictionary<string, HashSet<string>> { [paths[0]] = [] };
        var timedOut = FastCoreFirstPlanner.Create(singleGraph, new Dictionary<string, double> { [paths[0]] = 1 },
            2, "synthetic", "profile", "cluster", new ExpiredClock(), 500);
        Require(timedOut.PlanningStatus == "planning-budget" && timedOut.SearchBudgetExpired && !timedOut.InfeasibilityProven,
            "A budget timeout must not become a proof of infeasibility.");
        var impossible = FastCoreFirstPlanner.Create(singleGraph, new Dictionary<string, double> { [paths[0]] = 1 },
            2, "synthetic", "profile", "cluster");
        Require(impossible.PlanningStatus == "balance-infeasible" && impossible.InfeasibilityProven,
            "A complete enumeration must distinguish proven infeasibility.");
        CheckInterruptions(graph, costs);
        var sharedDirectoryGraph = new Dictionary<string, HashSet<string>>
        {
            [Path.GetFullPath("Synthetic/Shared/A.csproj")] = [],
            [Path.GetFullPath("Synthetic/Shared/B.csproj")] = [],
        };
        var allCore = FastCoreFirstPlanner.Create(sharedDirectoryGraph,
            sharedDirectoryGraph.Keys.ToDictionary(path => path, _ => 1d), 2, "synthetic", "profile", "cluster");
        Require(allCore.PlanningStatus == "ok" && !allCore.InfeasibilityProven && allCore.Core.Length == 2 &&
            allCore.BalanceSatisfied && allCore.BalanceRatio == 1 && allCore.GroupCosts.All(cost => cost == 0),
            "All-core promotion must preserve the defined zero-over-zero balance convention.");
    }

    private static void CheckInterruptions(Dictionary<string, HashSet<string>> graph, Dictionary<string, double> costs)
    {
        foreach (var stage in new[] { "preparation-closures", "preparation-affinity", "seed", "evaluation", "materialization-completion" })
        {
            var clock = new ManualClock();
            var interrupted = FastCoreFirstPlanner.Create(graph, costs, 2, "synthetic", "profile", "cluster", clock, 500,
                checkpoint: current => { if (current == stage) { clock.Expire(); } });
            Require(interrupted.PlanningStatus == "planning-budget" && !interrupted.InfeasibilityProven &&
                interrupted.SearchBudgetExpired && interrupted.InterruptedStage == stage,
                "A candidate completed after the deadline must not be accepted: " + stage);
        }
        var paths = Enumerable.Range(0, 12).Select(index => Path.GetFullPath($"Synthetic/Ranking/{index}/{index}.csproj")).ToArray();
        var rankingGraph = paths.ToDictionary(path => path, _ => new HashSet<string>());
        var rankingCosts = paths.ToDictionary(path => path, _ => 1d);
        foreach (var stage in new[] { "swap-ranking", "bundle-ranking" })
        {
            var clock = new ManualClock();
            var retained = FastCoreFirstPlanner.Create(rankingGraph, rankingCosts, 2, "synthetic", "profile", "cluster", clock, 500,
                checkpoint: current => { if (current == stage) { clock.Expire(); } });
            Require(retained.PlanningStatus == "ok" && retained.SearchBudgetExpired && retained.InterruptedStage == stage &&
                retained.BalanceSatisfied && retained.Core.Length == 0 && retained.GroupCosts.SequenceEqual([6d, 6d]),
                "Ranking must stop on deadline and retain the previously completed seed: " + stage);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CheckFallback()
    {
        var plan = new CoreFirstPlan("synthetic", "profile", [], [["A"], ["B"]], [["A"], ["B"]], 0, 100, [50, 50], true, true);
        var decision = RefusalPolicy.Decide(plan, 100_000, 1000, 10, false);
        foreach (var status in new[] { "failed", "timeout", "plan-violation" })
        {
            var attempted = Row(status, 200);
            attempted["Error"] = "Injected failure";
            attempted["PlanViolationCount"] = status == "plan-violation" ? 1 : 0;
            attempted["PlanViolations"] = new JsonArray("Injected violation");
            var result = RefusalExperiment.Compose(plan, decision, 10, attempted, Row("ok", 100));
            Require(result["Status"]!.GetValue<string>() == status && !result["GateEligible"]!.GetValue<bool>() &&
                result["WallMs"]!.GetValue<double>() == 310 && result["WastedParallelMs"]!.GetValue<double>() == 200 &&
                result["AttemptedParallel"]!["Projects"]!["partial"]!.GetValue<string>() == "retained" &&
                result["Error"]!.GetValue<string>() == "Injected failure", "Failed attempted load evidence must survive ordinary success.");
        }
        var refused = RefusalExperiment.Compose(plan, decision with { Reason = "planning-budget" }, 10, null, Row("ok", 100));
        Require(refused["WallMs"]!.GetValue<double>() == 110 && refused["GateEligible"]!.GetValue<bool>() &&
            refused["FallbackKind"]!.GetValue<string>() == "pre-load-refusal", "Pre-load refusal must include planning and ordinary wall.");
        var unavailable = RefusalExperiment.Compose(plan, decision with { ModelUnavailableReason = "Injected unavailable model" },
            10, null, Row("ok", 100));
        Require(unavailable["Status"]!.GetValue<string>() == "model-unavailable" && !unavailable["GateEligible"]!.GetValue<bool>() &&
            unavailable["FallbackKind"]!.GetValue<string>() == "model-unavailable",
            "Ordinary success must not convert model unavailability into a permitted refusal.");
    }

    private static JsonObject Row(string status, double wall) => new()
    {
        ["Status"] = status, ["WallMs"] = wall, ["Projects"] = new JsonObject { ["partial"] = "retained" },
        ["Process"] = new JsonObject
        {
            ["PeakBuildHostWorkingSet"] = 1000, ["PeakConcurrentBuildHosts"] = 1, ["MaxSampleGapMs"] = 10,
            ["BuildHostCount"] = 1, ["Samples"] = 1, ["RootCpuMs"] = 10, ["ChildCpuMs"] = 10,
            ["Errors"] = new JsonArray(), ["BuildHosts"] = new JsonArray(),
        },
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }

    private sealed class ExpiredClock : TimeProvider
    {
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Add(ref _timestamp, 501);
        private long _timestamp;
    }

    private sealed class ManualClock : TimeProvider
    {
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _timestamp;
        public void Expire() => _timestamp = 500;
        private long _timestamp;
    }
}
