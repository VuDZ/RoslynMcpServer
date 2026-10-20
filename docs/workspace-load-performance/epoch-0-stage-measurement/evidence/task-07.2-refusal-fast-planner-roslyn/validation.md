# Task-07.2 — независимое ревью и финальная validation

Дата: **2026-10-10**, Europe/Moscow. Статус: **implemented; матрица завершена, no-go; независимое ревью round 2 ACCEPT для standalone реализации/evidence отрицательного эксперимента; формальная приёмка code/tests pending зелёного main CI**.

Scope — standalone spike, фиксированная политика отказа/force, bounded planner, public progress, binlog replay и evidence. **36 matrix runs**: baseline трёх решений и шесть parallel/refusal комбинаций, каждую запускали новым процессом spike с warmup 0 и measured 1/2/3. Дополнительно: Roslyn prerequisite, три ordinary profile runs (PowerToys/Roslyn — требуемая binlog диагностика, Files — профиль), две parallel N4 binlog диагностики. Post-load failures/fallback не скрываются; все строки сохранены. Production, Version, CHANGELOG, solution и CI не менялись.

## Независимые раунды

Один исполнитель — сабагент Codex (GPT-6, модель сессии унаследована); все исправления принадлежат ему. Два новых независимых рецензента Codex (GPT-6).

1. [Round 1](review-round-1.md) — MUST-FIX: четыре замечания (interruptible budget и запрет позднего materialization; баланс zero/zero; отдельный model-unavailable; actual parallel diagnostic gate). Исправлены тем же исполнителем одним фикс-раундом.
2. [Round 2](review-round-2.md) — implementation ACCEPT до измерений и **финальный data audit ACCEPT**, 2026-10-10. Проверены все 36 строки, пересчёт summary/comparisons, хэши, SDK 10.0.301 и реальные TRX результаты. Report-only disclosures уточнили ограничение детерминизма deadline-limited N8 (102/98 core) и значение diagnostic unexplainedCount внешнего ordinary fallback (0), отдельно от failed attempt (66). Универсальное одинаковое разбиение при deadline не достигнуто; полное причинное разложение прежнего wall residual также недоступно. Принят отрицательный standalone эксперимент с этими ограничениями, формальная code/tests приёмка не объявляется.

Development fixes до freeze и Python analysis fixes после замеров отдельно от review must-fix. Измеренный C# после preregistration не менялся.

## Проверки координатора

- MCP Release build spike: **PASS**; check-refusal, check-core-first, check-profile, check-merge: **PASS**.
- MCP Release build RoslynMcpServer.sln: **PASS**.
- Полный Release main `Category!=AnalyzerLifecycle`: **1362 passed / 1 failed / 0 skipped**, total/executed **1363**. В нём весь SourceStructure: **123 passed / 0 failed**. Exact expected inventories структурных ограничений не менялись.
- Failed: `RoslynMcpServer.Tests.WorkspaceLoadCache.DependencyPartialProofTests.Framework_selection_matches_installed_pack_files_without_loading_analyzers`. Expected только `framework-task-input-closure-unproven`; actual дополнительно `framework-selection-profile-unproven`. MCP SDK 10.0.401; тот же сбой уже записан в task-07/task-07.1, профиль теста рассчитан на 10.0.300. Тест не исправлялся, не исключался. Локальный SDK 10.0.301 корпуса Roslyn не заменяет SDK main CI.
- AnalyzerLifecycle не требуется: production load/prepare/publication, lifecycle/host и их test/build configuration не менялись.
- Python check_refusal_analysis.py, check_core_first_continuation.py и check_core_first_analysis.py: **PASS**, повторены координатором после последней Python правки. Development fix: synthetic attempted report без Phase1Ms вызывал KeyError; теперь недоступное время сохраняется null. Ошибочная команда координатора с несуществующим check_core_first_continuation_analysis.py отмечена как launcher error; правильная проверка выполнена.
- Все **18** новых binlog timing replay CLI: **PASS**, read errors 0; Roslyn ordinary profile replay ранее тоже PASS. SDK properties ordinary/phase1/groups проверяются по evidence; resolver duration отдельно недоступно, evaluation profiler не захвачен.

Build/run выполнены специализированными MCP tools. Полный main выполнен MCP execute_dotnet_command; Release/filter соответствуют .github/workflows/test-suite.yml, timeout 1200 s. TRX и полный MCP receipt находятся в ignored artifacts/parallel-load/task-07.2-evidence/.

```text
dotnet test <repo>/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter "Category!=AnalyzerLifecycle" --logger "trx;LogFileName=task-07.2-main.trx" --results-directory <repo>/artifacts/parallel-load/task-07.2-evidence/TestResults --verbosity minimal
```

## Целостность и ошибки запуска

Все **50 C#/csproj** совпали с measured-source SHA-256; comparator и SolutionObservation остаются byte-unchanged. Все **19** файлов прежнего evidence task-07/task-07.1 сохранились. Frozen h1/residual/c/b и pre-parallel forecast не перекалиброваны. Окончательная проверка после status updates подтвердила все 205 entries (143 artifacts, 50 runtime, 12 Python) без расхождений и 36 прежних tracked diff sections без изменений, кроме намеренного task-status в docs/README.md. Локальные ссылки task-07.2 проверены без отсутствующих целей; ignored final-integrity.json сохраняет точный inventory. [Coordinator validation/status hashes](validation-artifact-sha256.json), [машиночитаемый итог](validation.json).

Четыре первоначальных Roslyn baseline launch errors координатора произошли в Directory.CreateDirectory **до workspace/load/BuildHost**, из-за незакрытой кавычки output argument. Exit 1, no timeout. Они отдельно сохранены в launcher-errors-before-load.json; planned matrix затем выполнена корректными командами. Полные MCP receipts этих ошибок доступны в conversation, selected facts явно помечены reconstructed. У уже завершившегося PowerToys baseline warm0 после прерывания orchestration cell утрачен MCP receipt, но исходный валидный JSON сохранён; повтор/подмена не выполнялись. Эти incidents не скрываются и не считаются measurement rows.

Коммит не создан: обязательный main CI не зелёный. Отрицательный эксперимент не означает завершение E0, production readiness или активацию E1. [Отчёт](../task-07.2-refusal-fast-planner-roslyn.md), [машиночитаемая сводка](summary.json).
