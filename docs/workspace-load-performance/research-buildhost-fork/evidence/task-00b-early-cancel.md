# BuildHost/task-00b — ранняя отмена в сервере

Дата: **2026-10-24**. Пакет `Microsoft.CodeAnalysis.Workspaces.MSBuild` на nuget.org — **5.9.0**.
Коммита `2624f5c156` в нём нет. Шаг 1 задачи (прогон сценария task-00 на новом пакете) не выполнялся.

## Достижима ли ранняя отмена

Да. Токен отмены MCP-запроса доходит до `OpenSolutionAsync` / `OpenProjectAsync`.

`load_workspace` передаёт `CancellationToken` инструмента в `LoadAndPrepareAsync` → `LoadCoreAsync` →
`AnalyzerProvenanceCaptureService.OpenAndCaptureAsync` → `OpenWorkspaceAsync`. Там `.sln`/`.slnx`
открываются через `OpenSolutionAsync`, `.csproj` — через `OpenProjectAsync`. Отмена запроса уже
обрабатывается: `WorkspaceTools` пишет в лог `load_workspace cancelled by client` и возвращает
сообщение client abort, а не ошибку MSBuild.

Тот же `LoadAsync(..., cancellationToken)` вызывают `list_projects` и `get_project_graph`, когда им
передан `workspacePath`. Ленивая загрузка из `RoslynMcp.jsonc` (`EnsureWorkspaceFromConfigAsync`)
передаёт токен первого семантического вызова и пробрасывает `OperationCanceledException`.

Не отменяют уже идущий `OpenSolutionAsync`:

- Повторный `load_workspace` и смена configuration/platform/TFM ждут `_workspaceLock`. В коде прямо
  сказано, что ожидание не отменяет текущую загрузку. Новый `MSBuildWorkspace` создаётся только после
  возврата предыдущего вызова.
- `reset_workspace` тоже ждёт замок и затем вызывает `Dispose` у workspace. Это не отмена
  `OpenSolutionAsync`.
- Остановка процесса сервера отменяет запрос только если хост успевает отменить токен. Жёсткое
  завершение процесса до `catch` reap не выполняет. Дочерний процесс на Windows при этом не входит
  в job object и может пережить родительский.

Окно утечки — одно на загрузку. `BuildHostProcessManager` в 5.9.0 держит свой замок на время
`NoLock_GetBuildHostAsync`: процесс уже запущен, а `ConfigureGlobalStateAsync` и при необходимости
`FindBestMSBuildAsync` идут с токеном отмены до `_processes.Add`. Отмена в этом окне не попадает в
`catch` вокруг `ConnectAsync`, где процесс убивается, и не доходит до `Add`. `DisposeAsync` менеджера
в `finally` метода `LoadInfoAsync` завершает только то, что уже в `_processes`. Загрузки сервера
идут по одной (`_workspaceLock`), поэтому за одну отмену остаётся один такой процесс (следующий вид
host, net472 или повторный netcore из-за SDK, стартует после регистрации или dispose предыдущего).
Каждая отменённая попытка добавляет ещё один. Их не забирают `Dispose` workspace (он гасит только
фабрику логгера), `reset_workspace` и следующая успешная загрузка. Они живут, пока жив процесс сервера.

## Смягчение в сервере 1.5.16

После отмены в `LoadCoreAsync` сервер завершает дочерние BuildHost своего процесса, которые не числятся
используемыми. Чужой родитель не считается своим: цепочка предков не длиннее трёх, и родитель, стартовавший
позже ребёнка, — это повторно выданный pid, как у sampler task-00. Процесс `dotnet` или `mono` без
прочитанной командной строки не считается BuildHost и не завершается; в лог пишется предупреждение.
На macOS родитель и командная строка не читаются, поэтому там ничего не завершается.

К моменту `catch` менеджер пакета уже уничтожен внутри `OpenSolutionAsync`, и его таблица зарегистрированных
процессов снаружи недоступна. Зарегистрированный host к этому моменту уже получил `Shutdown`. Набор
«ещё используется» в боевом вызове пуст: живой потомок — это как раз незарегистрированный. Параметр
набора оставлен, чтобы не завершать host, чей id туда передан.

Форк BuildHost это не меняет: правка в клиенте пакета. Сценарий task-00 (`Invoke-BuildHostForkScenarios.ps1`)
по-прежнему идёт мимо сервера и до пакета с `2624f5c156` оставляет host.

## Проверка

- `UnregisteredBuildHostReaperTests`, Release, 2026-10-24: 13 passed, 0 failed. Отбор: свой незарегистрированный
  host завершается, занятый и чужой — нет, обычный `dotnet build` — нет, нечитаемая командная строка только
  считается, net472 узнаётся по имени процесса, цепочка через launcher проходит, повторно выданный pid
  родителя — нет, допуск 100 мс на время старта родителя — да, глубже трёх предков — нет.
- Тот же класс поднимает пакетный `BuildHost-netcore` 5.9.0 как дочерний процесс. Пока id в наборе занятых,
  процесс жив. Затем reap завершает его и не трогает соседний `ping`. Ограничение набора id в этом прогоне
  не даёт тесту завершить чужой BuildHost на машине.
- Main CI suite, Release, фильтр `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
  (как в `.github/workflows/test-suite.yml` на Windows): **1445 passed, 0 failed**. Сюда входят тесты
  `RoslynMcpServer.Tests/SourceStructure`. `AnalyzerLifecycle` не запускался: правка не касается
  shadow-copy и публикации анализаторов.
