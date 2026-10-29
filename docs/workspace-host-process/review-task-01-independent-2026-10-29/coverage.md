# Покрытие ревью task-01, 2026-10-29

Отдельный проход. Каталог `docs/workspace-host-process/review-task-01-2026-10-29/` не читался до записи замечаний и не использовался как источник findings.

## Прочитано

- `AGENTS.md` (правила репозитория в сессии) и `docs/README.md`, раздел «Учёт задач, ревью и статистики».
- `docs/workspace-host-process/README.md` целиком.
- `docs/workspace-host-process/epoch-0-design-and-spike/README.md` целиком.
- `docs/workspace-host-process/epoch-0-design-and-spike/task-01-architecture-decision-hi.md` целиком.
- `docs/workspace-host-process/design.md` целиком.
- `docs/workspace-host-process/epoch-0-design-and-spike/evidence/task-01-architecture-decision.md` целиком.
- `docs/workspace-host-process/epoch-0-design-and-spike/evidence/task-01-data/validation.json`.
- Из task-00: «Главное», инвентарные строки S01–S15 и таблица этапов для `list_projects`, `get_project_graph`, `rename_project` / package, `get_method_body`, `search_code`.
- Из task-05: абзац замера 2606,4 MiB и раздел «Сравнение моделей: только условные границы» (формула `L/H/U/p`).
- Код, на который опираются проверяемые утверждения: `Program.cs`, `Hosting/RoslynMcpServiceCollectionExtensions.cs`, `Hosting/McpToolCatalog.cs` (`Build`), `Services/Workspace/SolutionManager.cs` (load defaults, `LoadAsync`, `ClearWorkspaceAsync`, свежий load), `Services/Build/SessionBuildState.cs`, `Services/Build/DotNetSdkEnvironment.cs`, `Tools/TestTools.cs` (`ExecuteDllTestsAsync`), `Tools/UtilityTools.cs` (`GetChangedFiles`, `list_projects`, `get_project_graph`, `get_method_body`), `Tools/ProjectTools.cs`, `Tools/BuildTools.cs` (разрешение пути build), `Services/GitChangedFilesHelper.cs` (`SuggestTestProjects`), `Services/Workspace/InputPathCanon.cs`, `Plugins/IRoslynMcpPlugin.cs`, `publish-and-verify.ps1`, `Services/DiagnosticReportStore.cs`, `Services/Navigation/NavigationOverflowStore.cs`, `Services/Navigation/SearchExceptionRecorder.cs`, `Services/Resolve/NuGetFallbackAssemblyResolver.cs`.
- `RoslynMcpServer.csproj`: `Version` 1.5.18.

## Поиск, которого нет в отчёте автора

Автор описал текстовый поиск `SetCurrentDirectory`, `SetEnvironmentVariable`, `AppContext.SetSwitch`, `Console.Set*`, `[ThreadStatic]`, `AsyncLocal`, `AssemblyResolve` в production C# без forks/spikes/tests/samples.

Дополнительно, по тем же деревьям (совпадения в `spikes/`, `forks/`, тестах и `RoslynMcpServer.LifecycleTestHost` отсечены):

| Поиск | Production-совпадения | Куда легло |
|---|---|---|
| `AssemblyLoadContext`, `AppDomain` (`FirstChance`, `AssemblyResolve`, `GetAssemblies`) | `Plugins/PluginAssemblyLoad.cs` (`PluginLoadContext`); `Services/Analyzers/InProcessAnalyzerAssemblyLoader.cs` (`AssemblyResolve`); `Services/Analyzers/AnalyzerExecutionGate.cs` и `Plugins/PluginDiscovery.cs` (обход загруженных сборок); `Services/Navigation/SearchExceptionRecorder.cs` (`FirstChanceException`) | Уже S08/S09/S16/S23 и §4/§8 design: plugins — отдельный gate, счётчики процесса подписаны как shared. Нового механизма нет. |
| `new Timer`, `PeriodicTimer`, `Task.Run`, `BackgroundService` | `SolutionManager` — `SearchPrewarmTask = Task.Run` (около строки 2232); `AnalyzerProvenanceCaptureService` — `Task.Run` replay | Prewarm уже назван в §10 (S41) как предел drain. Отдельного таймера жизненного цикла в production нет. |
| Статические `ConcurrentDictionary` / `Dictionary` / `List` / `HashSet` в `Services/` | `DiagnosticReportStore.ById`, `NavigationOverflowStore.ById`, `NuGetFallbackAssemblyResolver.NuGetDllCache`; прочие static-наборы — константные словари имён, не сессионное состояние | S27/S28 и S42. Design переносит cursor remainder в proxy. |
| `GCSettings`, `Environment.SetEnvironmentVariable`, `Directory.SetCurrentDirectory`, `Console.SetOut/Error/In` | В production-сервере: `Program.cs` `Directory.SetCurrentDirectory`. Запись env и `Console.SetOut` — тесты и lifecycle host | Cwd уже в descriptor (S04). |
| `LoadAsync(` / `LoadAndPrepareAsync(` в `Tools/` | Только `UtilityTools.list_projects` и `get_project_graph`. `WorkspaceTools.load_workspace` вызывает `LoadAndPrepareAsync`. `ProjectTools` вызывает `ClearWorkspaceAsync` | Это F-01 и F-02. В отчёте task-01 такого поиска нет. |

`[ThreadStatic]` и `AsyncLocal` в production нашлись только у `SearchExceptionRecorder` (реентерабельный флаг обработчика и текущий проект). Автор это место уже относит к search recorder.

## Запущено

- `git rev-parse HEAD` → `d54a93adc781439ea586a1eff5016c5a51aef947`.
- `git log --oneline b2a7b80..HEAD` → `d8c15f6` (проект design), `d54a93a` (отложение выбора владельца). `b2a7b80` является предком `HEAD`.
- `git status --short` — пусто.
- `git diff --check` по `design.md`, отчёту task-01 и файлу задачи — без вывода, код 0.
- Имена каталога пересчитаны по `McpToolCatalog.Build`: 54. Номера S01–S42 сверены со §10 design. Формула 1878,2 пересчитана из чисел task-05.

Сборки, тестов и SourceStructure не запускалось: объект ревью — документы, production этой задачей не менялся. Повторно гонять 267 ссылок из `validation.json` не стал: файл лежит в evidence, дерево с того коммита не расходилось с `HEAD` по коду, а новые findings получены чтением кода, а не обходом markdown-ссылок.

## Не читалось

- `review-task-01-2026-10-29/result.md` и `coverage.md` — чужой каталог до собственных замечаний.
- Сырые JSON task-03 и task-04, linux-прогоны, env-probe JSON. Утверждения task-04 F-01…F-05 и F-08 сверены с текстом design (switch вне gate, раздельные бюджеты, cooldown после отказа, один lock descriptor, явный retry, manifest), а не с повторным прогоном spike.
- Полные отчёты task-00 и task-05 вне указанных разделов.
- Повторный names-only probe `CODEX_*` и проверка macOS.
