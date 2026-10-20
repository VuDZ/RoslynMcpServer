"""Synthetic gate checks; no workspace load and no alteration of the observation comparator."""
import json
import pathlib
import tempfile

from summarize_core_first_continuation import summarize_matrix


def run(mode="core-first", graph="xml", partition="cluster", workers=4, phase2="parallel"):
    return {
        "Status": "ok", "Mode": mode, "Graph": graph, "Partition": partition, "Workers": workers,
        "CoreDepth": 1, "Phase2": phase2, "WallMs": 100 if mode == "baseline" else 50,
        "UniqueProjects": 1, "Projects": {"Root": {"Name": "Root"}}, "Diagnostics": [],
        "PlanViolationCount": 0, "Phase1Ms": 10, "Phase1FirstProgressMs": 1,
        "Phase2FirstProgressMs": [1] * workers, "GroupMs": [20] * workers,
        "MergeMs": 1, "PlanningMs": 1, "AggregatorPreparationMs": 1,
        "Plan": {"Core": [], "CoreCost": 0, "TotalCost": 40, "Groups": [["Root"]] * workers,
                 "GroupCosts": [10] * workers},
        "Process": {"BuildHostCount": 1, "PeakConcurrentBuildHosts": 1, "PeakBuildHostWorkingSet": 100,
                    "BusyCores": 1, "Errors": [], "MaxSampleGapMs": 100, "Samples": 10,
                    "BuildHosts": [{"Identity": "host", "CpuMs": 10, "FirstObservedMs": 0,
                                    "LastObservedMs": 20, "SampledLifetimeMs": 20,
                                    "SampledZeroCpuMs": 0, "SampledZeroCpuShare": 0}]},
    }


def fixtures(directory):
    configs = [("powertoys", "baseline", "xml", "greedy", 1, "parallel"),
               ("files", "baseline", "xml", "greedy", 1, "parallel")]
    configs += [("powertoys", "core-first", "xml", partition, workers, "parallel")
                for partition in ("greedy", "cluster") for workers in (2, 4, 8)]
    configs += [("powertoys", "core-first", "snapshot-load", "cluster", 4, "parallel")]
    configs += [("files", "core-first", "xml", "cluster", workers, "parallel") for workers in (2, 4)]
    configs += [("powertoys", "core-first", "snapshot-load", "cluster", 4, "isolated")]
    for solution, mode, graph, partition, workers, phase2 in configs:
        for index in range(4):
            value = run(mode, graph, partition, workers, phase2)
            (directory / f"{solution}-{mode}-{graph}-{partition}-{workers}-{phase2}-{index}.json").write_text(
                json.dumps(value), encoding="utf-8")


def mutate(directory, pattern, action):
    for path in directory.glob(pattern):
        value = json.loads(path.read_text())
        action(value)
        path.write_text(json.dumps(value), encoding="utf-8")


def main():
    scratch = pathlib.Path(__file__).resolve().parents[2] / "artifacts" / "parallel-load"
    scratch.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=scratch, prefix="continuation-check-") as temporary:
        directory = pathlib.Path(temporary)
        fixtures(directory)
        assert summarize_matrix(directory)["go"], "A complete valid equivalent series meeting .75 must pass."
        mutate(directory, "files-core-first-xml-cluster-4-parallel-1.json",
               lambda value: value["Process"].update(PeakBuildHostWorkingSet=4_000_000_001))
        assert not summarize_matrix(directory)["go"], "The maximum individual memory peak must gate both solutions."
        fixtures(directory)
        mutate(directory, "files-core-first-xml-cluster-4-parallel-*.json",
               lambda value: value.update(Status="infeasible-search", Error="No balanced assignment", Projects={}))
        failed = summarize_matrix(directory)
        item = failed["solutions"]["files"]["combinations"]["xml-cluster-N4-depth1-parallel"]
        assert not failed["go"] and item["statusCounts"] == {"infeasible-search": 3} and item["wallRatio"] is None
        assert item["plan"] and item["unexplainedCounts"], "All-failed planning rows must retain plan and failure evidence."
        fixtures(directory)
        mutate(directory, "*-core-first-*-parallel-*.json", lambda value: value.update(WallMs=80))
        mutate(directory, "*-isolated-*.json", lambda value: value.update(WallMs=10))
        assert not summarize_matrix(directory)["go"], "An isolated diagnostic run must never satisfy the speed threshold."
        fixtures(directory)
        mutate(directory, "files-core-first-xml-cluster-2-parallel-2.json", lambda value: value.update(Diagnostics=["New failure"]))
        assert not summarize_matrix(directory)["go"], "An unexplained difference on Files must reject a fast PowerToys series."
        fixtures(directory)
        mutate(directory, "powertoys-core-first-xml-greedy-2-parallel-1.json",
               lambda value: value["Process"].update(MaxSampleGapMs=1001))
        assert not summarize_matrix(directory)["go"], "A sampler gap above one second is invalid evidence."
        fixtures(directory)
        mutate(directory, "powertoys-core-first-xml-greedy-2-parallel-1.json", lambda value: value.update(Status="timeout", Error="Timeout"))
        assert not summarize_matrix(directory)["go"], "Timeout rows must fail global correctness."
        fixtures(directory)
        assert not summarize_matrix(directory)["model"]["available"], (
            "A negative calibrated group residual must report an unsupported additive model rather than invent cold overhead."
        )
        mutate(directory, "*-core-first-*.json", lambda value: value["Plan"].update(TotalCost=100, CoreCost=60))
        positive_model = summarize_matrix(directory)["model"]
        assert positive_model["available"] and len(positive_model["rows"]) == 6
        assert positive_model["groupFixedResidualProxyMs"] > 5 * positive_model["h2IsolatedFirstProgressProxyMs"], (
            "Measured isolated/profile context overhead must be retained separately from the first-progress startup proxy."
        )
        assert positive_model["calibration"]["observedWallMs"] == 50, (
            "A calibration must expose its observed result, allowing readers to assess model error."
        )
        fixtures(directory)
        mutate(directory, "*-core-first-*.json",
               lambda value: value["Plan"].update(GroupCosts=[0] * value["Workers"], TotalCost=0, CoreCost=0))
        zero_cost = summarize_matrix(directory)
        assert not zero_cost["model"]["available"] and "zero" in zero_cost["model"]["reason"]
        assert len(zero_cost["model"]["groups"]) == 4 and not zero_cost["model"]["rows"], (
            "Nonempty measured zero-cost groups must retain evidence and report an unavailable model without division."
        )
        fixtures(directory)
        (directory / "files-baseline-xml-greedy-1-parallel-3.json").unlink()
        assert not summarize_matrix(directory)["go"], "Missing baseline runs must not produce a successful oracle."
    print("Continuation analysis checks passed: .75 speed gate, global equivalence, isolated exclusion, per-run memory, all-failed plans, sampler gaps, timeouts, zero/negative model refusal, calibrated residual separation, and missing runs.")


if __name__ == "__main__":
    main()
