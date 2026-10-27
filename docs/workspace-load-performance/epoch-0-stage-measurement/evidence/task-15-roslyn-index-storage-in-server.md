# D/E0/task-15 — реализация и validation

Дата: **2026-10-23**. Сервер **1.5.14**, ветка `research/workspace-load-performance`.
Реализация — Claude Opus 5.5. Независимого ревью не было, офисный контроль pending.
Эффект измерен в [task-14](task-14-roslyn-index-storage.md) на spike; в сервере
отдельного замера времени не было.

## Проверки

| проверка | результат |
|---|---|
| `RoslynIndexStorageTests.Shipped_sqlite_is_the_build_Roslyn_references` | PASS: версия 2.3.5.0 и ключ `df579d6b11135638` совпадают со ссылкой Roslyn 5.9.0 |
| `RoslynIndexStorageTests.Indexes_are_stored_and_a_later_session_sees_changed_sources` | PASS — см. ниже |
| мутация: пакет SQLite убран из сервера | второй тест падает: база не создана |
| Release build решения | PASS, только прежние предупреждения CS8603, CS8601, xUnit1031 |
| main CI `Category!=AnalyzerLifecycle&Category!=RequiresWindows`, Release, Linux (SourceStructure входит) | 1388 passed / 0 failed / 0 skipped |
| кеш в `~/.local/share/Microsoft/VisualStudio/Roslyn/Cache` после полного CI | пусто: `RoslynIndexCache` удалил кеш процесса тестов |
| AnalyzerLifecycle, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`, Release, Linux | 13 passed / 0 failed / 0 skipped; каталог кеша после прогона пуст |
| `scripts/Test-MeasureWorkspaceLoad.ps1` | PASS |
| `Get-IndexCacheState` на синтетическом дереве | 2 папки `RoslynMcpServer*`, 2 МБ; `dotnet-*` и другие решения не считаются |

Что проверяет второй тест:
- через `SolutionManager` загружается `.slnx`;
- поиск `Alpha` находит 1 объявление, база `storage.ide` создана;
- затем `Alpha` переименовывается в `Beta` в файле;
- новая сессия находит 0 `Alpha` и 1 `Beta`.

## Не проверено

- Время первого поиска в сервере с тёплым кешем — офисный контроль на 1.5.14.
- Windows: место кеша и удаление кеша тестов при открытой базе. Удаление при выходе
  может не сработать, тогда кеш удалит следующий запуск тестов.
- Кеш процессов `RoslynMcpServer.LifecycleTestHost` тестами отдельно не удаляется.
  После AnalyzerLifecycle на Linux его не было.
