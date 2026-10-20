"""Read-only exhaustive witness for the small Files literal-XML graph; no workspace loads."""
import argparse
import hashlib
import itertools
import json
import math
import pathlib
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument("--solution", type=pathlib.Path, required=True)
parser.add_argument("--snapshot", type=pathlib.Path, required=True)
parser.add_argument("--profile", type=pathlib.Path, required=True)
parser.add_argument("--output", type=pathlib.Path, required=True)
options = parser.parse_args()
solution = options.solution.resolve()
baseline = json.loads(options.snapshot.read_text(encoding="utf-8-sig"))
profile_path = options.profile
profile = json.loads(profile_path.read_text(encoding="utf-8-sig"))
assert baseline.get("Status") == profile.get("Status") == "ok", "A failed baseline/profile cannot support a witness."
canonical = {str((solution.parent / item.attrib["Path"]).resolve()).casefold():
             (solution.parent / item.attrib["Path"]).resolve() for item in ET.parse(solution).iter("Project")}
loaded = {str((solution.parent / key.split("|")[0].removeprefix("<solution>").lstrip("/\\")).resolve()).casefold()
          for key in baseline["Projects"]}
graph = {key: set() for key, path in canonical.items() if key in loaded and path.suffix.casefold() == ".csproj"}
assert set(graph) == loaded, "The literal solution graph must cover every loaded project path."
for key in graph:
    path = canonical[key]
    for item in ET.parse(path).iter():
        if item.tag.rsplit("}", 1)[-1] == "ProjectReference":
            for include in item.attrib.get("Include", "").split(";"):
                if not include or "$(" in include:
                    continue
                target = str((path.parent / include).resolve()).casefold()
                if target in graph:
                    graph[key].add(target)
costs = {}
for item in profile["Projects"]:
    key = str((solution.parent / item["FilePath"]).resolve()).casefold()
    assert item.get("WeightMs") is not None and math.isfinite(item["WeightMs"]) and item["WeightMs"] >= 0
    costs[key] = costs.get(key, 0) + item["WeightMs"]
assert set(graph) <= set(costs), "The profile must cover every project in the graph."
referenced = set().union(*graph.values())
roots = sorted(set(graph) - referenced)


def closure(paths):
    result, remaining = set(), list(paths)
    while remaining:
        path = remaining.pop()
        if path not in result:
            result.add(path)
            remaining.extend(graph[path])
    return result


assert set().union(*(closure([root]) for root in roots)) == set(graph)
assert len(roots) <= 8, "This witness is deliberately bounded to a small exact search."
directories = {}
for path in graph:
    directories.setdefault(str(canonical[path].parent).casefold(), set()).add(path)
results = {}
for workers in (2, 4):
    best, feasible, evaluated = None, 0, 0
    for assignment in itertools.product(range(workers), repeat=len(roots)):
        requests = [[root for root, owner in zip(roots, assignment) if owner == group] for group in range(workers)]
        closures = [closure(paths) for paths in requests]
        core = {path for path in graph if sum(path in group for group in closures) > 1}
        while True:
            previous = len(core)
            core |= closure(core)
            for siblings in directories.values():
                owners = sum(bool(siblings & group) for group in closures)
                if siblings & core or owners > 1:
                    core |= siblings
            if len(core) == previous:
                break
        group_costs = [sum(costs[path] for path in group - core) for group in closures]
        average = sum(group_costs) / workers
        ratio = max(group_costs) / average if average else 1
        item = {"balanceRatio": ratio, "coreCost": sum(costs[path] for path in core),
                "coreCount": len(core), "groupCosts": group_costs, "assignment": assignment}
        evaluated += 1
        feasible += ratio <= 1.15 + 1e-12
        if best is None or (ratio, item["coreCost"]) < (best["balanceRatio"], best["coreCost"]):
            best = item
    results[str(workers)] = {"evaluatedAssignments": evaluated, "feasibleAssignments": feasible, "bestBalance": best}
witness = {
    "scope": "Exact finite enumeration for this literal-XML graph and this profile; not a proof for evaluated imports or other solutions",
    "roots": [str(canonical[root].relative_to(solution.parent)) for root in roots],
    "totalCost": sum(costs[path] for path in graph), "workers": results,
    "sha256": {str(path.relative_to(solution.parent)): hashlib.sha256(path.read_bytes()).hexdigest()
               for path in [solution, *(canonical[key] for key in graph)]},
    "profileSha256": hashlib.sha256(profile_path.read_bytes()).hexdigest(),
    "snapshotSha256": hashlib.sha256(options.snapshot.read_bytes()).hexdigest(),
}
options.output.parent.mkdir(parents=True, exist_ok=True)
options.output.write_text(json.dumps(witness, indent=2), encoding="utf-8")
print(json.dumps(witness, indent=2))


