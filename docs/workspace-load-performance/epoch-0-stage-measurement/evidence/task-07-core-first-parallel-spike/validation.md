# Task-07 — независимое ревью и финальная validation

Дата: **2026-10-10**. Статус: **implemented; эксперимент завершён, no-go; формальная приёмка code/tests pending единого зелёного main CI**.

Выполненный scope — standalone core-first spike, шаг 0, 40 свежих процессов матрицы, компаратор task-04 без изменений, модель/прогноз, evidence и статусы. Production, Version, CHANGELOG, solution/CI/publish не менялись в этой задаче. Принятый scope code/tests не объявлен: обязательный main CI имеет прежний SDK-specific сбой. Ревью реализации и отрицательного результата завершено ACCEPT; это не приёмка production и не разрешение E1.

## Независимые раунды

Исполнитель — один сабагент Codex (GPT-6, модель сессии унаследована), все доработки выполнены им же. Рецензенты — два новых независимых сабагента Codex с унаследованной моделью сессии.

1. Round 1 — MUST-FIX: повторный StopAsync после ошибки observation терял failure JSON; ошибка группы теряла соседние group outcomes; analysis gate допускал разрыв sampler >1000 мс. Все три замечания исправлены одним фикс-раундом. Добавлены cached SamplerFinalization, FailureEvidenceCapture и durable group state; проверки сериализуют исходный отказ, отдельный отказ sampler и сохраняют partial projects/progress/timing при success/failure/timeout. Анализ проверяет границу 1000 мс, превышение, nonfinite telemetry и timeout. Дополнительно соблюдён порядок доступности изменённого Redact.
2. Round 2 — **ACCEPT**: проверены исходники/дельта, шаг 0, все 40 raw JSON и MCP records, failure regressions, gate, модель с ограничениями и hashes. compare.py и SolutionObservation.cs побайтно совпадают с исходным состоянием. 28 процессов получили ok, 12 — plan-violation (с учётом прогрева); measured snapshot-прогоны PowerToys не объявлены корректным ускорением. Все sampling gaps <1000 мс, sampler errors отсутствуют. No-go подтверждён независимо.

## Проверки координатора на финальном коде

- MCP Release build `spikes/ParallelLoad/ParallelLoad.csproj`: passed, warnings 0.
- MCP `check-core-first`, `check-merge`, `check-profile`: passed. Python `check_core_first_analysis.py`: passed.
- MCP контрольный запуск финального runner, Files snapshot N=2: ok, 0 plan violations. Projects и Diagnostics побайтно как JSON-значения совпадают с соответствующим измеренным прогоном; относительно baseline сохраняются прежние 5 diagnostics различий. Контроль не добавлен в медианы.
- MCP Release build `RoslynMcpServer.sln`: passed. Четыре прежних предупреждения: CS8603 ×2 NuGetFallbackAssemblyResolver, CS8601 HostSession, xUnit1031 WpfTemporaryProjectCallbackTests.
- Полный Release main `Category!=AnalyzerLifecycle`: **1362 passed / 1 failed / 0 skipped**, всего и executed **1363**. Error/timeout/aborted/notExecuted в TRX — 0. В этом полном запуске весь SourceStructure: **123 passed / 0 failed**; identities/kinds структурных ограничений и expected inventory не менялись.
- Единственный failed: `RoslynMcpServer.Tests.WorkspaceLoadCache.DependencyPartialProofTests.Framework_selection_matches_installed_pack_files_without_loading_analyzers`. Ожидалось только `framework-task-input-closure-unproven`; actual дополнительно содержит `framework-selection-profile-unproven`. SDK процесса — 10.0.401, тест требует профиль 10.0.300. Тот же сбой был воспроизведён предварительным полным запуском до изменений spike и записан task-04/task-05.1. Тест не исправлялся и не исключался из final main.
- AnalyzerLifecycle не запускался: production load/prepare/publication, analyzer lifecycle, lifecycle host и его test/build конфигурация этой задачей не меняются.

Полный main выполнен через MCP execute_dotnet_command, чтобы сохранить TRX в заданный task-07 artifacts-каталог; фильтр и Release соответствуют `.github/workflows/test-suite.yml`. Build и standalone runs выполнены специализированными MCP-инструментами.

```text
dotnet test <repo>/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter "Category!=AnalyzerLifecycle" --logger "trx;LogFileName=task-07-main.trx" --results-directory <repo>/artifacts/parallel-load/task-07-evidence/TestResults --verbosity minimal
```

Матрица снята на отдельно зафиксированных [измеренных исходниках](measured-source-sha256.json), до исправления failure handling. [Финальные исходники](source-sha256.json) и [дельта C# хэшей](post-matrix-source-delta.json) отделены явно; повтор всей матрицы на финальном runner не заявляется. Границы успешного load/merge/wall, planner и merger сохранены; контрольный финальный Files load совпал по observation. Это достаточное evidence отрицательного результата, а не validated production performance claim.

Тяжёлые logs/TRX/snapshots находятся в `artifacts/parallel-load/task-07-evidence/`: `final-solution-build.log`, `final-main-validation.log`, `initial-main-validation.log`, `post-fix-files-smoke.json`, `mcp-run-results.json`, `TestResults/task-07-main.trx`. [Хэши validation-артефактов](validation-artifact-sha256.json).

Статусы task, карты эпохи и темы обновлены согласованно. Коммит не создан: единый обязательный main CI не зелёный; чужие dirty/untracked changes сохранены. Формальная приёмка требует устранения SDK-specific блокера в соответствующей работе и повторной полной validation.
