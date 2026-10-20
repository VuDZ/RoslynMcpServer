"""Executable regressions for the task-07.2 gate, using synthetic observations."""
import copy
import json
import pathlib
import tempfile

from summarize_refusal import baseline_estimate, binlog_comparison, merge_timing_streams, parallel_diagnostic_issues, progress_project_order, summarize


def row(wall=100, mode="baseline"):
    return {
        "Status": "ok", "Mode": mode, "WallMs": wall, "UniqueProjects": 1,
        "Projects": {"P|P": {"Field": "stable"}}, "Diagnostics": [], "PlanViolationCount": 0,
        "GlobalProperties": "debug-x64", "Properties": {"Configuration": "Debug", "Platform": "x64"},
        "Solution": "<solution>/S.slnx", "Registration": "SDK", "DiagnosticRun": False,
        "Process": {"PeakBuildHostWorkingSet": 1000, "Errors": [], "Samples": 1, "MaxSampleGapMs": 10,
                    "BuildHostCount": 1, "BuildHosts": [{"Identity": "1", "CpuMs": 1, "FirstObservedMs": 0,
                        "LastObservedMs": 100, "SampledLifetimeMs": 100, "SampledZeroCpuMs": 1, "SampledZeroCpuShare": .01}]},
    }


def main():
    scratch = pathlib.Path(__file__).resolve().parents[2] / "artifacts/parallel-load/task-07.2-evidence"
    scratch.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=scratch, prefix="analysis-check-") as temporary:
        directory = pathlib.Path(temporary)
        for solution, combos in (("powertoys", ["baseline", "cluster-N4"]),
                                 ("files", ["baseline", "cluster-N2"]),
                                 ("roslyn", ["baseline", "cluster-N4", "cluster-N8", "greedy-N4"])):
            for combo in combos:
                for index in range(4):
                    value = row(100 if combo == "baseline" or solution == "files" else 70,
                                "baseline" if combo == "baseline" else "core-first")
                    if combo != "baseline":
                        value["GateEligible"] = True
                        value["PlannerDecision"] = {"Reason": "balance-infeasible" if solution == "files" else None,
                            "PredictedParallelMs": 70, "PredictedPeakBytes": 2000, "BypassedReasons": []}
                        value["FallbackKind"] = "pre-load-refusal" if solution == "files" else None
                        if solution != "files":
                            value["AttemptedParallel"] = copy.deepcopy(value)
                    (directory / f"{solution}-{combo}-{index}.json").write_text(json.dumps(value), encoding="utf-8")
        for solution in ("powertoys", "roslyn"):
            for mode in ("baseline", "cluster-N4"):
                value = row(mode="baseline-binlog" if mode == "baseline" else "core-first")
                value.update(DiagnosticRun=True, BinlogCount=1)
                if mode == "cluster-N4":
                    value.update(Workers=4, FallbackKind=None, PlannerDecision={"Reason": None, "BypassedReasons": []})
                    attempted = copy.deepcopy(value)
                    attempted.update(Plan={"Roots": [["A"], ["B"], ["C"], ["D"]]}, Phase1RequestedPaths=["Core"],
                        GroupOutcomes=[{"Index": index, "Status": "ok"} for index in range(4)],
                        BinlogFiles=["phase1-1.binlog", *[f"group-{index}-1.binlog" for index in range(4)]],
                        BinlogCount=5, BinlogBytes=1000)
                    value["AttemptedParallel"] = attempted
                    value["BinlogCount"] = 5
                (directory / f"diagnostic-{solution}-{mode}.json").write_text(json.dumps(value), encoding="utf-8")
        result, _ = summarize(directory)
        assert result["go"], result
        diagnostic = json.loads((directory / "diagnostic-powertoys-cluster-N4.json").read_text(encoding="utf-8"))
        assert not parallel_diagnostic_issues(diagnostic)
        fallback = copy.deepcopy(diagnostic)
        fallback.update(FallbackKind="pre-load-refusal", AttemptedParallel=None)
        assert parallel_diagnostic_issues(fallback), "Ordinary fallback must not satisfy required parallel diagnostic"
        missing_group = copy.deepcopy(diagnostic)
        missing_group["AttemptedParallel"]["BinlogFiles"].pop()
        assert parallel_diagnostic_issues(missing_group), "Every nonempty group must have its own binlog"
        missing_phase = copy.deepcopy(diagnostic)
        missing_phase["AttemptedParallel"]["BinlogFiles"].pop(0)
        assert parallel_diagnostic_issues(missing_phase), "Phase1 binlog must be present"
        forced = copy.deepcopy(diagnostic)
        forced["PlannerDecision"]["BypassedReasons"] = ["memory-budget"]
        assert parallel_diagnostic_issues(forced)
        forced["ForceParallel"] = True
        assert not parallel_diagnostic_issues(forced)
        inputs = [directory / f"powertoys-baseline-{index}.json" for index in (1, 2, 3)]
        assert baseline_estimate(inputs)["WallMs"] == 100
        candidate = directory / "powertoys-cluster-N4-3.json"
        original = candidate.read_text(encoding="utf-8")
        value = json.loads(original)
        value.update(Status="timeout", FallbackKind="post-load-failure", GateEligible=False)
        value["AttemptedParallel"]["Status"] = "timeout"
        candidate.write_text(json.dumps(value), encoding="utf-8")
        assert not summarize(directory)[0]["go"], "Ordinary fallback must never rescue failed parallel gate"
        candidate.write_text(original, encoding="utf-8")
        value = json.loads(original)
        value["AttemptedParallel"]["Process"]["PeakBuildHostWorkingSet"] = 4_000_000_001
        candidate.write_text(json.dumps(value), encoding="utf-8")
        assert not summarize(directory)[0]["gates"]["memory"], "Individual outlier must fail memory gate"
        candidate.write_text(original, encoding="utf-8")
        files = directory / "files-cluster-N2-3.json"
        value = json.loads(files.read_text(encoding="utf-8"))
        value["WallMs"] = 120
        files.write_text(json.dumps(value), encoding="utf-8")
        # The gate is explicitly a median, so a single outlier retains the predeclared 105% median criterion.
        assert summarize(directory)[0]["gates"]["refusalOverhead"]
        value["Projects"]["P|P"]["Field"] = "unexplained"
        files.write_text(json.dumps(value), encoding="utf-8")
        assert not summarize(directory)[0]["go"], "Pre-load fallback equivalence must be checked"
        (directory / "roslyn-greedy-N4-2.json").unlink()
        assert "roslyn/greedy-N4/2" in summarize(directory)[0]["missing"]
    project = {"Path": "P", "LoadOrder": 1, "EvaluationMs": 10, "Targets": [], "Tasks": []}
    ordinary = {"Projects": [project], "SdkResolverLimitation": "unavailable"}
    grouped = {"Status": "ok", "Projects": [dict(project, EvaluationMs=20)], "SdkProperties": []}
    diagnostic = binlog_comparison(ordinary, [grouped])
    assert diagnostic["sdkResolverTimingMs"] is None
    assert diagnostic["groups"][0]["partitions"]["firstThree"]["layers"]["evaluation"]["positiveAttributedShare"] == 1
    streams = [dict(grouped, Input="host1.binlog", FileFormatVersion=26),
               dict(grouped, Input="host2.binlog", FileFormatVersion=27)]
    merged = merge_timing_streams(streams)
    assert merged["Projects"][0]["EvaluationMs"] == 40
    assert len(merged["Projects"][0]["Occurrences"]) == 2 and len(merged["Streams"]) == 2
    combined = binlog_comparison(merge_timing_streams([dict(ordinary, Status="ok")]), [merged])
    assert not combined["groups"][0]["firstThreeAvailable"] and "allMatched" in combined["groups"][0]["partitions"]
    public_order = progress_project_order({"ProjectLoadProgress": {"Groups": [{"Index": 0,
        "Events": [json.dumps({"FilePath": "<solution>/P", "Operation": "Evaluate", "ElapsedMs": 1})]}]}})
    combined = binlog_comparison(merge_timing_streams([dict(ordinary, Status="ok")]), [merged], public_order)
    assert combined["groups"][0]["firstThreeAvailable"] and "proxy" in combined["groups"][0]["orderMeaning"]
    print("Refusal analysis checks passed: valid gate, fallback failure, memory outlier, refusal equivalence, missing row, baseline estimate, diagnostic shares.")


if __name__ == "__main__":
    main()
