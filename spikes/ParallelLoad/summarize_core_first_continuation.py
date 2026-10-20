"""Task-07.1 evidence, using the byte-unchanged task-04 comparator."""
import argparse
import collections
import hashlib
import json
import math
import pathlib
import statistics

from compare import compare, differences
from summarize_core_first import summarize_combo


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def step_zero(artifacts):
    results = {}
    for solution, workers in (("files", 2), ("powertoys", 4)):
        paths = [artifacts / f"step0-{solution}-baseline-{run}.json" for run in (1, 2)]
        candidate_path = artifacts / f"step0-{solution}-core-first-xml-{workers}.json"
        baselines = [read(path) for path in paths]
        candidate = read(candidate_path)
        baseline_difference = differences(*baselines)
        natural = {(key, field): [before, after] for key, field, before, after in baseline_difference}
        comparison = compare(baselines[0], candidate, natural)
        valid = all(run.get("Status") == "ok" and run.get("WallMs", 0) > 0
                    and run.get("UniqueProjects", 0) > 0 and run.get("PlanViolationCount", 0) == 0
                    and not run.get("Process", {}).get("Errors", ["missing telemetry"])
                    and 0 < run.get("Process", {}).get("Samples", 0)
                    and run.get("Process", {}).get("MaxSampleGapMs", float("inf")) <= 1000
                    for run in [*baselines, candidate])
        directories = [candidate.get(field) for field in
                       ("OriginalSolutionDirectory", "Phase1SolutionDirectory", "GroupSolutionDirectory")]
        passed = (valid and not baseline_difference and not comparison["unexplained"]
                  and not comparison["natural"] and directories[0] and len(set(directories)) == 1)
        results[solution] = {
            "gatePassed": bool(passed), "validMeasurements": valid,
            "baselineDifferences": baseline_difference, "comparison": comparison,
            "unexplainedCounts": dict(collections.Counter(item[1] for item in comparison["unexplained"])),
            "solutionDirectories": directories,
            "phase1RequestedPaths": candidate.get("Phase1RequestedPaths", []),
            "runs": [{key: value for key, value in run.items() if key not in ("Projects", "Diagnostics")}
                     for run in [*baselines, candidate]],
            "sha256": {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
                       for path in [*paths, candidate_path]},
        }
    return {"gatePassed": all(result["gatePassed"] for result in results.values()), "solutions": results}


def combination(run):
    if run["Mode"] == "baseline":
        return "baseline"
    return (f"{run['Graph']}-{run.get('Partition', 'greedy')}-N{run['Workers']}"
            f"-depth{run.get('CoreDepth', 1)}-{run.get('Phase2', 'parallel')}")


def host_health(run):
    process = run.get("Process", {})
    hosts = process.get("BuildHosts", [])
    issues = []
    wall = run.get("WallMs")
    if wall is None or not math.isfinite(wall) or wall <= 0 or run.get("UniqueProjects", 0) <= 0:
        issues.append("Zero, nonfinite or absent load measurement")
    if process.get("Samples", 0) <= 0:
        issues.append("No telemetry samples")
    if process.get("BuildHostCount", 0) <= 0 or len(hosts) != process.get("BuildHostCount"):
        issues.append("Missing per-BuildHost telemetry or no observed host")
    for host in hosts:
        for field in ("CpuMs", "FirstObservedMs", "LastObservedMs", "SampledLifetimeMs", "SampledZeroCpuMs"):
            value = host.get(field)
            if value is None or not math.isfinite(value) or value < 0:
                issues.append(f"Invalid host {host.get('Identity')} {field}")
        share = host.get("SampledZeroCpuShare")
        if share is not None and (not math.isfinite(share) or not 0 <= share <= 1):
            issues.append("Invalid sampled zero-CPU share")
    return issues


def summarize_matrix(directory):
    inventory = {}
    hashes = {}
    for path in sorted(directory.glob("*.json")):
        if not path.name.startswith(("powertoys-", "files-")):
            continue
        run = read(path)
        if run.get("Mode") not in ("baseline", "core-first"):
            continue
        try:
            index = int(path.stem.rsplit("-", 1)[1])
        except ValueError:
            continue
        solution = path.name.split("-", 1)[0]
        combo = combination(run)
        entries = inventory.setdefault(solution, {}).setdefault(combo, {})
        if index in entries:
            raise ValueError(f"Duplicate {solution}/{combo}/run {index}")
        entries[index] = run
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    result = {"solutions": {}, "sha256": hashes, "missing": [], "go": False}
    for solution in ("powertoys", "files"):
        combos = inventory.get(solution, {})
        expected = ["baseline"]
        if solution == "powertoys":
            expected += [f"xml-{partition}-N{workers}-depth1-parallel"
                         for partition in ("greedy", "cluster") for workers in (2, 4, 8)]
            expected += ["snapshot-load-cluster-N4-depth1-parallel"]
        else:
            expected += [f"xml-cluster-N{workers}-depth1-parallel" for workers in (2, 4)]
        for combo in expected:
            for index in range(4):
                if index not in combos.get(combo, {}):
                    result["missing"].append(f"{solution}/{combo}/run {index}")
        baseline = combos.get("baseline", {})
        if any(index not in baseline for index in (1, 2, 3)):
            result["solutions"][solution] = {"combinations": {}, "oracleAvailable": False}
            continue
        baseline_differences = differences(baseline[1], baseline[2])
        natural = {(key, field): [before, after] for key, field, before, after in baseline_differences}
        reference = dict(baseline[1])
        reference["BaselineMedianMs"] = statistics.median(baseline[index]["WallMs"] for index in (1, 2, 3))
        oracle_valid = all(baseline[index].get("Status") == "ok" and not host_health(baseline[index])
                           for index in (1, 2, 3))
        entry = {"oracleAvailable": oracle_valid, "baselineMs": reference["BaselineMedianMs"],
                 "baselineDifferences": baseline_differences, "combinations": {}}
        for combo, runs in combos.items():
            measured = [runs[index] for index in (1, 2, 3) if index in runs]
            if not measured:
                continue
            item = summarize_combo(reference, measured, natural)
            item["missingMeasuredRuns"] = [index for index in (1, 2, 3) if index not in runs]
            item["missingWarmup"] = 0 not in runs
            item["hostTelemetryIssues"] = [host_health(run) if run.get("Status") == "ok" else [] for run in measured]
            item["completeValidRuns"] &= (not item["missingMeasuredRuns"] and not item["missingWarmup"]
                                          and not any(item["hostTelemetryIssues"]) and oracle_valid)
            item["workers"] = measured[0].get("Workers")
            item["partition"] = measured[0].get("Partition")
            item["graph"] = measured[0].get("Graph")
            item["coreDepth"] = measured[0].get("CoreDepth", 1)
            item["phase2"] = measured[0].get("Phase2", "parallel")
            item["runMetrics"] = [{key: value for key, value in run.items() if key not in ("Projects", "Diagnostics")}
                                  for run in measured]
            item["goEligibleSpeed"] = (solution == "powertoys" and combo != "baseline"
                and item["phase2"] == "parallel" and item["workers"] <= 4 and item["completeValidRuns"]
                and item["wallRatio"] is not None and item["wallRatio"] <= .75)
            entry["combinations"][combo] = item
        result["solutions"][solution] = entry
    power = result["solutions"].get("powertoys", {}).get("combinations", {})
    n4 = [(name, item) for name, item in power.items() if name != "baseline" and item.get("workers") == 4
          and item.get("phase2") == "parallel" and item.get("coreDepth") == 1
          and item["completeValidRuns"] and not item["unexplainedCounts"]]
    best_n4 = min(n4, key=lambda pair: pair[1]["wallMs"], default=None)
    result["bestN4"] = best_n4[0] if best_n4 else None
    if best_n4:
        isolated_name = best_n4[0].replace("-parallel", "-isolated")
        result["requiredIsolated"] = isolated_name
        if isolated_name not in power:
            result["missing"].append("powertoys/" + isolated_name)
    candidates = [(name, item) for name, item in power.items() if name != "baseline" and item.get("workers", 99) <= 4
                  and item.get("phase2") == "parallel" and item.get("coreDepth") == 1
                  and item["completeValidRuns"] and not item["unexplainedCounts"]]
    best = min(candidates, key=lambda pair: pair[1]["wallMs"], default=None)
    result["bestNAtMost4"] = best[0] if best else None
    result["recursiveRequired"] = bool(best and best[1]["phase1Ms"] / best[1]["wallMs"] > .25)
    if result["recursiveRequired"]:
        recursive_name = best[0].replace("depth1", "depth2")
        result["requiredRecursive"] = recursive_name
        if recursive_name not in power:
            result["missing"].append("powertoys/" + recursive_name)
    all_items = [item for entry in result["solutions"].values() for item in entry["combinations"].values()]
    result["globalCorrectness"] = bool(all_items) and not result["missing"] and all(
        item["completeValidRuns"] and not item["unexplainedCounts"] and item["planViolationCount"] == 0
        and item["peakMemoryMax"] <= 4_000_000_000 for item in all_items)
    result["go"] = result["globalCorrectness"] and any(item.get("goEligibleSpeed") for item in all_items)
    result["model"] = model_from_isolated(result)
    return result


def model_from_isolated(summary):
    name = summary.get("bestN4")
    combos = summary["solutions"].get("powertoys", {}).get("combinations", {})
    isolated = combos.get(name.replace("-parallel", "-isolated")) if name else None
    if not name or not isolated or not isolated["completeValidRuns"] or isolated["unexplainedCounts"]:
        return {"available": False, "reason": "No equivalent complete isolated run of the selected N=4 plan", "rows": []}
    parallel = combos[name]
    groups = []
    for index, (concurrent, alone, profile) in enumerate(zip(parallel["groupMs"], isolated["groupMs"], parallel["groupCosts"])):
        first = [run.get("Phase2FirstProgressMs", [])[index] for run in isolated["runMetrics"]
                 if len(run.get("Phase2FirstProgressMs", [])) > index]
        groups.append({"index": index, "parallelMs": concurrent, "isolatedMs": alone, "profileWeightMs": profile,
                       "isolatedMinusProfileMs": alone - profile,
                       "firstProgressProxyMs": statistics.median(value for value in first if value is not None) if any(
                           value is not None for value in first) else None,
                       "concurrencyRatio": concurrent / alone if alone > 0 else None})
    usable = [group for group in groups if group["isolatedMs"] > 0 and group["firstProgressProxyMs"] is not None]
    if not usable:
        return {"available": False, "reason": "No group with measured cold-start proxy", "rows": [], "groups": groups}
    profile_costs = parallel["groupCosts"]
    if any(not math.isfinite(cost) or cost < 0 for cost in profile_costs):
        return {"available": False, "reason": "Non-core profile costs are not finite nonnegative values",
                "rows": [], "groups": groups}
    noncore_cost = sum(profile_costs)
    if noncore_cost == 0:
        return {"available": False,
                "reason": "Non-core profile cost is zero; no calibrated balance factor can be computed",
                "rows": [], "groups": groups}
    h1 = parallel["phase1FirstProgressMs"] or 0
    h2 = statistics.median(group["firstProgressProxyMs"] for group in usable)
    baseline_ms = summary["solutions"]["powertoys"]["baselineMs"]
    total_profile_cost = parallel["plan"].get("TotalCost")
    if not total_profile_cost or not math.isfinite(total_profile_cost) or total_profile_cost <= 0 or baseline_ms <= h1:
        return {"available": False, "reason": "No positive complete profile service cost for baseline normalization",
                "rows": [], "groups": groups}
    profile_scale = (baseline_ms - h1) / total_profile_cost
    for group in groups:
        group["baselineNormalizedProfileMs"] = profile_scale * group["profileWeightMs"]
        group["isolatedMinusNormalizedProfileMs"] = group["isolatedMs"] - group["baselineNormalizedProfileMs"]
    fixed_residual = statistics.median(group["isolatedMinusNormalizedProfileMs"] for group in usable)
    if not math.isfinite(fixed_residual) or fixed_residual <= 0:
        return {"available": False, "reason": "The measured residual does not support a positive additive group-overhead model",
                "profileBaselineScale": profile_scale, "rows": [], "groups": groups}
    competition = statistics.median(group["concurrencyRatio"] for group in usable)
    balance = max(profile_costs) / (noncore_cost / 4)
    planning = parallel["planningMs"] or 0
    merge = parallel["mergeMs"] or 0
    preparation = parallel["aggregatorPreparationMs"] or 0

    def predict(baseline, core_share):
        work = max(0, baseline - h1)
        return planning + preparation + h1 + core_share * work + competition * (
            fixed_residual + balance * (1 - core_share) * work / 4) + merge

    rows = []
    for baseline_seconds in (120, 300):
        for core_share in (.05, .22, .5):
            estimate = predict(baseline_seconds * 1000, core_share)
            rows.append({"baselineSeconds": baseline_seconds, "coreShare": core_share,
                         "predictedSeconds": estimate / 1000, "predictedSpeedup": baseline_seconds * 1000 / estimate})
    return {"available": True, "plan": name, "groups": groups,
            "h1FirstProgressProxyMs": h1, "h2IsolatedFirstProgressProxyMs": h2,
            "groupFixedResidualProxyMs": fixed_residual, "profileBaselineScale": profile_scale,
            "concurrencyRatio": competition, "balanceFactor": balance, "planningMs": planning,
            "preparationMs": preparation, "mergeMs": merge, "rows": rows,
            "calibration": {"predictedWallMs": predict(baseline_ms, parallel["coreCostShare"]),
                            "observedWallMs": parallel["wallMs"],
                            "relativeError": predict(baseline_ms, parallel["coreCostShare"]) / parallel["wallMs"] - 1},
            "limitations": "Conditional forecast, not measurements or held-out validation. First progress includes first project work, not pure process startup. Fixed residual is isolated time minus profile weight normalized to the current baseline service time; it includes cold loader, aggregator, ordering and context differences and is not a causal estimate of process startup. Forecast assumes this residual and the parallel/isolated ratio stay constant. Parallel/isolated ratios measure total concurrency penalty and cannot identify antivirus or import-cache causes."}


def compact_summary(summary):
    gate = summary.get("step0", summary)
    compact = {"step0": {"gatePassed": gate["gatePassed"], "solutions": {}}}
    for solution, item in gate["solutions"].items():
        compact["step0"]["solutions"][solution] = {
            "gatePassed": item["gatePassed"], "baselineDifferenceCount": len(item["baselineDifferences"]),
            "equivalence": item["comparison"]["verdict"], "unexplainedCounts": item["unexplainedCounts"],
            "explainedCaseCount": len(item["comparison"]["explainedCase"]),
            "solutionDirectories": item["solutionDirectories"], "sha256": item["sha256"],
        }
    if "matrix" not in summary:
        return compact
    matrix = summary["matrix"]
    compact["matrix"] = {key: value for key, value in matrix.items() if key != "solutions"}
    compact["matrix"]["solutions"] = {}
    fields = ("oracleAvailable", "baselineMs")
    combo_fields = ("statusCounts", "completeValidRuns", "wallMs", "speedup", "wallRatio", "phase1Ms",
                    "phase1FirstProgressMs", "phase2FirstProgressMs", "criticalGroupMs", "groupMs", "mergeMs",
                    "planningMs", "aggregatorPreparationMs", "peakMemoryMedian", "peakMemoryMax", "busyCores",
                    "buildHostCount", "planViolationCount", "equivalence", "unexplainedCounts", "coreCount",
                    "coreCostShare", "groupCounts", "groupCosts", "samplerErrors", "maxSampleGapMs",
                    "workers", "partition", "graph", "coreDepth", "phase2", "goEligibleSpeed")
    for solution, entry in matrix["solutions"].items():
        compact_entry = {field: entry.get(field) for field in fields}
        compact_entry["baselineDifferenceCount"] = len(entry.get("baselineDifferences", []))
        compact_entry["combinations"] = {}
        for name, item in entry["combinations"].items():
            row = {field: item.get(field) for field in combo_fields}
            row["balanceRatio"] = item["plan"].get("BalanceRatio") if item.get("plan") else None
            row["planningReasons"] = sorted({run.get("Plan", {}).get("PlanningReason") for run in item["runMetrics"]
                                             if run.get("Plan") and run["Plan"].get("PlanningReason")})
            row["recursiveSubphases"] = [
                {key: recursive.get(key) for key in ("LowerCoreMs", "LowerCoreFirstProgressMs", "MergeMs", "Violations")}
                | {"groupMs": [group["ElapsedMs"] for group in recursive.get("Groups", [])],
                   "groupStatuses": [group["Status"] for group in recursive.get("Groups", [])],
                   "lowerCoreCount": len(recursive.get("Plan", {}).get("Core", []))}
                for run in item["runMetrics"] if (recursive := run.get("RecursiveCore")) is not None]
            hosts = [host for run in item["runMetrics"] for host in run.get("Process", {}).get("BuildHosts", [])]
            shares = [host["SampledZeroCpuShare"] for host in hosts if host.get("SampledZeroCpuShare") is not None]
            row["hostTelemetryRange"] = {
                "measuredHostObservations": len(hosts),
                "cpuMsMin": min((host["CpuMs"] for host in hosts), default=None),
                "cpuMsMax": max((host["CpuMs"] for host in hosts), default=None),
                "zeroCpuShareMin": min(shares, default=None), "zeroCpuShareMax": max(shares, default=None),
            }
            compact_entry["combinations"][name] = row
        compact["matrix"]["solutions"][solution] = compact_entry
    return compact


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--artifacts", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--matrix", type=pathlib.Path)
    parser.add_argument("--compact-output", type=pathlib.Path)
    options = parser.parse_args()
    summary = step_zero(options.artifacts)
    if options.matrix is not None:
        summary = {"step0": summary, "matrix": summarize_matrix(options.matrix)}
        summary["matrix"]["go"] &= summary["step0"]["gatePassed"]
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    if options.compact_output is not None:
        options.compact_output.parent.mkdir(parents=True, exist_ok=True)
        options.compact_output.write_text(json.dumps(compact_summary(summary), ensure_ascii=False, indent=2), encoding="utf-8")
    gate = summary.get("step0", summary)
    print("Step zero: " + ("PASS" if gate["gatePassed"] else "NO-GO"))
    for solution, result in gate["solutions"].items():
        print(f"  {solution}: {result['comparison']['verdict']}; {result['unexplainedCounts']}")
    if "matrix" in summary:
        print(f"Matrix go={summary['matrix']['go']}; missing={summary['matrix']['missing']}")


if __name__ == "__main__":
    main()
