using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using ParallelLoad.Models;

namespace ParallelLoad;

[SupportedOSPlatform("windows")]
internal static class RefusalExperiment
{
    public static async Task<int> RunAsync(RunOptions options, string registration, TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;
        var start = timeProvider.GetTimestamp();
        var plan = CoreFirstPartition.Create(options);
        var planningMs = timeProvider.GetElapsedTime(start).TotalMilliseconds;
        var decision = RefusalPolicy.Decide(options, plan, planningMs);
        // Policy/baseline reads are wall work too, but are excluded from the search budget.
        planningMs = timeProvider.GetElapsedTime(start).TotalMilliseconds;
        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
        var attemptedPath = options.OutputPath + ".attempted.json";
        var ordinaryPath = options.OutputPath + ".ordinary.json";
        JsonObject? attempted = null;
        if (decision.ParallelAllowed)
        {
            await CoreFirstExperiment.RunAsync(options with { OutputPath = attemptedPath, RefusalPolicy = false }, registration,
                timeProvider, plan);
            attempted = JsonNode.Parse(File.ReadAllText(attemptedPath))!.AsObject();
        }
        JsonObject? ordinary = null;
        if (attempted is null || attempted["Status"]!.GetValue<string>() != "ok")
        {
            await LoadExperiment.RunAsync(options with
            {
                Mode = options.BinlogDirectory is null ? "baseline" : "baseline-binlog",
                Workers = 1, OutputPath = ordinaryPath, RefusalPolicy = false,
            }, registration, timeProvider);
            ordinary = JsonNode.Parse(File.ReadAllText(ordinaryPath))!.AsObject();
        }
        var result = Compose(plan, decision, planningMs, attempted, ordinary);
        result["Graph"] = options.Graph;
        result["Partition"] = options.Partition;
        result["ForceParallel"] = options.ForceParallel;
        result["DiagnosticRun"] = options.BinlogDirectory is not null;
        File.WriteAllText(options.OutputPath, result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"refusal-policy reason={decision.Reason ?? "parallel"} status={result["Status"]} report={options.OutputPath}");
        return result["Status"]!.GetValue<string>() == "ok" ? 0 : 2;
    }

    internal static JsonObject Compose(CoreFirstPlan plan, RefusalDecision decision, double planningMs,
        JsonObject? attempted, JsonObject? ordinary)
    {
        var result = (ordinary ?? attempted ?? throw new InvalidDataException("No load result was produced.")).DeepClone().AsObject();
        var attemptedFailed = attempted is not null && attempted["Status"]!.GetValue<string>() != "ok";
        var attemptedWall = attempted?["WallMs"]?.GetValue<double>() ?? 0;
        var ordinaryWall = ordinary?["WallMs"]?.GetValue<double>() ?? 0;
        result["Status"] = attemptedFailed ? attempted!["Status"]!.DeepClone() : result["Status"]!.DeepClone();
        if (decision.ModelUnavailableReason is not null)
        {
            result["Status"] = "model-unavailable";
            result["Error"] = decision.ModelUnavailableReason;
        }
        result["Mode"] = "core-first";
        result["Workers"] = decision.Workers;
        result["Plan"] = JsonSerializer.SerializeToNode(plan);
        result["PlannerDecision"] = JsonSerializer.SerializeToNode(decision);
        result["PlanningMs"] = planningMs;
        result["WallMs"] = planningMs + attemptedWall + ordinaryWall;
        result["FallbackKind"] = ordinary is null ? null : decision.ModelUnavailableReason is not null
            ? "model-unavailable" : attempted is null ? "pre-load-refusal" : "post-load-failure";
        result["WastedParallelMs"] = attemptedFailed ? attemptedWall : 0;
        result["OrdinaryFallbackMs"] = ordinaryWall;
        result["AttemptedParallel"] = attempted?.DeepClone();
        result["OrdinaryFallback"] = ordinary?.DeepClone();
        result["GateEligible"] = !attemptedFailed && result["Status"]!.GetValue<string>() == "ok";
        result["ForceParallel"] = decision.BypassedReasons.Length > 0;
        if (attemptedFailed)
        {
            result["Error"] = attempted!["Error"]?.DeepClone();
            result["PlanViolationCount"] = attempted["PlanViolationCount"]?.DeepClone();
            result["PlanViolations"] = attempted["PlanViolations"]?.DeepClone();
        }
        if (ordinary is not null && attempted is not null)
        {
            result["Process"] = CombineProcesses(attempted["Process"]!.AsObject(), ordinary["Process"]!.AsObject(),
                planningMs + attemptedWall + ordinaryWall);
        }
        return result;
    }

    private static JsonObject CombineProcesses(JsonObject attempted, JsonObject ordinary, double wallMs)
    {
        var result = ordinary.DeepClone().AsObject();
        foreach (var name in new[] { "PeakBuildHostWorkingSet", "PeakConcurrentBuildHosts", "MaxSampleGapMs" })
        {
            result[name] = Math.Max(Number(attempted[name]), Number(ordinary[name]));
        }
        foreach (var name in new[] { "BuildHostCount", "Samples", "RootCpuMs", "ChildCpuMs" })
        {
            result[name] = Number(attempted[name]) + Number(ordinary[name]);
        }
        result["Errors"] = new JsonArray(attempted["Errors"]!.AsArray().Concat(ordinary["Errors"]!.AsArray())
            .Select(node => node!.DeepClone()).ToArray());
        result["BuildHosts"] = new JsonArray(attempted["BuildHosts"]!.AsArray().Concat(ordinary["BuildHosts"]!.AsArray())
            .Select(node => node!.DeepClone()).ToArray());
        result["BusyCores"] = (result["RootCpuMs"]!.GetValue<double>() + result["ChildCpuMs"]!.GetValue<double>()) / wallMs;
        return result;
    }

    private static double Number(JsonNode? node) => JsonSerializer.Deserialize<double>(node!.ToJsonString());
}
