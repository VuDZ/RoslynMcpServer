# Coverage

## Reviewed target

`docs/usage-statistics/README.md` целиком (черновик 2026-10-29, 174 строки): Q-01…Q-06, ключ workspace, текущие логи, JSONL и хранение, общие поля, события `start` / `client` / `load` / `reset` / `tool` / `mem` / `exit`, приватность, M1…M6, синтетика, таблица критериев, проверка harness, порядок работы, O-01…O-04.

Каталог результатов: `docs/usage-statistics/review-grok/`.

Сверка checkout: ветка `usage-statistics`, HEAD `f4a81d883d3d6b59fe25f8cda787cea4c406e522`.

## Context inspected

Документы этого checkout:

- `docs/README.md` — строка индекса темы;
- `PRIVACY.md`, корневой `README.md` (обязательство о телеметрии, платформы, установка Linux);
- `docs/ARCHITECTURE.md` — наблюдаемость, server GC, односессионная модель;
- `docs/workspace-load-performance/epoch-0-stage-measurement/evidence/task-12-server-gc-first-search.md` — расхождение кучи, committed и working set, отдельный GC BuildHost, непроверенный простой;
- `docs/workspace-load-performance/research-buildhost-fork/evidence/task-04/windows-roslyn-fork-ab-trace-summary.md` и `task-02-stage-3/office-fork-ab-summary.md` — пики клиента и BuildHost.

Код этого checkout:

- `Program.cs` — дневной Serilog, `retainedFileCountLimit: 14`, `shared: true`, каталог `AppContext.BaseDirectory/logs`; после `host.RunAsync` нет записи выхода;
- `Diagnostics/HarnessNameResolver.cs`, `HarnessSessionId.cs`, `ToolTelemetry.cs`, `McpInboundProtocolLogger.cs`;
- `Diagnostics/ParentProcessProbe.cs` и платформенные снимки родителя (полный путь образа);
- `Services/Workspace/SolutionManager.cs` — кэш загрузки, сброс workspace до нового открытия, `ClearWorkspaceAsync`, ленивая загрузка, `LogProcessWorkingSet`;
- `Services/Workspace/MsBuildWorkspaceProperties.cs`, `InputPathCanon.cs`, `Services/Build/DotNetConfigurationArguments.cs`;
- `Services/Workspace/UnregisteredBuildHostReaper.cs`, `Models/BuildHostProcessObservation.cs`;
- `Tools/WorkspaceTools.cs` — текст ошибки load при уже выполненном `LoadCoreAsync`;
- `RoslynMcpServer.csproj` — `ServerGarbageCollection`.

Ветка `research/workspace-host-process` (worktree, не этот checkout), только как контекст, на который ссылается спека:

- отсутствие `docs/workspace-host-process/evidence/2026-10-29-freeze.md` (`git show` / `git ls-tree`);
- `docs/workspace-host-process/README.md` — статус, мотивирующие 10–20 ГБ, task-05, O-05, O-02, L-01 (`~/.local/share/roslyn-mcp/launch.sh`, логи `~/.cache/roslyn-mcp/logs`);
- `docs/workspace-host-process/design.md` — статус, формула WorkspaceKey, канон пути, формулировка O-02;
- `docs/workspace-host-process/evidence/2026-10-29-task-01-reset-decision.md` целиком;
- `docs/workspace-host-process/epoch-0-design-and-spike/evidence/task-05-applicability.md` — фрагменты про private bytes, сумму working set и явное исключение BuildHost.

## Context not inspected

- `docs/usage-statistics/review-codex/` не читался. Поиск по репозиторию задевал пути внутри него; эти совпадения не открывались и в выводы не шли.
- Каталоги `review-*` внутри `docs/workspace-host-process` на ветке исследования не открывались.
- Полный `design.md` и полный отчёт task-05 не читались: взяты статус, ключ, канон пути, O-02 и абзацы про память.
- Остальной корпус `docs/workspace-load-performance` не читался.

## Source/code verification

Проверялись чтением текущего исходника и указанных замеров. Сервер не запускался, синтетические логи не считались, процессы BuildHost на машине не снимались.

## Coverage limitations

Пики BuildHost — спайки загрузки и поиска, не многочасовой простой production MCP. Task-05 прямо исключил BuildHost из своих сумм и этим подтвердил пробел; плато ребёнка в простое отдельным числом не приведено.

Канон WorkspaceKey и выбор O-02 прочитаны как записанные решения владельца на ветке, чей design как реализация не принят. Если после этих коммитов статус заморозки изменился и не попал в ветку, в ней этого всё равно не видно: названного файла нет.

Реализация `Process.PrivateMemorySize64` на Linux в исходниках установленного рантайма не сверялась; расхождение Linux-счётчиков с Windows private bytes взято из формулировки task-05.
