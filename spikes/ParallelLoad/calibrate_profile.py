"""Cross-validate gap weights against held-out task-04 strategies; never authorize a failed model."""
import argparse
import collections
import datetime
import gzip
import hashlib
import json
import pathlib
import statistics

from compare import compare, differences


def read(path):
    if path.suffix == ".gz":
        with gzip.open(path, "rt", encoding="utf-8-sig") as stream:
            return json.load(stream)
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding="utf-8")


def closure(seeds, graph):
    found, pending = set(), list(seeds)
    while pending:
        key = pending.pop()
        if key in found:
            continue
        found.add(key)
        pending.extend(graph[key])
    return found


def solve(matrix, vector):
    rows = [list(row) + [value] for row, value in zip(matrix, vector)]
    for column in range(len(rows)):
        pivot = max(range(column, len(rows)), key=lambda i: abs(rows[i][column]))
        rows[column], rows[pivot] = rows[pivot], rows[column]
        divisor = rows[column][column]
        if abs(divisor) < 1e-12:
            raise ValueError("Calibration is underdetermined.")
        rows[column] = [value / divisor for value in rows[column]]
        for index in range(len(rows)):
            if index != column:
                multiplier = rows[index][column]
                rows[index] = [a - multiplier * b for a, b in zip(rows[index], rows[column])]
    return [row[-1] for row in rows]


def fit(samples):
    # Equal weight per group in relative squared error, including small/empty cluster groups.
    # Nonnegative least squares with four variables is small enough to enumerate active constraints.
    design = []
    for workers, weight, measured in samples:
        row = [1 / measured] + [weight / measured if workers == n else 0 for n in (2, 4, 8)]
        design.append(row)
    candidates = []
    for mask in range(1, 16):
        active = [i for i in range(4) if mask & (1 << i)]
        matrix = [[sum(row[i] * row[j] for row in design) for j in active] for i in active]
        vector = [sum(row[i] for row in design) for i in active]
        try:
            values = solve(matrix, vector)
        except ValueError:
            continue
        if any(value < 0 for value in values):
            continue
        parameters = [0.0] * 4
        for index, value in zip(active, values):
            parameters[index] = value
        error = sum((sum(a * b for a, b in zip(row, parameters)) - 1) ** 2 for row in design)
        candidates.append((error, parameters))
    _, parameters = min(candidates)
    return {"HostMs": parameters[0], "Competition": dict(zip((2, 4, 8), parameters[1:])), "Validated": False}


def timestamp(text):
    return datetime.datetime.fromisoformat(text).timestamp()


def native_summary(snapshot, profile, order):
    failures = [json.loads(item) for item in snapshot["NativeFailures"]]
    unsupported = [item for item in failures if item["Message"].startswith("Cannot open project '")]
    native_order = [path for path in order if path.lower().endswith(".vcxproj")]
    unique = {item["Message"].split("'")[1].replace("\\", "/").casefold() for item in unsupported}
    assert all(("<solution>/" + path).casefold() in unique for path in native_order)
    intervals = [interval for session in profile["Sessions"] for interval in session["Intervals"]]
    associated = []
    inside_build = outside = gap_callbacks = 0
    for interval in intervals:
        previous, start, end = map(timestamp, (interval["PreviousEnd"], interval["Start"], interval["End"]))
        gap = [item for item in unsupported if previous <= timestamp(item["Timestamp"]) < start]
        if gap:
            associated.append({"chargedTo": interval["FilePath"], "gapMsUpperBound": (start - previous) * 1000,
                               "failureCallbacks": len(gap), "nativePaths": sorted({item["Message"].split("'")[1] for item in gap})})
            gap_callbacks += len(gap)
        inside_build += sum(start <= timestamp(item["Timestamp"]) <= end for item in unsupported)
    outside = len(unsupported) - gap_callbacks - inside_build
    first_times = {}
    for item in unsupported:
        first_times.setdefault(item["Message"].split("'")[1], timestamp(item["Timestamp"]))
    return {"solutionNativeProjects": len(native_order), "uniqueUnsupportedNativePaths": len(unique),
            "unsupportedFailureCallbacks": len(unsupported), "msbuildNativeFailureCallbacks": len(failures) - len(unsupported),
            "nativeProgressEvents": len(snapshot["NativeProgress"]), "callbacksWithinGaps": gap_callbacks,
            "callbacksWithinOwnBuild": inside_build, "callbacksOutsideBinlogInterval": outside,
            "firstOccurrenceSpanMs": (max(first_times.values()) - min(first_times.values())) * 1000 if first_times else 0,
            "nativeAssociatedGapMsUpperBound": sum(item["gapMsUpperBound"] for item in associated),
            "nativeAttemptMs": None,
            "timingLimit": "Failure reports completion without attempt start; native progress is absent. Gap bounds include Roslyn/evaluation work and must not be reported as isolated native duration.",
            "modelAttribution": "Keep the entire inter-build gap, including native activity, in the following C# project weight; the first gap remains host cost.",
            "associatedGaps": associated}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--artifacts", type=pathlib.Path, required=True)
    parser.add_argument("--task04", type=pathlib.Path, required=True)
    parser.add_argument("--evidence", type=pathlib.Path, required=True)
    options = parser.parse_args()
    inventory = []
    for path in sorted(options.task04.glob("*.json.gz")):
        snapshot = read(path)
        assert snapshot["Status"] == "ok" and snapshot["UniqueProjects"] > 0
        inventory.append({"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                          "groups": snapshot["GroupProjectCounts"], "wallMs": snapshot["WallMs"]})
    assert len(inventory) == 44, "The task-04 inventory must include all 44 snapshots."
    write(options.evidence / "task04-inventory.json", inventory)
    summary = {"step2": "not-started", "solutions": {}}
    for name in ("powertoys", "files"):
        folder = options.artifacts / name
        profile = read(folder / "profile.json")
        snapshot = read(folder / "profile.load.json")
        exported = read(folder / "partitions.json")
        assert profile["Status"] == "ok" and profile["ReconciliationError"] <= 0.1
        projects = {project["Key"].casefold(): project for project in profile["Projects"]}
        baseline = [read(options.task04 / f"{name}-baseline-{run}.json.gz") for run in (1, 2, 3)]
        def canonical_target(target):
            normalized = target.replace("\\", "/").casefold()
            if normalized in projects:
                return normalized
            matches = [key for key, project in projects.items()
                       if normalized.endswith("/" + project["FilePath"].casefold() + "|" + project["Name"].casefold())]
            assert len(matches) == 1, "A task-04 reference must resolve uniquely to a loaded project."
            return matches[0]
        graph = {}
        for key, project in baseline[0]["Projects"].items():
            graph[key.replace("\\", "/").casefold()] = [canonical_target(json.loads(item)["Target"])
                                                        for item in json.loads(project["ProjectReferences"])]
        assert set(graph) == set(projects)
        for key, project in projects.items():
            assert set(graph[key]) == {reference.casefold() for reference in project["References"]}
        paths = collections.defaultdict(list)
        for key, project in projects.items():
            paths[project["FilePath"].casefold()].append(key)
        costs = {key: value["WeightMs"] for key, value in projects.items()}
        assert all(value is not None and value >= 0 for value in costs.values())
        partitions, weights, reconstruction = {}, {}, {}
        for combo, groups in exported["Partitions"].items():
            closures = [closure([key for path in group for key in paths[path.casefold()]], graph) for group in groups]
            partitions[combo] = closures
            weights[combo] = [sum(costs[key] for key in group) for group in closures]
            captured = sorted(options.task04.glob(f"{name}-{combo}-*.json.gz"))
            count_checks = []
            for path in captured:
                actual = read(path)["GroupProjectCounts"]
                count_checks.append({"file": path.name, "actualCounts": actual,
                                     "matchesGraphClosure": [len(group) for group in closures] == actual})
            reconstruction[combo] = {"requestedCounts": [len(group) for group in groups],
                                     "closureCounts": [len(group) for group in closures], "weightMs": weights[combo],
                                     "snapshotCountChecks": count_checks}
        raw_difference_counts = dict(collections.Counter(item[1] for item in differences(baseline[0], snapshot)))
        for observed in baseline + [snapshot]:
            for observed_project in observed["Projects"].values():
                references = []
                for item in json.loads(observed_project["ProjectReferences"]):
                    reference = json.loads(item)
                    reference["Target"] = projects[canonical_target(reference["Target"])]["Key"]
                    references.append(json.dumps(reference, separators=(",", ":")))
                observed_project["ProjectReferences"] = json.dumps(references, separators=(",", ":"))
        natural = {(key, category): [before, after] for key, category, before, after in differences(baseline[0], baseline[1])}
        comparison = compare(baseline[0], snapshot, natural)
        write(options.evidence / f"{name}-comparison.json", comparison)
        native = native_summary(snapshot, profile, exported["SolutionOrder"])
        write(options.evidence / f"{name}-native.json", native)
        weight_values = sorted(costs.values())
        build_total = sum(project["OwnBuildMs"] for project in projects.values())
        first_total = sum(session["FirstMs"] for session in profile["Sessions"])
        weight_total = sum(weight_values)
        entry = {"status": profile["Status"], "projects": len(projects), "baselineMedianMs": statistics.median(item["WallMs"] for item in baseline),
                 "profileWallMs": profile["BaselineWallMs"], "loadSolutionMs": profile["LoadSolutionMs"],
                 "intervalSumMs": first_total + weight_total, "reconciliationError": profile["ReconciliationError"],
                 "weightSumMs": weight_total, "firstMs": first_total, "firstShare": first_total / (weight_total + first_total),
                 "weightDistributionMs": {"min": min(weight_values), "median": statistics.median(weight_values),
                                          "p90": weight_values[int(0.9 * (len(weight_values) - 1))], "max": max(weight_values)},
                 "ownBuildMs": build_total, "ownBuildShareOfFullInterval": build_total / (first_total + weight_total),
                 "ownBuildShareOfWeight": sum(p["OwnBuildMs"] for p in projects.values() if p["FirstMs"] == 0) / weight_total,
                 "markup": [{"name": project["Name"], "evidence": project["MarkupEvidence"]} for project in projects.values() if project["Markup"]],
                 "nullEvaluationCount": sum(p["EvaluationMs"] is None for p in projects.values()),
                 "registrationChanged": snapshot["Registration"] != baseline[0]["Registration"],
                 "comparisonVerdict": comparison["verdict"], "partitions": reconstruction}
        entry["rawDifferenceCounts"] = raw_difference_counts
        entry["closureReconstructionComplete"] = all(check["matchesGraphClosure"]
            for combo in reconstruction.values() for check in combo["snapshotCountChecks"])
        if name == "powertoys":
            assert {"FancyZonesEditor", "ColorPickerUI"} <= {item["name"] for item in entry["markup"]}
            models, validations = {}, {}
            for training, held_out in (("round-robin", "cluster"), ("cluster", "round-robin")):
                samples = []
                for workers in (2, 4, 8):
                    combo = f"{training}-{workers}"
                    for run in (1, 2, 3):
                        observed = read(options.task04 / f"{name}-{combo}-{run}.json.gz")
                        samples.extend((workers, weight, measured) for weight, measured in zip(weights[combo], observed["GroupMs"]))
                model = fit(samples)
                models[training] = model
                validation = {}
                for workers in (2, 4, 8):
                    combo = f"{held_out}-{workers}"
                    predicted = [model["HostMs"] + model["Competition"][workers] * weight for weight in weights[combo]]
                    errors, measured_groups, wall_errors = [], [], []
                    for run in (1, 2, 3):
                        measured = read(options.task04 / f"{name}-{combo}-{run}.json.gz")["GroupMs"]
                        measured_groups.append(measured)
                        errors.extend(abs(a - b) / b for a, b in zip(predicted, measured))
                        wall_errors.append(abs(max(predicted) - max(measured)) / max(measured))
                    median_error = statistics.median(errors)
                    median_wall_error = statistics.median(wall_errors)
                    validation[combo] = {"predictedGroupMs": predicted, "measuredGroupMs": measured_groups,
                                         "medianGroupAbsoluteError": median_error, "medianWallAbsoluteError": median_wall_error,
                                         "wallErrorsByRun": wall_errors, "groupErrors": errors,
                                         "passed": median_error <= 0.15 and all(error <= 0.1 for error in wall_errors)}
                validations[f"{training}-to-{held_out}"] = validation
            valid = entry["closureReconstructionComplete"] and all(
                item["passed"] for direction in validations.values() for item in direction.values())
            for model in models.values():
                model["Validated"] = valid
            entry.update({"models": models, "validation": validations, "modelValidated": valid})
            # This is the specified RR-trained direction, not a fit selected after seeing held-out errors.
            profile["Model"] = models["round-robin"]
            write(folder / "model-profile.json", profile)
            entry["referenceSimulation"] = {}
            for combo, groups in partitions.items():
                multiplicity = collections.Counter(key for group in groups for key in group)
                core = {key for key, count in multiplicity.items() if count > 1}
                workers = int(combo.rsplit("-", 1)[1])
                for training, model in models.items():
                    predicted = [model["HostMs"] + model["Competition"][workers] * weight for weight in weights[combo]]
                    entry["referenceSimulation"][f"{training}-model/{combo}"] = {
                        "admissible": valid, "groupMs": predicted, "wallMs": max(predicted),
                        "speedup": entry["baselineMedianMs"] / max(predicted),
                        "repeatedProjects": sum(multiplicity.values()) - len(projects), "sharedCoreCount": len(core),
                        "sharedCoreWeightShare": sum(costs[key] for key in core) / weight_total,
                        "repeatedMarkup": sorted(projects[key]["Name"] for key in core if projects[key]["Markup"])}
            entry["planSimulation"] = {}
            for workers in (2, 4, 8):
                plan_path = options.evidence / f"plan-{workers}.json"
                if not plan_path.exists():
                    continue
                plan = read(plan_path)
                assert plan["Status"] == ("validated-model" if valid else "uncalibrated-model")
                assert not plan["RepeatedMarkup"]
                plan_groups = [{key.casefold() for key in group} for group in plan["Closures"]]
                assert set.union(*plan_groups) == set(projects)
                core = {key.casefold() for key in plan["SharedCore"]}
                for training, model in models.items():
                    predicted = [model["HostMs"] + model["Competition"][workers] * sum(costs[key] for key in group)
                                 for group in plan_groups]
                    if training == "round-robin":
                        assert all(abs(a - b) < 1e-6 for a, b in zip(predicted, plan["PredictedGroupMs"]))
                    entry["planSimulation"][f"{training}-model/plan-{workers}"] = {
                        "admissible": valid, "groupMs": predicted, "wallMs": max(predicted),
                        "wallRatio": max(predicted) / entry["baselineMedianMs"],
                        "speedup": entry["baselineMedianMs"] / max(predicted),
                        "repeatedProjects": plan["RepeatedProjects"], "sharedCoreCount": len(core),
                        "sharedCoreWeightShare": sum(costs[key] for key in core) / weight_total,
                        "repeatedMarkup": [], "closureCounts": [len(group) for group in plan_groups]}
            summary["modelValidated"] = valid
            summary["verdict"] = "pending-plan-gate" if valid else "no-go: model unsuitable"
            if entry["planSimulation"]:
                candidates = [item for key, item in entry["planSimulation"].items()
                              if key.startswith("round-robin-model/") and int(key.rsplit("-", 1)[1]) <= 4]
                summary["gate"] = {"baselineMs": entry["baselineMedianMs"],
                                   "thresholdMs": 0.45 * entry["baselineMedianMs"],
                                   "modelPassed": valid,
                                   "predictionPassed": any(item["wallRatio"] <= 0.45 for item in candidates),
                                   "step2Authorized": valid and any(item["wallRatio"] <= 0.45 for item in candidates)}
                summary["verdict"] = "go-to-step2" if summary["gate"]["step2Authorized"] else "no-go"
        write(options.evidence / ("profile.json" if name == "powertoys" else "files-profile.json"), profile)
        write(options.evidence / f"{name}-partitions.json", exported)
        summary["solutions"][name] = entry
    write(options.evidence / "summary.json", summary)
    print(json.dumps({"verdict": summary["verdict"], "validations": summary["solutions"]["powertoys"]["validation"]}, indent=2))


if __name__ == "__main__":
    main()
