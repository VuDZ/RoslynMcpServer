"""Frozen task-07.2 analysis. Comparator and earlier evidence remain unchanged."""
import argparse
import collections
import hashlib
import json
import math
import pathlib
import re
import statistics

from compare import compare, differences
from summarize_core_first import summarize_combo, measurement_issues
from summarize_core_first_continuation import host_health


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def parallel_diagnostic_issues(run):
    attempted = run.get("AttemptedParallel")
    if (run.get("Mode") != "core-first" or run.get("Workers") != 4 or run.get("FallbackKind") is not None
            or not attempted or attempted.get("Status") != "ok" or attempted.get("Workers") != 4
            or attempted.get("Mode") != "core-first" or attempted.get("PlanViolationCount", 0)
            or not attempted.get("DiagnosticRun") or run.get("PlannerDecision", {}).get("Reason")
            or run.get("PlannerDecision", {}).get("ModelUnavailableReason")):
        return ["Required diagnostic is not a successful actual parallel N4 load"]
    roots = attempted.get("Plan", {}).get("Roots", [])
    outcomes = attempted.get("GroupOutcomes", [])
    if len(roots) != 4 or len(outcomes) != 4 or {group.get("Index") for group in outcomes} != set(range(4)):
        return ["Missing N4 plan/group outcome inventory"]
    files = attempted.get("BinlogFiles", [])
    issues = []
    if not attempted.get("Phase1RequestedPaths") or not any(re.fullmatch(r"phase1(?:-\d+)?\.binlog", name) for name in files):
        issues.append("Missing phase1 load/binlog inventory")
    for index, requests in enumerate(roots):
        outcome = next(group for group in outcomes if group["Index"] == index)
        if requests and (outcome.get("Status") != "ok" or not any(
                re.fullmatch(rf"group-{index}(?:-\d+)?\.binlog", name) for name in files)):
            issues.append(f"Missing successful nonempty group {index} load/binlog")
    if attempted.get("BinlogCount") != len(files) or attempted.get("BinlogBytes", 0) <= 0:
        issues.append("Invalid binlog count/size inventory")
    bypassed = run.get("PlannerDecision", {}).get("BypassedReasons", [])
    if bypassed and (not run.get("ForceParallel") or any(reason not in ("no-expected-gain", "memory-budget") for reason in bypassed)):
        issues.append("Economic refusal diagnostic was not explicitly forced within permitted scope")
    return issues


def baseline_estimate(paths):
    runs = [read(path) for path in paths]
    if len(runs) != 3 or any(run.get("Status") != "ok" or run.get("Mode") != "baseline"
                             or run.get("DiagnosticRun") or host_health(run) or measurement_issues(run) for run in runs):
        raise ValueError("Estimate requires three successful measured ordinary runs with healthy telemetry")
    for field in ("Solution", "Registration", "GlobalProperties", "Properties", "LocalSdk", "DotnetHost"):
        if len({json.dumps(run.get(field), sort_keys=True) for run in runs}) != 1:
            raise ValueError(f"Baseline context differs: {field}")
    if any(differences(runs[0], run) for run in runs[1:]):
        raise ValueError("Baseline snapshots differ; estimate has no stable same-series oracle")
    return {
        "Status": "ok", "Mode": "baseline-estimate", "Solution": runs[0]["Solution"],
        "GlobalProperties": runs[0]["GlobalProperties"], "Registration": runs[0]["Registration"],
        "WallMs": statistics.median(run["WallMs"] for run in runs),
        "Process": {"PeakBuildHostWorkingSet": max(run["Process"]["PeakBuildHostWorkingSet"] for run in runs)},
        "Sources": {path.name: sha(path) for path in paths},
        "Formula": "median wall of measured baseline 1/2/3; maximum sampled summed BuildHost working-set peak among those runs",
    }


def summarize(directory):
    inventory = {}
    hashes = {}
    diagnostics = {}
    for path in sorted(directory.glob("*.json")):
        if path.name.endswith((".attempted.json", ".ordinary.json")):
            continue
        if not (re.fullmatch(r"diagnostic-(?:powertoys|roslyn)-(?:baseline|cluster-N4)\.json", path.name) or re.fullmatch(
                r"(?:powertoys|files|roslyn)-(?:baseline|cluster-N\d+|greedy-N\d+)(?:-force)?-[0-3]\.json", path.name)):
            continue
        run = read(path)
        if not isinstance(run, dict) or run.get("Mode") not in ("baseline", "baseline-binlog", "core-first"):
            continue
        if path.name.startswith("diagnostic-"):
            diagnostics[path.stem] = run
            hashes[path.name] = sha(path)
            continue
        try:
            solution, rest = path.stem.split("-", 1)
            combo, index = rest.rsplit("-", 1)
            index = int(index)
        except ValueError:
            continue
        if solution not in ("powertoys", "files", "roslyn") or not 0 <= index <= 3:
            continue
        if run.get("DiagnosticRun"):
            raise ValueError(f"Binlog diagnostic must not enter medians: {path.name}")
        entries = inventory.setdefault(solution, {}).setdefault(combo, {})
        if index in entries:
            raise ValueError(f"Duplicate inventory: {path.name}")
        entries[index] = run
        hashes[path.name] = sha(path)
    result = {"go": False, "missing": [], "solutions": {}, "diagnostics": {}, "sha256": hashes}
    details = {}
    correctness = True
    memory = True
    refusal = True
    for solution, expected in (("powertoys", ["baseline", "cluster-N4"]),
                               ("files", ["baseline", "cluster-N2"]),
                               ("roslyn", ["baseline", "cluster-N4", "cluster-N8", "greedy-N4"])):
        combos = inventory.get(solution, {})
        if solution == "roslyn":
            expected += [combo + "-force" for combo in expected[1:]
                         if any(run.get("PlannerDecision", {}).get("Reason") for run in combos.get(combo, {}).values())]
        for combo in expected:
            for index in range(4):
                if index not in combos.get(combo, {}):
                    result["missing"].append(f"{solution}/{combo}/{index}")
        baseline = combos.get("baseline", {})
        available = all(index in baseline and baseline[index].get("Status") == "ok"
                        and not host_health(baseline[index]) for index in (1, 2, 3))
        baseline_diff = differences(baseline[1], baseline[2]) + differences(baseline[1], baseline[3]) if available else []
        available &= not baseline_diff
        baseline_ms = statistics.median(baseline[index]["WallMs"] for index in (1, 2, 3)) if available else None
        oracle = dict(baseline[1], BaselineMedianMs=baseline_ms) if available else None
        entry = {"oracleAvailable": available, "baselineMs": baseline_ms,
                 "baselineDifferenceCount": len(baseline_diff), "combinations": {}}
        result["solutions"][solution] = entry
        correctness &= available
        for combo in sorted(set(expected) | combos.keys()):
            runs = [combos.get(combo, {}).get(index) for index in (1, 2, 3)]
            if not all(runs) or not available:
                entry["combinations"][combo] = {"completeValidRuns": False, "statusCounts":
                    dict(collections.Counter(run.get("Status", "missing") if run else "missing" for run in runs)),
                    "reason": "missing measured row or unavailable baseline oracle"}
                correctness = False
                continue
            summary = summarize_combo(oracle, runs, {})
            comparisons = summary.pop("comparisons")
            details[solution + "/" + combo] = comparisons
            attempted_comparisons = [compare(oracle, run["AttemptedParallel"], {})
                if (run.get("AttemptedParallel") or {}).get("Projects") else None for run in runs]
            ordinary_comparisons = [compare(oracle, run["OrdinaryFallback"], {})
                if (run.get("OrdinaryFallback") or {}).get("Projects") else None for run in runs]
            details[solution + "/" + combo + "/attempted"] = attempted_comparisons
            details[solution + "/" + combo + "/ordinaryFallback"] = ordinary_comparisons
            summary.pop("planViolations")
            summary.pop("plan")
            summary.pop("additionalCorePaths")
            healthy = not any(host_health(run) for run in runs)
            final_success = all(run.get("GateEligible", True) and run.get("FallbackKind") != "post-load-failure"
                                for run in runs)
            attempted_success = all(not run.get("AttemptedParallel") or (
                run["AttemptedParallel"].get("Status") == "ok" and
                run["AttemptedParallel"].get("PlanViolationCount", 0) == 0 and
                all(group.get("Status") in ("ok", "empty") for group in run["AttemptedParallel"].get("GroupOutcomes", [])))
                for run in runs)
            summary["completeValidRuns"] &= healthy and final_success and attempted_success
            summary["plannerReasons"] = [run.get("PlannerDecision", {}).get("Reason") for run in runs]
            summary["plannerPredictedMs"] = [run.get("PlannerDecision", {}).get("PredictedParallelMs") for run in runs]
            summary["plannerPredictedPeakBytes"] = [run.get("PlannerDecision", {}).get("PredictedPeakBytes") for run in runs]
            summary["bypassedReasons"] = [run.get("PlannerDecision", {}).get("BypassedReasons", []) for run in runs]
            summary["fallbackKinds"] = [run.get("FallbackKind") for run in runs]
            summary["wastedParallelMs"] = [run.get("WastedParallelMs", 0) for run in runs]
            summary["searchMs"] = [run.get("Plan", {}).get("SearchMs") for run in runs]
            summary["searchBudgetExpired"] = [run.get("Plan", {}).get("SearchBudgetExpired") for run in runs]
            summary["postLoadFailureCount"] = sum(run.get("FallbackKind") == "post-load-failure" for run in runs)
            summary["attemptedUnexplainedCounts"] = dict(collections.Counter(item[1]
                for comparison in attempted_comparisons if comparison for item in comparison["unexplained"]))
            summary["attemptedExplainedCaseCount"] = sum(len(comparison["explainedCase"])
                for comparison in attempted_comparisons if comparison)
            summary["ordinaryFallbackUnexplainedCounts"] = dict(collections.Counter(item[1]
                for comparison in ordinary_comparisons if comparison for item in comparison["unexplained"]))
            summary["attemptedStatusCounts"] = dict(collections.Counter(run["AttemptedParallel"]["Status"]
                for run in runs if run.get("AttemptedParallel")))
            summary["attemptedWallMs"] = statistics.median([run["AttemptedParallel"]["WallMs"]
                for run in runs if run.get("AttemptedParallel")]) if any(run.get("AttemptedParallel") for run in runs) else None
            summary["attemptedWallWithPlanningMs"] = statistics.median([run["AttemptedParallel"]["WallMs"] + run.get("PlanningMs", 0)
                for run in runs if run.get("AttemptedParallel")]) if any(run.get("AttemptedParallel") for run in runs) else None
            attempted_runs = [run["AttemptedParallel"] for run in runs if run.get("AttemptedParallel")]
            for field in ("Phase1Ms", "MergeMs"):
                values = [run[field] for run in attempted_runs if run.get(field) is not None]
                summary["attempted" + field] = statistics.median(values) if values else None
            summary["attemptedCriticalGroupMs"] = statistics.median([max(run.get("GroupMs", []), default=0)
                for run in attempted_runs]) if attempted_runs else None
            summary["attemptedGroupStatusCounts"] = dict(collections.Counter(group["Status"]
                for run in attempted_runs for group in run.get("GroupOutcomes", [])))
            summary["outerWallRatioDescriptive"] = summary["wallMs"] / baseline_ms if summary["wallMs"] and baseline_ms else None
            summary["ordinaryFallbackStatusCounts"] = dict(collections.Counter(run["OrdinaryFallback"]["Status"]
                for run in runs if run.get("OrdinaryFallback")))
            summary["attemptedPeakMemoryMax"] = max((run["AttemptedParallel"].get("Process", {}).get("PeakBuildHostWorkingSet", 0)
                for run in runs if run.get("AttemptedParallel")), default=None)
            summary["forceParallel"] = any(run.get("ForceParallel") or combo.endswith("-force") for run in runs)
            if summary["wallMs"] and summary["plannerPredictedMs"][0] is not None:
                prediction = statistics.median(summary["plannerPredictedMs"])
                summary["forecastRelativeError"] = (prediction - summary["wallMs"]) / summary["wallMs"]
            else:
                summary["forecastRelativeError"] = None
            summary["forecastVersusAttemptedRelativeError"] = (
                (statistics.median(summary["plannerPredictedMs"]) - summary["attemptedWallWithPlanningMs"]) / summary["attemptedWallWithPlanningMs"]
                if summary["attemptedWallWithPlanningMs"] and all(value is not None for value in summary["plannerPredictedMs"]) else None)
            valid = summary["completeValidRuns"] and not summary["unexplainedCounts"]
            correctness &= valid
            for run in runs:
                if run.get("AttemptedParallel") and not summary["forceParallel"]:
                    memory &= run["AttemptedParallel"].get("Process", {}).get("PeakBuildHostWorkingSet", math.inf) <= 4_000_000_000
            if any(kind == "pre-load-refusal" for kind in summary["fallbackKinds"]):
                refusal &= valid and summary["wallRatio"] is not None and summary["wallRatio"] <= 1.05
            entry["combinations"][combo] = summary
        for name, run in diagnostics.items():
            if not name.startswith("diagnostic-" + solution + "-"):
                continue
            comparison = compare(oracle, run, {}) if available and run.get("Projects") else None
            diag_valid = bool(comparison is not None and not comparison["unexplained"] and run.get("Status") == "ok"
                              and not run.get("PlanViolationCount") and run.get("BinlogCount", 0) > 0)
            diagnostic_issues = parallel_diagnostic_issues(run) if name.endswith("-cluster-N4") else []
            diag_valid &= not diagnostic_issues
            result["diagnostics"][name] = {"status": run.get("Status"), "equivalent": diag_valid,
                "unexplainedCount": len(comparison["unexplained"]) if comparison else None,
                "binlogCount": run.get("BinlogCount"), "wallMs": run.get("WallMs"), "outsideMedians": True,
                "issues": diagnostic_issues}
            correctness &= diag_valid
    for solution in ("powertoys", "roslyn"):
        for mode in ("baseline", "cluster-N4"):
            if f"diagnostic-{solution}-{mode}" not in diagnostics:
                result["missing"].append(f"diagnostic-{solution}-{mode}")
    pt = result["solutions"]["powertoys"]["combinations"].get("cluster-N4", {})
    speed = bool(pt.get("completeValidRuns") and pt.get("wallRatio") is not None and pt["wallRatio"] <= .75)
    result["gates"] = {"correctness": correctness, "speed": speed, "refusalOverhead": refusal, "memory": memory}
    result["go"] = not result["missing"] and all(result["gates"].values())
    for combo, summary in result["solutions"]["roslyn"]["combinations"].items():
        forced = result["solutions"]["roslyn"]["combinations"].get(combo + "-force", {})
        if "no-expected-gain" in summary.get("plannerReasons", []) and forced.get("completeValidRuns"):
            summary["gainRefusalFalsified"] = forced.get("wallRatio") is not None and forced["wallRatio"] < .9
    heldout_path = directory / "roslyn-heldout-prediction-before-parallel.json"
    if heldout_path.exists():
        heldout = read(heldout_path)
        result["heldoutPredictionRecordedAtUtc"] = heldout["recordedAtUtc"]
        result["heldoutPredictionSha256"] = sha(heldout_path)
        result["heldoutComparison"] = {}
        roslyn = result["solutions"]["roslyn"]["combinations"]
        for workers, frozen in heldout["forecasts"].items():
            combo = "cluster-N" + workers
            measured = roslyn.get(combo + "-force") if frozen["decision"]["Reason"] else roslyn.get(combo)
            measured = measured or {}
            predicted = frozen["decision"]["PredictedParallelMs"]
            attempted = measured.get("attemptedWallWithPlanningMs")
            outer = measured.get("wallMs")
            result["heldoutComparison"][workers] = {
                "forecastSourceSha256": frozen["sha256"], "predictedMs": predicted,
                "originalReason": frozen["decision"]["Reason"], "observedCombination": combo + ("-force" if frozen["decision"]["Reason"] else ""),
                "attemptedWithPlanningMedianMs": attempted, "outerIncludingFallbackMedianMs": outer,
                "errorAgainstAttempted": (predicted - attempted) / attempted if attempted else None,
                "errorAgainstOuter": (predicted - outer) / outer if outer else None,
                "validParallelResult": bool(measured.get("completeValidRuns") and not measured.get("attemptedUnexplainedCounts")),
                "estimatedPeakBytes": frozen["decision"]["PredictedPeakBytes"],
                "attemptedPeakMaxBytes": measured.get("attemptedPeakMemoryMax"),
                "meaning": "Frozen premeasurement prediction; failed attempted parallel timings are descriptive and do not validate the model or pass correctness gate."}
    return result, details


def merge_timing_streams(streams):
    projects = {}
    sources = []
    seen = set()
    for stream_index, stream in enumerate(streams):
        identity = stream.get("TimingJson"), stream.get("TimingSha256")
        if identity[0] is not None and identity in seen:
            raise ValueError("A timing stream was supplied more than once: " + identity[0])
        seen.add(identity)
        sources.append({"input": stream.get("Input"), "timingJson": stream.get("TimingJson"),
            "timingSha256": stream.get("TimingSha256"), "formatVersion": stream.get("FileFormatVersion"),
            "status": stream.get("Status"), "errors": stream.get("Errors", []),
            "projectCount": len(stream["Projects"]),
            "evaluationProfilerAvailable": stream.get("EvaluationProfilerAvailable", False),
            "sdkProperties": stream.get("SdkProperties", []), "meaning": stream.get("Meaning")})
        for project in stream["Projects"]:
            key = project["Path"].casefold()
            merged = projects.setdefault(key, {"Path": project["Path"], "LoadOrder": len(projects) + 1,
                "EvaluationMs": 0, "ProjectInclusiveMs": 0, "ResolverMessageCount": 0,
                "Targets": {}, "Tasks": {}, "Occurrences": []})
            for field in ("EvaluationMs", "ProjectInclusiveMs", "ResolverMessageCount"):
                merged[field] += project.get(field, 0)
            merged["Occurrences"].append({"stream": stream_index, "input": stream.get("Input"),
                "loadOrder": project["LoadOrder"], "evaluationMs": project["EvaluationMs"]})
            for category in ("Targets", "Tasks"):
                for item in project[category]:
                    total = merged[category].setdefault(item["Name"], {"Name": item["Name"], "ExclusiveMs": 0, "InclusiveMs": 0})
                    total["ExclusiveMs"] += item["ExclusiveMs"]
                    total["InclusiveMs"] += item.get("InclusiveMs", 0)
    for project in projects.values():
        for category in ("Targets", "Tasks"):
            project[category] = sorted(project[category].values(), key=lambda item: (-item["ExclusiveMs"], item["Name"]))
    return {"Status": "ok" if all(stream.get("Status") == "ok" for stream in streams) else "incomplete",
        "Projects": list(projects.values()), "Streams": sources,
        "SdkProperties": sorted({value for stream in streams for value in stream.get("SdkProperties", [])}),
        "SdkResolverLimitation": "; ".join(sorted({stream.get("SdkResolverLimitation", "unavailable") for stream in streams})),
        "CrossStreamStartOrderAvailable": len(streams) == 1,
        "AggregationMeaning": "Union/exclusive durations were computed inside each replay stream. Across distinct BuildHost streams, project service durations are added, never interpreted as union wall. Repeated project paths/TFM contexts remain path aggregates; every stream occurrence is retained."}


def progress_project_order(report):
    attempted = report.get("AttemptedParallel") or report
    order = {}
    for group in attempted.get("ProjectLoadProgress", {}).get("Groups", []):
        paths = []
        for event in group.get("Events", []):
            value = json.loads(event) if isinstance(event, str) else event
            path = value["FilePath"].replace("\\", "/").removeprefix("<solution>/").casefold()
            if path not in paths:
                paths.append(path)
        order[group["Index"]] = paths
    return order


def binlog_comparison(baseline, groups, project_order=None):
    ordinary = {project["Path"].casefold(): project for project in baseline["Projects"]}
    result = {"sdkResolverTimingMs": None, "sdkResolverLimitation": baseline["SdkResolverLimitation"],
        "baselineStreams": baseline.get("Streams", []), "aggregationMeaning": baseline.get("AggregationMeaning"), "groups": []}
    for group_index, group in enumerate(groups):
        matched = [project for project in sorted(group["Projects"], key=lambda project: project["LoadOrder"])
                   if project["Path"].casefold() in ordinary]
        first_three_available = group.get("CrossStreamStartOrderAvailable", True)
        order_meaning = "first appearance within one binlog stream"
        ordering_unmatched = []
        if project_order and group_index in project_order:
            positions = {path: index for index, path in enumerate(project_order[group_index])}
            ordering_unmatched = [project["Path"] for project in matched if project["Path"].casefold() not in positions]
            matched.sort(key=lambda project: (positions.get(project["Path"].casefold(), math.inf), project["Path"].casefold()))
            first_three_available = sum(project["Path"].casefold() in positions for project in matched) >= min(3, len(matched))
            order_meaning = "first public progress callback per matched project across this group; callback completion order is a proxy, not exact cross-stream project start order"
        elif not first_three_available:
            order_meaning = "cross-stream start order unavailable; no invented chronological first-three partition"
        partitions = {}
        selected_parts = (("firstThree", matched[:3]), ("rest", matched[3:])) if first_three_available else (("allMatched", matched),)
        for label, selected in selected_parts:
            increments = {"evaluation": [], "targets": [], "tasks": []}
            for project in selected:
                before = ordinary[project["Path"].casefold()]
                increments["evaluation"].append({"path": project["Path"],
                    "deltaMs": project["EvaluationMs"] - before["EvaluationMs"],
                    "ordinaryMs": before["EvaluationMs"], "groupMs": project["EvaluationMs"]})
                for field, category in (("Targets", "targets"), ("Tasks", "tasks")):
                    old = {item["Name"]: item["ExclusiveMs"] for item in before[field]}
                    new = {item["Name"]: item["ExclusiveMs"] for item in project[field]}
                    increments[category] += [{"path": project["Path"], "name": name, "deltaMs": new.get(name, 0) - old.get(name, 0),
                        "ordinaryMs": old.get(name, 0), "groupMs": new.get(name, 0)} for name in old.keys() | new.keys()]
            partitions[label] = {"projectCount": len(selected), "paths": [project["Path"] for project in selected],
                "layers": {category: {"netDeltaMs": sum(item["deltaMs"] for item in rows),
                    "positiveDeltaMs": sum(max(0, item["deltaMs"]) for item in rows),
                    "topIncrements": sorted(rows, key=lambda item: (-item["deltaMs"], item["path"], item.get("name", "")))[:20]}
                    for category, rows in increments.items()}}
            denominator = sum(layer["positiveDeltaMs"] for layer in partitions[label]["layers"].values())
            partitions[label]["positiveAttributedDeltaMs"] = denominator
            partitions[label]["shareDenominator"] = "sum of positive deltas of evaluation and exclusive targets/tasks on matched projects in this partition; not total wall residual"
            for layer in partitions[label]["layers"].values():
                layer["positiveAttributedShare"] = layer["positiveDeltaMs"] / denominator if denominator else None
        result["groups"].append({"index": group_index, "status": group["Status"], "partitions": partitions,
            "streams": group.get("Streams", []), "firstThreeAvailable": first_three_available,
            "orderMeaning": order_meaning, "orderingUnmatchedPaths": ordering_unmatched,
            "unmatchedPaths": [project["Path"] for project in group["Projects"] if project["Path"].casefold() not in ordinary],
            "sdkProperties": group.get("SdkProperties", [])})
    result["meaning"] = "Evaluation and exclusive target/task layers reported separately; inclusive target/task parents are never added. Across streams, service durations are summed, not asserted to be wall. First-three ordering uses available one-stream appearance or explicitly labeled public callback proxy; otherwise unavailable. Synthetic aggregator/unmatched projects remain separate. Differences are diagnostic context effects, not causal process-start measurements."
    return result


def main():
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="mode", required=True)
    estimate = sub.add_parser("estimate")
    estimate.add_argument("--inputs", required=True, nargs=3, type=pathlib.Path)
    estimate.add_argument("--output", required=True, type=pathlib.Path)
    matrix = sub.add_parser("matrix")
    matrix.add_argument("--artifacts", required=True, type=pathlib.Path)
    matrix.add_argument("--output", required=True, type=pathlib.Path)
    matrix.add_argument("--details", required=True, type=pathlib.Path)
    diagnostic = sub.add_parser("binlogs")
    diagnostic.add_argument("--baseline", required=True, nargs="+", type=pathlib.Path)
    group_options = diagnostic.add_mutually_exclusive_group(required=True)
    group_options.add_argument("--groups", nargs="+", type=pathlib.Path)
    group_options.add_argument("--group-streams", action="append", nargs="+", type=pathlib.Path)
    diagnostic.add_argument("--group-load-report", type=pathlib.Path)
    diagnostic.add_argument("--output", required=True, type=pathlib.Path)
    args = parser.parse_args()
    if args.mode == "estimate":
        result = baseline_estimate(args.inputs)
    elif args.mode == "matrix":
        result, details = summarize(args.artifacts)
        args.details.parent.mkdir(parents=True, exist_ok=True)
        args.details.write_text(json.dumps(details, indent=2), encoding="utf-8")
    else:
        def load_streams(paths):
            return merge_timing_streams([dict(read(path), TimingJson=path.name, TimingSha256=sha(path)) for path in paths])
        group_paths = args.group_streams or [[path] for path in args.groups]
        result = binlog_comparison(load_streams(args.baseline), [load_streams(paths) for paths in group_paths],
            progress_project_order(read(args.group_load_report)) if args.group_load_report else None)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"{args.mode}: {args.output}")


if __name__ == "__main__":
    main()
