# Ревью результатов task-00, 2026-10-28

- Объект: [task-00](../epoch-0-design-and-spike/task-00-process-state-inventory-med.md),
  [инвентаризация](../epoch-0-design-and-spike/evidence/task-00-state-inventory.md) и
  [актуализация](../epoch-0-design-and-spike/evidence/task-00-impact-2026-10-28.md)
  на `27ede33` (сервер 1.5.17).
- Рецензент: Claude Opus 5.5 (`claude-opus-5-5`), сессия Claude Code; навык
  [research-task-review](../../../.agents/skills/research-task-review/SKILL.md).
  Исполнитель task-00 — Codex; это первое независимое ревью.
- Раунд: 1. Исправления внесены координатором в той же сессии и перепроверены
  рецензентом (одна и та же сессия: рецензент и исправляющий совпадают).

## Вердикт

Результат принимается в scope инвентаризации. Он полный по проверенным
направлениям, ключевые утверждения подтверждаются кодом. Замечания касаются
формы и судьбы найденного попутно дефекта, а не фактов; все пять исправлены.

## Главное

- Чистый proxy невозможен без источника метаданных plugin tools и решения о
  синтаксическом Roslyn для `get_code_skeleton`/`get_method_body`.
- Demotion оставляет процесс тяжёлым; атомарного переключения load нет.
- Разделение процессного состояния на per-session / per-workspace / per-process —
  общая работа всех shared-моделей, её можно планировать до выбора модели.
- Найденная автором утечка sanitized snapshot шире, чем описано: она держит старый
  граф и во время свежего load. Исправлено в 1.5.18 с регрессионными тестами.

## Findings

| ID | Severity | Где | Что не так | Что сделано |
|---|---|---|---|---|
| F-01 | minor | инвентаризация, начало | 80 КБ таблиц без сводки: блокеры и находки разбросаны по ячейкам и абзацам | Добавлен раздел «Главное» (8 пунктов) |
| F-02 | major | инвентаризация, «Что нужно чистому proxy»; `Services/Workspace/SolutionManager.cs` | Утечка sanitized snapshot помечена «вне docs-only» без отслеживания. Проверка показала больший масштаб: кеш держит старый `Solution` и после `reset_workspace`, и всё время свежего load другого решения | Фикс `ForgetSanitizedPublishedSolution` в `ClearWorkspaceAsync` и свежем пути `LoadCoreAsync`; тесты `SolutionManagerSanitizedSnapshotTests` (red без фикса, green с ним); 1.5.18, CHANGELOG |
| F-03 | minor | README темы, task-05 | Наблюдение ~18 МБ не помечено как возможно сделанное до Server GC (1.5.12, S38); task-05 не мерит режимы GC | README помечает наблюдение; в task-05 добавлен п. 4 «Режим GC» и версия сервера в разборе ~20 MCP |
| F-04 | suggestion | инвентаризация | Не выделена работа, общая для всех моделей (разделение state) | «Главное», п. 5: списки S-строк по трём уровням |
| F-05 | suggestion | таблица инструментов | Требование «proxy не грузит ничего» сталкивается с Roslyn-парсером у disk-only инструментов; вопрос не вынесен | «Главное», п. 1: вынесено в task-01 |

Исправлено и перепроверено: 5. Открытых замечаний нет.

## Проверенные утверждения

- **Подтверждено** S13: `LoadCoreAsync` освобождает workspace до нового открытия
  (`SolutionManager.cs`, свежий путь после `StopDiskWatcherUnderLock`).
- **Подтверждено** S27: `NavigationOverflowStore` — static class со static
  `Gate`/`Order`/`ById`, без владельца.
- **Подтверждено** S23 и каталог: метаданные tools хранятся как `MethodInfo`
  (`PluginStartup.cs:277`, `McpToolCatalog.cs:400`).
- **Подтверждено** таблица инструментов: `get_code_skeleton` парсит через
  `CSharpSyntaxTree.ParseText` (`CodeSkeletonTools.cs:90`).
- **Подтверждено и расширено** утечка sanitized snapshot (F-02): тест без фикса
  падает в обоих сценариях.
- **Подтверждено** S38: `ServerGarbageCollection=true` в `RoslynMcpServer.csproj`.

## Рекомендации

- Следующим документом сделать производную таблицу «per-session / per-workspace /
  per-process» по «Главное», п. 5: она не зависит от выбора модели.
- В task-01 явно решить источник метаданных tools для proxy и допустимость
  синтаксического Roslyn в нём.

Покрытие и ограничения — в [coverage.md](coverage.md).
