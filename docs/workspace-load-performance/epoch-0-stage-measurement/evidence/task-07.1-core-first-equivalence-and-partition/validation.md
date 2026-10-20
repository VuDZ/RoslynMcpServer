# Task-07.1 — независимое ревью и финальная validation

Дата: **2026-10-10**, Europe/Moscow. Статус: **implemented; эксперимент завершён, no-go; независимое ревью round 2 ACCEPT; формальная приёмка code/tests pending единого зелёного main CI**.

Выполненный scope — standalone spike, допустимые входы загрузки, cluster/snapshot-load/recursive/isolated режимы, telemetry, шаг 0, матрица, модель и evidence. Всего 58 новых процессов: шесть проверок шага 0 и 52 попытки матрицы (44 основные, четыре isolated, четыре recursive; прогрев плюс n=3 каждой комбинации). Восемь matrix-попыток Files остановлены до загрузки с infeasible-search при ε=0,15; эти отказы сохранены и не объявляются эквивалентной загрузкой или ускорением. Production, Version, CHANGELOG, solution/CI не менялись в этой задаче.

## Независимые раунды

Один исполнитель — сабагент Codex (GPT-6, модель сессии унаследована); исправления принадлежат ему же. Два новых независимых рецензента Codex (GPT-6).

1. Round 1 — MUST-FIX: модель делила на ноль для непустых групп с нулевой суммарной profile cost. Один фикс-раунд: модель возвращает unavailable с причиной, сохраняет group evidence; добавлена регрессия нулевой стоимости. Guard-код также отклоняет nonfinite и отрицательные profile costs; отдельные mutation-регрессии этих двух входов не заявляются.
2. Round 2 — **ACCEPT**, 2026-10-10: проверены завершённый код и модель, отчёт, все 52 matrix attempts и шесть step0, conditional isolated/recursive, raw hashes и неизменённый comparator. Ревью подтвердило strict no-go из-за Files infeasible-search, maximum measured peak 2 971 971 584 bytes и sampler gap 325,6263 мс. Принятый независимым ревью scope — standalone реализация и evidence отрицательного эксперимента; формальная приёмка code/tests не объявляется.

До независимого review координатор обнаружил две проблемы: planning time терялось при отказе, а simultaneous failure мог скрывать plan-violation в status. Исполнитель исправил обе до матрицы; это development fixes, не отдельные замечания независимого review. Предупреждение CS9124 также устранено до замеров.

## Проверки координатора

- MCP Release build spike: passed. MCP check-core-first, check-merge, check-profile: passed.
- MCP Release build RoslynMcpServer.sln: passed.
- Полный Release main Category!=AnalyzerLifecycle: **1362 passed / 1 failed / 0 skipped**, executed/total **1363**. В нём весь SourceStructure: **123 passed / 0 failed**. Expected inventories identities/kinds структурных ограничений не менялись.
- Failed: RoslynMcpServer.Tests.WorkspaceLoadCache.DependencyPartialProofTests.Framework_selection_matches_installed_pack_files_without_loading_analyzers. Expected: только framework-task-input-closure-unproven; actual дополнительно framework-selection-profile-unproven. MCP dotnet SDK — 10.0.401, профиль теста рассчитан на 10.0.300. Тот же сбой зафиксирован до продолжения в task-07; тест не исправлялся и не исключался.
- AnalyzerLifecycle не запускался: production load/prepare/publication, analyzer lifecycle/host и их test/build configuration не менялись.
- Python check_core_first_continuation.py и check_core_first_analysis.py прошли повторно после уточнения модели. Проверены отказ нулевой стоимости, отказ отрицательного calibrated residual и раздельное сохранение first-progress proxy и additive residual.

Основной набор выполнен через MCP execute_dotnet_command для сохранения TRX в task-specific artifacts; Release/filter соответствуют .github/workflows/test-suite.yml. Build и spike runs выполнены специализированными MCP-инструментами.

```text
dotnet test <repo>/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter "Category!=AnalyzerLifecycle" --logger "trx;LogFileName=task-07.1-main.trx" --results-directory <repo>/artifacts/parallel-load/task-07.1-evidence/TestResults --verbosity minimal
```

Все 43 измеренных C#/csproj файла совпали с финальным runtime-кодом по SHA-256. compare.py и SolutionObservation.cs побайтно совпадают с pre-task snapshot. Все десять файлов evidence task-07 сохранили SHA-256. Tracked production/test и прочие существовавшие diff sections, кроме task-status в docs/README.md, совпали с pre-task snapshot.
Окончательная integrity-проверка после обновления статусов проверила 122 SHA entries без расхождений, десять прежних evidence files без изменений и все новые локальные ссылки отчёта/validation без отсутствующих целей; результат сохранён в ignored final-integrity.json.

Полные snapshots/logs/TRX/MCP records — в ignored artifacts/parallel-load/task-07.1-evidence/. [Validation artifact SHA-256](validation-artifact-sha256.json), [машиночитаемый итог](validation.json), [отчёт](../task-07.1-core-first-equivalence-and-partition.md). Статусы task, карты эпохи, темы и общего индекса обновлены согласованно. Коммит не создан: обязательный main CI не зелёный; чужие dirty/untracked changes сохранены. Отрицательный экспериментальный результат и независимое ревью не означают приёмку code/tests, завершение E0 или разрешение production/E1.

