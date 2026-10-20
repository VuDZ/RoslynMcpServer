"""Summarize the explicit early exit without inventing calibration or simulation values."""
import argparse
import collections
import gzip
import hashlib
import json
import pathlib
import statistics
import struct

from compare import differences


def read_json(path):
    if path.suffix == ".gz":
        with gzip.open(path, "rt", encoding="utf-8-sig") as stream:
            return json.load(stream)
    return json.loads(path.read_text(encoding="utf-8-sig"))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--artifacts", required=True, type=pathlib.Path)
    parser.add_argument("--task04", required=True, type=pathlib.Path)
    parser.add_argument("--evidence", required=True, type=pathlib.Path)
    options = parser.parse_args()
    options.evidence.mkdir(parents=True, exist_ok=True)
    summary = {"verdict": "no-go", "step": "profile", "calibration": "not-started",
               "step2": "not-started", "solutions": {}}
    for name in ("powertoys", "files"):
        folder = options.artifacts / (name + "-restored")
        profile = read_json(folder / "profile.json")
        snapshot = read_json(folder / "profile.load.json")
        preliminary = read_json(options.artifacts / name / "profile.load.json")
        baseline1 = read_json(options.task04 / (name + "-baseline-1.json.gz"))
        baseline2 = read_json(options.task04 / (name + "-baseline-2.json.gz"))
        assert snapshot["Status"] == "ok" and snapshot["UniqueProjects"] > 0
        keys = {project["Key"] for project in profile["Projects"]}
        for project in profile["Projects"]:
            assert set(project["References"]) <= keys, "The sanitized graph must resolve every target."
        public_json = json.dumps(profile)
        assert "E:" not in public_json and "C:" not in public_json
        output_name = "profile.json" if name == "powertoys" else "files-profile.json"
        (options.evidence / output_name).write_text(json.dumps(profile, indent=2), encoding="utf-8")
        logs = []
        for path in sorted((folder / "profile-binlogs").glob("*.binlog")):
            with gzip.open(path, "rb") as stream:
                header = struct.unpack("<i", stream.read(4))[0]
            logs.append({"file": path.name, "bytes": path.stat().st_size,
                         "formatVersion": header, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        own = [project["OwnBuildMs"] for project in profile["Projects"] if project["OwnBuildMs"] is not None]
        summarize_differences = lambda left, right: dict(collections.Counter(item[1] for item in differences(left, right)))
        entry = {"profileStatus": profile["Status"], "error": profile["Error"],
                 "baselineWallMs": snapshot["WallMs"], "loadStatus": snapshot["Status"],
                 "uniqueProjects": snapshot["UniqueProjects"], "diagnosticCount": snapshot["DiagnosticCount"],
                 "readerVersion": profile["ReaderVersion"], "binlogs": logs,
                 "registration": "<SDK>/10.0.401" if "10.0.401" in snapshot["Registration"] else "<VS>/MSBuild/Current/Bin/amd64",
                 "task04Registration": "<SDK>/10.0.401" if "10.0.401" in baseline1["Registration"] else "<VS>/MSBuild/Current/Bin/amd64",
                 "registrationChanged": snapshot["Registration"] != baseline1["Registration"],
                 "task04BaselineSelfDifferenceCounts": summarize_differences(baseline1, baseline2),
                 "profileLoadSelfDifferenceCounts": summarize_differences(preliminary, snapshot),
                 "task04BaselineToProfileDifferenceCounts": summarize_differences(baseline1, snapshot),
                 "markup": [{"name": project["Name"], "evidence": project["MarkupEvidence"]}
                            for project in profile["Projects"] if project["Markup"]],
                 "evaluationIntervalsAvailable": sum(project["EvaluationMs"] is not None for project in profile["Projects"]),
                 "ownBuildMs": {"sum": sum(own), "median": statistics.median(own),
                                "min": min(own), "max": max(own)} if own else None}
        summary["solutions"][name] = entry
    summary["simulation"] = [
        {"strategy": strategy, "workers": workers, "status": "unavailable: PowerToys profile failed",
         "wallMs": None, "speedup": None, "repeatedProjects": None, "sharedCoreSize": None,
         "repeatedMarkup": None}
        for strategy in ("plan", "round-robin", "cluster") for workers in (2, 4, 8)]
    (options.evidence / "comparison.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
