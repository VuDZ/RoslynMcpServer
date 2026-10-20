"""Task-07 evidence using the unchanged task-04 observation comparator."""
import argparse
import collections
import hashlib
import json
import math
import pathlib
import statistics

from compare import compare, differences


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def median(values):
    present = [value for value in values if value is not None]
    return statistics.median(present) if present else None


def first_phase2(run):
    values = [value for value in run.get("Phase2FirstProgressMs", []) if value is not None]
    return min(values) if values else None


def critical_group(run):
    return max(run.get("GroupMs", []), default=0)


def measurement_issues(run):
    issues = []
    if run.get("WallMs", 0) <= 0 or run.get("UniqueProjects", 0) <= 0:
        issues.append("Zero or missing wall/project measurement")
    process = run.get("Process", {})
    if process.get("Errors", ["missing telemetry"]):
        issues.append("Sampler errors or missing telemetry")
    gap = process.get("MaxSampleGapMs", float("inf"))
    if not math.isfinite(gap) or gap > 1000:
        issues.append(f"Maximum sample gap {gap}ms exceeds the once-per-second sampling requirement")
    return issues


def summarize_combo(baseline, runs, natural):
    comparisons = []
    for run in runs:
        if run["Status"] in ("ok", "plan-violation") and run.get("Projects"):
            result = compare(baseline, run, natural)
            result["runStatus"] = run["Status"]
            comparisons.append(result)
        elif run["Status"] == "ok":
            comparisons.append(compare(baseline, run, natural))
        else:
            comparisons.append({"verdict": run["Status"], "unexplained": [["run", "Failure", None, run.get("Error")]],
                                "natural": [], "explainedCase": [], "strictVerdict": run["Status"]})
    counts = collections.Counter(item[1] for result in comparisons for item in result["unexplained"])
    invalid_measurements = [measurement_issues(run) for run in runs]
    all_valid = all(run["Status"] == "ok" and run.get("PlanViolationCount", 0) == 0 and not issues
                    for run, issues in zip(runs, invalid_measurements))
    wall = median([run.get("WallMs") for run in runs])
    baseline_wall = baseline.get("BaselineMedianMs")
    plan = next((run["Plan"] for run in runs if run.get("Plan")), None)
    process_median = lambda key: median([run.get("Process", {}).get(key) for run in runs])
    return {
        "statusCounts": dict(collections.Counter(run["Status"] for run in runs)),
        "completeValidRuns": all_valid, "wallMs": wall,
        "invalidMeasurementReasons": invalid_measurements,
        "speedup": baseline_wall / wall if all_valid and baseline_wall and wall else None,
        "wallRatio": wall / baseline_wall if all_valid and baseline_wall and wall else None,
        "phase1Ms": median([run.get("Phase1Ms") for run in runs]),
        "phase1FirstProgressMs": median([run.get("Phase1FirstProgressMs") for run in runs]),
        "phase2FirstProgressMs": median([first_phase2(run) for run in runs]),
        "criticalGroupMs": median([critical_group(run) for run in runs]),
        "groupMs": [median([run.get("GroupMs", [])[index] for run in runs if len(run.get("GroupMs", [])) > index])
                    for index in range(max((len(run.get("GroupMs", [])) for run in runs), default=0))],
        "mergeMs": median([run.get("MergeMs") for run in runs]),
        "planningMs": median([run.get("PlanningMs") for run in runs]),
        "aggregatorPreparationMs": median([run.get("AggregatorPreparationMs") for run in runs]),
        "peakMemoryMedian": process_median("PeakBuildHostWorkingSet"),
        "peakMemoryMax": max((run.get("Process", {}).get("PeakBuildHostWorkingSet", 0) for run in runs), default=0),
        "busyCores": process_median("BusyCores"), "buildHostCount": process_median("BuildHostCount"),
        "planViolationCount": sum(run.get("PlanViolationCount", 0) for run in runs),
        "planViolations": [run.get("PlanViolations", []) for run in runs],
        "equivalence": "unexplained" if counts else "identical except explained Windows path case" if any(
            result["explainedCase"] for result in comparisons) else "natural variance only" if any(
            result["natural"] for result in comparisons) else "identical",
        "unexplainedCounts": dict(counts), "comparisons": comparisons,
        "plan": plan, "coreCount": len(plan["Core"]) if plan else None,
        "coreCostShare": plan["CoreCost"] / plan["TotalCost"] if plan and plan["TotalCost"] else None,
        "groupCounts": [len(paths) for paths in plan["Groups"]] if plan else [],
        "groupCosts": plan["GroupCosts"] if plan else [],
        "actualCoreCounts": [len(run.get("ActualCorePaths", [])) for run in runs],
        "actualGroupProjectCountsIncludingAggregator": [run.get("GroupProjectCounts", []) for run in runs],
        "additionalCorePaths": [run.get("AdditionalCorePaths", []) for run in runs],
        "samplerErrors": [run.get("Process", {}).get("Errors", []) for run in runs],
        "maxSampleGapMs": max((run.get("Process", {}).get("MaxSampleGapMs", 0) for run in runs), default=0),
    }


def step0(artifacts):
    baseline = read(artifacts / "step0-baseline.json")
    controls = {}
    for variant in ("no-restore", "restore", "sequential"):
        run = read(artifacts / f"step0-{variant}.json")
        result = compare(baseline, run, {}) if run["Status"] == "ok" else None
        controls[variant] = {"status": run["Status"], "wallMs": run.get("WallMs"),
                             "uniqueProjects": run.get("UniqueProjects"), "buildHosts": run.get("Process", {}).get("BuildHostCount"),
                             "returnedCore": run.get("CoreReturnedFromGroups"), "planViolationCount": run.get("PlanViolationCount"),
                             "comparison": result, "diagnostics": run.get("Diagnostics"), "error": run.get("Error")}
    inventories = [read(artifacts / f"step0-obj-{name}.json") for name in ("before", "after-baseline", "after-aggregator")]
    changed = lambda left, right: sorted(path for path in left.keys() | right.keys() if left.get(path) != right.get(path))
    ordinary = changed(inventories[0], inventories[1])
    aggregator = changed(inventories[1], inventories[2])
    return {"selected": "no-restore", "controls": controls,
            "ordinaryChangedPaths": ordinary, "aggregatorChangedPaths": aggregator,
            "extraAggregatorChangedPaths": sorted(set(aggregator) - set(ordinary)),
            "inventoryCounts": [len(inventory) for inventory in inventories],
            "witnessLimitation": "Before/after SHA-256, size and mtime inventory detects persistent mutations; transient writes are not observed."}


def projections(summary):
    entries = [(name, value) for name, value in summary["solutions"]["powertoys"]["combinations"].items()
               if name != "baseline" and value["completeValidRuns"] and int(name.rsplit("-", 1)[1]) <= 4]
    if not entries:
        return {"available": False, "reason": "No complete valid PowerToys combination for model extraction.", "rows": []}
    name, value = min(entries, key=lambda pair: pair[1]["wallMs"])
    workers = int(name.rsplit("-", 1)[1])
    baseline = summary["solutions"]["powertoys"]["baselineMs"]
    h1, h2 = value["phase1FirstProgressMs"] or 0, value["phase2FirstProgressMs"] or 0
    core = max(0, (value["phase1Ms"] or 0) - h1)
    group = max(0, (value["criticalGroupMs"] or 0) - h2)
    merge = value["mergeMs"] or 0
    plan = value["plan"]
    expected_group = max(plan["GroupCosts"]) / plan["TotalCost"] * max(1, baseline - h1)
    competition = group / expected_group if expected_group else 1
    rows = []
    for target in (120000, 300000):
        for fraction in (.05, .22, .5):
            estimate = h1 + h2 + merge + max(0, target - h1) * (fraction + competition * (1 - fraction) / workers)
            rows.append({"baselineSeconds": target / 1000, "coreShare": fraction,
                         "predictedSeconds": estimate / 1000, "predictedSpeedup": target / estimate})
    return {"available": True, "combination": name, "workers": workers,
            "h1ProxyMs": h1, "coreResidualMs": core, "h2ProxyMs": h2, "criticalGroupResidualMs": group,
            "mergeMs": merge, "competitionProxy": competition, "reconstructedMs": h1 + core + h2 + group + merge,
            "observedWallMs": value["wallMs"], "rows": rows,
            "formula": "h1+h2+merge+(target-h1)*(coreShare+competition*(1-coreShare)/N)",
            "limitations": "Unvalidated extrapolation with balanced non-core work and constant host/merge costs. h values include first public progress work, not direct host startup. Competition is a profile-based proxy; graph/load mode differs from baseline. This is not a measured speedup."}


def fmt(value, divisor=1, digits=3):
    return "—" if value is None else f"{value / divisor:.{digits}f}"


def render(summary):
    proof = summary["step0"]
    lines = ["# D/E0/task-07 — Core-first parallel spike", "", "Дата: **2026-10-10**.", "",
             f"Вердикт: **{summary['verdict']}**. Production, Version и CHANGELOG не менялись; scope — standalone spike и evidence.", "",
             "## Шаг 0 и публичный API", "",
             "Выбран вариант 1: SDK-агрегатор без restore, ReferenceOutputAssembly=false, SkipGetTargetFrameworkProperties=true, Private=false. "
             "global.json скопирован; пустые Directory.Build.props/targets останавливают walk-up; все временные файлы находятся вне clone и удаляются после нагрузки.", "",
             "| Вариант Files N=2 | Status | Wall, с | Проекты | BuildHosts | Plan violations |",
             "|---|---|---:|---:|---:|---:|"]
    for name, value in proof["controls"].items():
        lines.append(f"| {name} | {value['status']} | {fmt(value['wallMs'],1000)} | {value['uniqueProjects']} | {value['buildHosts']} | {value['planViolationCount']} |")
    lines.extend(["", "Restore только агрегатора (RestoreRecursive=false) и последовательный контроль загружают то же решение. "
                  "Вариант 2 не нужен: отсутствие assets не мешает ProjectReference; вариант 3 добавляет холодные BuildHost на каждый корень. "
                  "Сравнение diagnostics остаётся строгим; результаты не используются для расширения естественной вариации.", "",
                  "Декомпиляция Microsoft.CodeAnalysis.Workspaces.MSBuild 5.9.0: ProjectMap.Create(Solution) сохраняет ProjectInfo; "
                  "Worker.LoadProjectInfosFromPathAsync возвращает попадание карты до BuildHost. Worker.LoadAsync возвращает requested и собственный discovered dictionary, "
                  "готовое ядро из карты в этот dictionary не добавляется. В Files ядро присутствует только в фазе 1, обе группы вернули 0 проектов ядра. "
                  "Слияние сохраняет ProjectId ядра, aliases/EmbedInteropTypes, отдельные TFM и opaque native targets; агрегатор и ссылки на него удаляются.", "",
                  f"Obj witness: по {proof['inventoryCounts'][0]} файлов до/после, ordinary изменил {len(proof['ordinaryChangedPaths'])} путей, "
                  f"агрегатор — {len(proof['aggregatorChangedPaths'])}; новых относительно ordinary путей: {len(proof['extraAggregatorChangedPaths'])}. "
                  "Сняты SHA-256, size и mtime. Это свидетельство сохранённых изменений для Files, transient writes не наблюдаются. Полные inventory в artifacts.", "",
                  "## Протокол и медианы", "",
                  "HEAD/restore проверены заново, Debug|x64 и global properties task-04. Каждый прогон — новый процесс MCP run_dotnet_run; "
                  "по комбинации прогрев 0 и n=3 (1/2/3). Cache ОС и антивирус сохранены. Plan/aggregator setup входят в wall; snapshot observation и завершение sampler — после wall. "
                  "Graph snapshot берёт точные references baseline этой серии; XML — буквальные XML edges с baseline inventory загружаемых проектов. "
                  "Ключи профиля task-05.1 нечувствительны к регистру, запросы сохраняют исходное написание путей solution.", "",
                  "| Решение / комбинация | Wall, с | × baseline | Planned/actual core count; planned cost | Фаза 1, с | Critical group, с | Merge, мс | Peak median/max, GiB | Busy cores | Violations | Эквивалентность |",
                  "|---|---:|---:|---|---:|---:|---:|---|---:|---:|---|"])
    for solution, entry in summary["solutions"].items():
        for name, value in entry["combinations"].items():
            core = "—" if value["coreCount"] is None else f"{value['coreCount']}/{fmt(median(value['actualCoreCounts']),digits=0)}; {fmt(value['coreCostShare'],.01,2)}%"
            memory = f"{fmt(value['peakMemoryMedian'],2**30)} / {fmt(value['peakMemoryMax'],2**30)}"
            lines.append(f"| {solution} / {name} | {fmt(value['wallMs'],1000)} | {fmt(value['speedup'])} | {core} | "
                         f"{fmt(value['phase1Ms'],1000)} | {fmt(value['criticalGroupMs'],1000)} | {fmt(value['mergeMs'])} | {memory} | "
                         f"{fmt(value['busyCores'])} | {value['planViolationCount']} | {value['equivalence']} ({value['statusCounts']}) |")
    lines.extend(["", "Failed/timeout/plan-violation сохраняются в строках и comparisons; ускорение для неполных или некорректных комбинаций не объявляется. "
                  "MaxSampleGapMs>1000 мс отвергает measurement: требование task-04 — хотя бы один опрос в секунду. "
                  "Естественная вариация — только значения baseline 1/2. Неизменённый compare.py сравнивает поля task-04 и diagnostics; "
                  "объясняется только регистр Windows paths в прежних категориях. Никакие новые diagnostics не подавляются.", "",
                  "Размеры, стоимости каждой группы, полные планы, phase timings каждого прогона, статусы и находки — [summary.json](task-07-core-first-parallel-spike/summary.json).", "",
                  "## План и различия", ""])
    for solution, entry in summary["solutions"].items():
        for name, value in entry["combinations"].items():
            if name == "baseline":
                continue
            lines.append(f"- {solution}/{name}: core={value['coreCount']}, group counts={value['groupCounts']}, group costs={value['groupCosts']}; "
                         f"unexplained={value['unexplainedCounts']}; additional core={value['additionalCorePaths']}.")
    lines.extend(["", "Различия режима requested/discovered остаются unexplained. Отсутствие native-request Failure diagnostics у групп "
                  "показано baseline/aggregator/sequential контролями Files и публичным Worker code: LoadSolutionInfoAsync запрашивает native проекты, "
                  "loadable roots через LoadProjectInfoAsync не запрашивают исходный native inventory. В ResolveReferencesAsync "
                  "первая проверка loadability использует Ignore, затем возможен metadata fallback; это другой путь, чем прямой requested native load. "
                  "Для E1 потребовалось бы сохранить весь список original requested projects и диагностический режим, проверить семантику missing/unloadable targets "
                  "и заново доказать эквивалентность. Такой адаптер не реализован и не считается объяснением для go. "
                  "[Разбор ROA=false, snapshot ownership и реального изменения OutputFilePath](task-07-core-first-parallel-spike/graph-and-load-mode.md) "
                  "содержит полный inventory: XML даёт 15 OutputFilePath и 94 diagnostics различия на прогон; необъяснённых document различий нет.", "", "## Модель и прогноз", ""])
    lines.insert(lines.index("## Модель и прогноз") - 1,
                 "В Files planned core N2=1, N4=3; actual core=2/4: фаза 1 дополнительно обнаружила Files.Core.SourceGenerator через "
                 "dependency load, не представленную compilation snapshot. Его WeightMs=0 в профиле task-05.1, поэтому planned cost share "
                 "не меняется; count модели и фактический count различаются. Table labels и additionalCorePaths сохраняют эту разницу. "
                 "Размеры групп в плане тоже planned; фактические counts с агрегатором сохранены отдельно. "
                 "Прогрев 0 отражён в warmup каждой комбинации summary; speedup/gate вычисляются по measured1/2/3.")
    model = summary["model"]
    if model["available"]:
        lines.append(f"Комбинация {model['combination']}, N={model['workers']}: h1 proxy={fmt(model['h1ProxyMs'])} мс, core residual={fmt(model['coreResidualMs'])} мс, "
                     f"h2 proxy={fmt(model['h2ProxyMs'])} мс, max(group) residual={fmt(model['criticalGroupResidualMs'])} мс, merge={fmt(model['mergeMs'])} мс. "
                     f"Сумма ≈ {fmt(model['reconstructedMs'],1000)} с, measured wall={fmt(model['observedWallMs'],1000)} с; setup/plan и scheduling объясняют разницу. "
                     f"Competition proxy={fmt(model['competitionProxy'])}.")
        lines.extend(["", "h — время до первого progress callback и включает работу первого evaluation/build; точное время spawn публичный API не отдаёт. "
                      "Core и group residual получены вычитанием этого proxy, не независимым профилированием. "
                      "Прогноз ниже не измерен и не валидирован; предполагает постоянный host/merge, равномерные группы и переносимый competition proxy. "
                      "При no-go он не обещает production ускорения.", "", "| Baseline, с | Core share | Прогноз, с | Прогноз × |", "|---:|---:|---:|---:|"])
        for row in model["rows"]:
            lines.append(f"| {row['baselineSeconds']:.0f} | {row['coreShare']:.0%} | {row['predictedSeconds']:.3f} | {row['predictedSpeedup']:.3f} |")
    else:
        lines.append(model["reason"] + " Прогноз не вычислен.")
    lines.extend(["", "## Gate, Files и validation", "",
                  f"Gate: strict equivalence={summary['gate']['strictEquivalence']}; PowerToys N≤4 wall≤75% baseline={summary['gate']['speed']}; "
                  f"max measured BuildHost peak≤4GB={summary['gate']['memory']}; matrix complete={summary['gate']['complete']}.", "",
                  "Для Files отдельный go не выносится. Условие отказа от параллели: оценка h1+core+h2+max(group)+merge+setup "
                  "не меньше ordinary baseline либо память N не укладывается в лимит. Это предпочтительнее порога только по count: распределение стоимости существенно. "
                  "При нарушении плана или несовпадении observation нужна обычная последовательная загрузка.", "",
                  "Матрица снята до исправлений failure handling. [Измеренные исходники](task-07-core-first-parallel-spike/measured-source-sha256.json) "
                  "отделены от [финальных исходников](task-07-core-first-parallel-spike/source-sha256.json): после матрицы добавлены cached sampler finalization, "
                  "durable group outcomes и regression injection. Последовательность успешной загрузки, граф, global properties, merger и границы wall сохранены; "
                  "ошибочные ветки теперь сохраняют первоначальную причину и результаты соседних групп. Это не повтор всей матрицы на финальном runner. "
                  "[Точная дельта C# хэшей](task-07-core-first-parallel-spike/post-matrix-source-delta.json) связывает обе версии. "
                  "CoreFirstChecks проверяет сериализацию исходной ошибки после stop, отдельную sampler error и group failure/timeout с partial projects/progress. "
                  "Standalone Release build и check-core-first/check-merge/check-profile прошли через Roslyn MCP. Координатор выполняет финальный Release solution build "
                  "и полный main Category!=AnalyzerLifecycle; итог formal acceptance/status записывает после review/validation. "
                  "AnalyzerLifecycle не нужен: production lifecycle/paths не менялись. Подробности финальной validation добавляются координатором.", "",
                  "Тяжёлые данные — artifacts/parallel-load/task-07-evidence; в docs только summary и "
                  "[SHA-256 inventory](task-07-core-first-parallel-spike/artifact-sha256.json). Компаратор task-04 и поля SolutionObservation не изменены.", ""])
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--artifacts", required=True, type=pathlib.Path)
    parser.add_argument("--evidence", required=True, type=pathlib.Path)
    options = parser.parse_args()
    options.evidence.mkdir(parents=True, exist_ok=True)
    summary = {"solutions": {}, "step0": step0(options.artifacts)}
    all_runs = []
    complete = True
    for solution, combinations in (("powertoys", [f"core-first-{graph}-{workers}" for graph in ("xml", "snapshot") for workers in (2, 4, 8)]),
                                   ("files", [f"core-first-snapshot-{workers}" for workers in (2, 4)])):
        runs_by_combo = {}
        warmups = {}
        for combo in ["baseline"] + combinations:
            runs = []
            for index in (0, 1, 2, 3):
                path = options.artifacts / f"{solution}-{combo}-{index}.json"
                run = read(path) if path.exists() else {"Status": "missing", "Error": str(path.name), "Process": {}}
                complete &= path.exists()
                if index == 0:
                    warmups[combo] = {"status": run["Status"], "wallMs": run.get("WallMs"),
                                      "planViolationCount": run.get("PlanViolationCount", 0), "error": run.get("Error"),
                                      "invalidMeasurementReasons": measurement_issues(run)}
                else:
                    runs.append(run)
                    all_runs.append(run)
            runs_by_combo[combo] = runs
        baseline_runs = runs_by_combo["baseline"]
        baseline_valid = all(run["Status"] == "ok" for run in baseline_runs)
        if baseline_valid:
            natural_differences = differences(baseline_runs[0], baseline_runs[1])
            natural = {(key, category): [before, after] for key, category, before, after in natural_differences}
            baseline_ms = median([run["WallMs"] for run in baseline_runs])
            reference = dict(baseline_runs[0], BaselineMedianMs=baseline_ms)
        else:
            natural_differences, natural, baseline_ms = [], {}, None
            reference = {"Projects": {}, "Diagnostics": [], "BaselineMedianMs": None}
        entry = {"baselineMs": baseline_ms, "baselineValid": baseline_valid, "naturalDifferences": natural_differences, "combinations": {}}
        for combo, runs in runs_by_combo.items():
            entry["combinations"][combo] = summarize_combo(reference, runs, natural)
            entry["combinations"][combo]["warmup"] = warmups[combo]
        summary["solutions"][solution] = entry
    strict = all(value["completeValidRuns"] and not value["unexplainedCounts"] for entry in summary["solutions"].values()
                 for value in entry["combinations"].values())
    speed = any(value["wallRatio"] is not None and value["wallRatio"] <= .75
                for name, value in summary["solutions"]["powertoys"]["combinations"].items()
                if name != "baseline" and int(name.rsplit("-", 1)[1]) <= 4)
    memory = all(run.get("Process", {}).get("PeakBuildHostWorkingSet", float("inf")) <= 4_000_000_000 for run in all_runs)
    summary["gate"] = {"strictEquivalence": strict, "speed": speed, "memory": memory, "complete": complete}
    summary["verdict"] = "go" if all(summary["gate"].values()) else "no-go"
    summary["model"] = projections(summary)
    detailed_comparisons = {}
    for solution, entry in summary["solutions"].items():
        detailed_comparisons[solution] = {}
        for combo, value in entry["combinations"].items():
            comparisons = value.pop("comparisons")
            detailed_comparisons[solution][combo] = comparisons
            value["comparisonSummary"] = [{"verdict": result["verdict"], "strictVerdict": result["strictVerdict"],
                                           "runStatus": result.get("runStatus", "ok"),
                                           "unexplainedCount": len(result["unexplained"]), "naturalCount": len(result["natural"]),
                                           "explainedCaseCount": len(result["explainedCase"])} for result in comparisons]
            value["diagnosticExamples"] = list(dict.fromkeys(item[0] for result in comparisons for item in result["unexplained"]
                                                             if item[1] == "Diagnostics"))[:5]
    (options.artifacts / "task07-comparisons.json").write_text(json.dumps(detailed_comparisons, ensure_ascii=False, indent=2), encoding="utf-8")
    (options.evidence / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    hashes = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(options.artifacts.glob("*.json"))}
    (options.evidence / "artifact-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    source = pathlib.Path(__file__).parent
    hashes = {path.relative_to(source).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
              for path in sorted(source.rglob("*")) if path.is_file() and "bin" not in path.parts and "obj" not in path.parts and
              path.suffix in (".cs", ".csproj", ".py", ".md")}
    (options.evidence / "source-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    measured = options.artifacts / "measured-source-sha256.json"
    if measured.exists():
        (options.evidence / "measured-source-sha256.json").write_bytes(measured.read_bytes())
        measured_hashes = {item["Path"].removeprefix("spikes/ParallelLoad/"): item["SHA256"].lower()
                           for item in read(measured) if item["Path"].endswith(".cs")}
        final_hashes = {path: digest for path, digest in hashes.items() if path.endswith(".cs")}
        delta = {path: {"measured": measured_hashes.get(path), "final": final_hashes.get(path)}
                 for path in sorted(measured_hashes.keys() | final_hashes.keys()) if measured_hashes.get(path) != final_hashes.get(path)}
        (options.evidence / "post-matrix-source-delta.json").write_text(json.dumps(delta, indent=2), encoding="utf-8")
    options.evidence.with_suffix(".md").write_text(render(summary), encoding="utf-8")
    print(f"Task-07 {summary['verdict']}: {summary['gate']}")


if __name__ == "__main__":
    main()
