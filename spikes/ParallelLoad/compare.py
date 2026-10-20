"""Compare fresh-process observations without suppressing new values in varying fields."""
import argparse
import collections
import gzip
import json
import pathlib
import statistics


def differences(left, right):
    result = []
    for key in sorted(left["Projects"].keys() | right["Projects"].keys()):
        if key not in left["Projects"] or key not in right["Projects"]:
            result.append((key, "ProjectKeys", key in left["Projects"], key in right["Projects"]))
            continue
        for field in sorted(left["Projects"][key]):
            a, b = left["Projects"][key][field], right["Projects"][key][field]
            if a != b:
                result.append((key, field, a, b))
    for message in sorted(set(left["Diagnostics"]) ^ set(right["Diagnostics"])):
        result.append((message, "Diagnostics", message in left["Diagnostics"], message in right["Diagnostics"]))
    return result


def compare(reference, candidate, natural):
    result = {"natural": [], "explainedCase": [], "unexplained": []}
    for difference in differences(reference, candidate):
        key, category, before, after = difference
        observed = natural.get((key, category), [])
        destination = "natural" if after in observed else (
            "explainedCase" if case_only_path_difference(category, before, after) else "unexplained")
        result[destination].append(difference)
    result["verdict"] = "unexplained" if result["unexplained"] else (
        "natural variance only" if result["natural"] else "identical")
    result["strictVerdict"] = "non-identical" if result["unexplained"] or result["explainedCase"] else result["verdict"]
    return result


def case_only_path_difference(category, before, after):
    scalar_paths = {"OutputFilePath", "OutputRefFilePath", "CompilationOutputInfo.AssemblyPath"}
    document_paths = {name + suffix for name in ("Documents", "AdditionalDocuments", "AnalyzerConfigDocuments")
                      for suffix in (".Ordered", ".Set")}
    if category not in scalar_paths | document_paths:
        return False
    a, b = json.loads(before), json.loads(after)
    if isinstance(a, list) and isinstance(b, list):
        a = [path.casefold() if path else path for path in a]
        b = [path.casefold() if path else path for path in b]
        if category.endswith(".Set"):
            return set(a) == set(b)
        return a == b
    return isinstance(a, str) and isinstance(b, str) and a.casefold() == b.casefold()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--directory", required=True, type=pathlib.Path)
    parser.add_argument("--evidence", required=True, type=pathlib.Path)
    options = parser.parse_args()
    options.evidence.mkdir(parents=True, exist_ok=True)
    observations = {}
    for path in sorted(options.directory.glob("*.json")):
        if path.name.startswith("smoke-") or path.name == "environment.json":
            continue
        solution, combo, run = path.stem.split("-", 1)[0], path.stem.rsplit("-", 1)[0].split("-", 1)[1], int(path.stem.rsplit("-", 1)[1])
        value = json.loads(path.read_text(encoding="utf-8-sig"))
        observations.setdefault(solution, {}).setdefault(combo, {})[run] = value
        with gzip.open(options.evidence / (path.name + ".gz"), "wt", encoding="utf-8") as output:
            json.dump(value, output, ensure_ascii=False, separators=(",", ":"))
    summary = {"solutions": {}}
    for solution, combos in observations.items():
        baseline = combos["baseline"]
        if any(baseline[index]["Status"] != "ok" for index in (1, 2, 3)):
            raise RuntimeError(f"{solution}: baseline failed; no valid speedup or equivalence oracle")
        natural_differences = differences(baseline[1], baseline[2])
        natural = {(key, category): [before, after] for key, category, before, after in natural_differences}
        baseline_ms = statistics.median(baseline[index]["WallMs"] for index in (1, 2, 3))
        entry = {"baselineMs": baseline_ms, "naturalDifferences": natural_differences, "combinations": {}}
        for combo, runs in combos.items():
            measured = [runs[index] for index in (1, 2, 3)]
            successful = [run for run in measured if run["Status"] == "ok"]
            comparisons = [compare(baseline[1], run, natural) if run["Status"] == "ok" else {
                "verdict": "failed", "strictVerdict": "failed", "natural": [], "explainedCase": [],
                "unexplained": [["run", "Failure", None, run.get("Error")]],
            } for run in measured]
            counts = collections.Counter(difference[1] for comparison in comparisons for difference in comparison["unexplained"])
            examples = {}
            for comparison in comparisons:
                for difference in comparison["unexplained"]:
                    items = examples.setdefault(difference[1], [])
                    if difference not in items and len(items) < 20:
                        items.append(difference)
            median_ms = statistics.median(run["WallMs"] for run in measured)
            median = lambda name: statistics.median(run[name] for run in successful) if successful else None
            process_median = lambda name: statistics.median(run["Process"][name] for run in successful) if successful else None
            item = {
                "successfulRuns": len(successful), "wallMs": median_ms, "speedup": baseline_ms / median_ms,
                "wallRatio": median_ms / baseline_ms, "repeatedProjects": median("RepeatedProjects"),
                "uniqueProjects": median("UniqueProjects"), "totalProjects": median("TotalProjects"),
                "mergeMs": median("MergeMs"), "diagnosticCount": median("DiagnosticCount"),
                "buildHostWorkingSet": process_median("PeakBuildHostWorkingSet"),
                "busyCores": process_median("BusyCores"), "buildHostCount": process_median("BuildHostCount"),
                "peakConcurrentBuildHosts": process_median("PeakConcurrentBuildHosts"),
                "maxSampleGapMs": max(run["Process"]["MaxSampleGapMs"] for run in measured),
                "samplerErrors": [error for run in measured for error in run["Process"]["Errors"]],
                "equivalence": "unexplained" if counts else ("natural variance only" if any(comparison["natural"] for comparison in comparisons) else "identical"),
                "strictEquivalence": "non-identical" if any(comparison["strictVerdict"] == "non-identical" for comparison in comparisons) else (
                    "natural variance only" if any(comparison["natural"] for comparison in comparisons) else "identical"),
                "explainedCaseDifferences": [difference for comparison in comparisons for difference in comparison["explainedCase"]],
                "unexplainedCounts": dict(counts), "examples": examples,
                "runComparisons": comparisons,
                "runs": [{key: value for key, value in run.items() if key not in ("Projects", "Diagnostics")} for run in runs.values()],
            }
            item["goEligible"] = (
                solution == "powertoys" and bool(successful) and len(successful) == 3 and combo != "baseline"
                and combo != "baseline-binlog" and successful[0]["Workers"] <= 4
                and item["wallRatio"] <= 0.5 and not counts and item["buildHostWorkingSet"] <= 4_000_000_000
                and not item["samplerErrors"] and item["maxSampleGapMs"] <= 1000)
            entry["combinations"][combo] = item
        summary["solutions"][solution] = entry
    (options.evidence / "comparison.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    for solution, entry in summary["solutions"].items():
        print(f"{solution}: natural differences {len(entry['naturalDifferences'])}")
        for combo, item in entry["combinations"].items():
            print(f"  {combo}: {item['wallMs']:.0f} ms, {item['speedup']:.2f}x, {item['equivalence']}, {item['unexplainedCounts']}")


if __name__ == "__main__":
    main()
