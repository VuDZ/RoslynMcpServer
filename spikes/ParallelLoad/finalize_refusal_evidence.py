"""Write compact task-07.2 evidence and verify the frozen runtime and earlier evidence."""
import hashlib
import json
import pathlib
import re


REPO = pathlib.Path(__file__).resolve().parents[2]
RAW = REPO / "artifacts/parallel-load/task-07.2-evidence"
DOCS = REPO / "docs/workspace-load-performance/epoch-0-stage-measurement/evidence/task-07.2-refusal-fast-planner-roslyn"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(name, data):
    (DOCS / name).write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")


def warmup():
    warm = {}
    for solution in ("powertoys", "roslyn"):
        path = RAW / (solution + "-warmup-comparison.json")
        comparison = read(path)
        entry = {key: value for key, value in comparison.items() if key != "groups"}
        entry["groups"] = []
        for group in comparison["groups"]:
            compact = {key: value for key, value in group.items() if key not in ("partitions", "orderingUnmatchedPaths", "unmatchedPaths")}
            compact["orderingUnmatchedPathCount"] = len(group["orderingUnmatchedPaths"])
            compact["orderingUnmatchedExamples"] = group["orderingUnmatchedPaths"][:3]
            compact["unmatchedPathCount"] = len(group["unmatchedPaths"])
            compact["unmatchedFileNames"] = [path.replace("\\", "/").rsplit("/", 1)[-1] for path in group["unmatchedPaths"]]
            compact["partitions"] = {}
            for label, partition in group["partitions"].items():
                part = {key: value for key, value in partition.items() if key not in ("layers", "paths")}
                if label == "firstThree":
                    part["paths"] = partition["paths"]
                part["layers"] = {key: dict(value, topIncrements=value["topIncrements"][:3])
                                  for key, value in partition["layers"].items()}
                compact["partitions"][label] = part
            entry["groups"].append(compact)
        totals = {label: sum(group["partitions"][label]["positiveAttributedDeltaMs"] for group in comparison["groups"])
                  for label in ("firstThree", "rest")}
        layers = {layer: sum(partition["layers"][layer]["positiveDeltaMs"] for group in comparison["groups"]
                            for partition in group["partitions"].values()) for layer in ("evaluation", "targets", "tasks")}
        entry.update(aggregatePositiveAttributedDeltaMs=sum(totals.values()),
                     firstThreePositiveAttributedShare=totals["firstThree"] / sum(totals.values()),
                     positivePartitionTotalsMs=totals, positiveLayerTotalsMs=layers,
                     positiveLayerShares={key: value / sum(layers.values()) for key, value in layers.items()})
        extras = {}
        paths = sorted(RAW.glob(solution + "-parallel-phase1-*-timings.json"))
        if solution == "roslyn":
            paths.append(RAW / "roslyn-parallel-capture-1-timings.json")
        for timing in paths:
            stream = read(timing)
            extras[timing.name] = {key: stream.get(key) for key in (
                "Status", "Input", "FileFormatVersion", "Errors", "SdkProperties", "EvaluationProfilerAvailable", "Meaning")}
            extras[timing.name].update(sha256=sha(timing), projectCount=len(stream["Projects"]),
                                      role="ordinary post-load fallback" if "capture" in timing.name else "phase1")
        entry["phase1AndFallbackStreams"] = extras
        for stream in entry.get("baselineStreams", []) + [stream for group in entry["groups"] for stream in group.get("streams", [])]:
            if stream.get("timingJson"):
                stream["projectCount"] = len(read(RAW / stream["timingJson"])["Projects"])
        entry["comparisonSha256"] = sha(path)
        warm[solution] = entry
    write("warmup.json", warm)


def integrity():
    freeze = read(RAW / "measured-source-sha256.json")
    changed = []
    for name, expected in freeze.items():
        actual = sha(REPO / "spikes/ParallelLoad" / name.replace("\\", "/"))
        if actual.lower() != expected.lower():
            changed.append({"path": name, "expected": expected, "actual": actual})
    write("measured-source-sha256.json", freeze)
    prior = read(RAW / "pre-task/prior-evidence-sha256.json")
    prior_changed = [name for name, expected in prior.items()
                     if sha(DOCS.parent / name.replace("\\", "/")).lower() != expected.lower()]
    comparator = {name: sha(REPO / "spikes/ParallelLoad" / name) == sha(RAW / "pre-task/spike" / (name + ".baseline.txt"))
                  for name in ("compare.py", "SolutionObservation.cs")}
    write("post-series-source-integrity.json", {
        "frozenSourceCount": len(freeze), "changedRuntimeSources": changed,
        "priorEvidenceFileCount": len(prior), "changedPriorEvidence": prior_changed,
        "byteUnchangedComparator": comparator})
    if changed or prior_changed or not all(comparator.values()):
        raise RuntimeError("Frozen runtime, historical evidence or comparator changed")


def inventories():
    patterns = (
        r"(powertoys|files|roslyn)-(baseline|cluster-N\d+|greedy-N\d+)(-force)?-[0-3]\.json(\.(attempted|ordinary)\.json)?",
        r"diagnostic-(powertoys|roslyn)-(baseline|cluster-N4)\.json(\.(attempted|ordinary)\.json)?",
        r"(powertoys|files|roslyn)-(profile|profile\.load|baseline-estimate|prerequisite|forecast-N[48]|heldout-prediction-before-parallel|warmup-comparison)\.json")
    selected = [path for path in RAW.iterdir() if path.is_file() and (
        any(re.fullmatch(pattern, path.name) for pattern in patterns)
        or path.name.endswith("-timings.json") and not path.name.startswith("mcp-")
        or path.name in ("environment.json", "launcher-errors-before-load.json", "measured-source-sha256.json")
        or path.name.endswith("-restore.log") or path.name.endswith("-restore-exit.txt"))]
    for folder in ("powertoys-profile-binlogs", "files-profile-binlogs", "roslyn-profile-binlogs",
                   "powertoys-parallel-binlogs", "roslyn-parallel-binlogs"):
        selected.extend((RAW / folder).glob("*.binlog"))
    write("artifact-sha256.json", {str(path.relative_to(RAW)).replace("\\", "/"): sha(path) for path in sorted(selected)})
    write("analysis-source-sha256.json", {path.name: sha(path) for path in (REPO / "spikes/ParallelLoad").glob("*.py")})
    print(f"Compact warmup and {len(selected)} artifact hashes written; frozen 50 sources and prior 19 evidence files verified.")


if __name__ == "__main__":
    warmup()
    integrity()
    inventories()
