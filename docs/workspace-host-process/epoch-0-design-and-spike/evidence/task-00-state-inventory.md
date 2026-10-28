# C/E0/task-00 — Инвентаризация состояния процесса

Дата: **2026-10-26**. Статус: **completed, docs-only**.
Актуализировано **2026-10-28** после обновления до main `4e5e9f9`, сервер 1.5.17:
[влияние изменений, S34–S42 и новая validation](task-00-impact-2026-10-28.md).
Ниже сохранена исходная инвентаризация S01–S33 и проверки базы 2026-10-26;
для текущего кода она читается вместе с дополнением, чьи поправки имеют приоритет
для BuildHost, config/env, фоновых работ, persistent indexes и диагностики.
Исполнитель: Codex; точный ID модели недоступен. Независимое ревью не проводилось.
Принятый scope: инвентаризация текущего кода, классификация состояния и ограничений
для U-C-01…U-C-08. Ключ совместимости, IPC, маршрутизация, модель процессов и
production activation не выбирались. Проверки и ограничения — в конце отчёта.

## Главное

Добавлено по [ревью результатов task-00, 2026-10-28](../../review-task-00-2026-10-28/result.md)
(F-01); сводит выводы обоих отчётов, не заменяя таблицы.

1. **Чистого proxy в нынешнем коде нет.** `Program.cs` безусловно регистрирует
   MSBuild, загружает Roslyn и plugins (S01). Для первого `tools/list` фасаду
   нужны метаданные plugin tools, а сейчас они есть только как `MethodInfo`
   загруженных сборок (S23); `get_code_skeleton` и `get_method_body` без
   workspace всё равно требуют Roslyn-парсер. Источник метаданных и допустимость
   синтаксического Roslyn в proxy решает task-01.
2. **Demotion не делает процесс лёгким.** Locator/MEF, analyzer- и
   plugin-сборки, resolver, DI singletons, heap Server GC и static stores остаются
   (S08, S09, S16, S23, S38). Это довод в сравнении моделей task-01, а не деталь.
3. **Атомарного переключения нет.** `LoadCoreAsync` освобождает старый граф до
   открытия нового (S13): требование scope «при отказе сохранить старую привязку»
   нынешний load не выполняет даже в одном процессе.
4. **Process-wide хранилища без владельца.** Курсоры `NavigationOverflowStore` и
   `DiagnosticReportStore` (S27, S28) не привязаны к клиенту и поколению: в любой
   общей модели клиенты вытесняют и могут читать чужие payload.
5. **Работа, общая для всех shared-моделей**, — разделение процессного состояния:
   per-session — S02 (surface/activation), S04, S06, S13, S14, S21, S25, S27, S28,
   S30; per-workspace — S10–S12, S15, S17, S18, S36; свойство процесса — S08, S09,
   S16, S23, S38. От выбора модели в task-01 не зависит.
6. **Утечка sanitized snapshot исправлена в 1.5.18.** `ClearWorkspaceAsync` и
   свежий load не сбрасывали кеш sanitized snapshot: после `reset_workspace` старый
   `Solution` оставался в памяти, а при перезагрузке держался рядом с новым
   графом до первого поиска. Регрессионные тесты —
   `SolutionManagerSanitizedSnapshotTests`; см. [CHANGELOG](../../../../CHANGELOG.md).
7. **Число ~18 МБ устарело.** С 1.5.12 включён Server GC (S38); память пустого
   процесса и разбор ~20 MCP измеряет task-05 в обоих режимах GC.
8. **Неизвестные N01–N08** остаются с адресатами; самые важные для выбора
   модели — N04 (поведение harness, task-05) и N02 (SDK seam фасада, task-03).

## Основание и способ проверки

- [Задание](../task-00-process-state-inventory-med.md), [карта эпохи](../README.md),
  [scope и U-C-01…U-C-08](../../README.md), [архитектура](../../../ARCHITECTURE.md).
- Исходный checkout: `bdbb812527a19e3e95dc83b45704e6e62e1f853d`, рабочее дерево
  перед началом чистое. Инвентаризация относится к этому коду, а не к более новому
  опубликованному серверу: в [csproj](../../../../RoslynMcpServer.csproj) версия
  `1.5.7`, MCP SDK `2.2.0`, Roslyn `5.9.0`.
- `get_mcp_server_info`: исследующий MCP — `1.5.16.0`, опубликован из соседнего
  checkout; cwd — этот репозиторий. `load_workspace` открыл именно
  `RoslynMcpServer.sln` этого checkout, 5 проектов. Restore assets — `0/5`,
  incomplete; это ограничение семантического разрешения внешних зависимостей,
  а не успешная сборка. Версию бинаря не использовали как доказательство поведения
  исследуемого кода; например, его вывод про search-prewarm не переносится сюда.
- Поиск выполнялся MCP `search_code` (managed walk, без shell grep), чтение —
  `get_file_content`, `read_file_range`, `get_class_skeleton`. Семантические
  `find_usages(SessionBuildState)` и
  `find_symbol_references(RoslynMcpServer.Diagnostics.MsBuildBootstrapper.Register)`
  подтвердили production-потребителей; отсутствие совпадений по неполному FQN
  исправлено запросом полного имени. Для полноты использовался также текстовый
  поиск, не зависящий от restore.
- Итоговый поиск static полей/свойств: 242 совпадения в 492 C#-файлах, без
  достижения cap 1000. Отдельно проверены environment/Console, поля tool hosts,
  DI, фоновые операции, watchers, plugins и build proofs. Это число текстовых
  совпадений, включая тесты и static lambdas, а не число изменяемых переменных.
  Предварительные capped-запросы уточнялись отдельными поисками.
- Production-контур определяется исключениями `Compile Remove` в csproj:
  tests, lifecycle test host, samples и spikes не исполняются в основном сервере.
  Их тесты здесь служат ссылками на уже существующие контракты; они не запускались.

## Значение классификаций

**Общий** — допустимо разделить только между совместимыми клиентами одного
workspace: опубликованный снимок, наблюдение диска, одинаковая analyzer policy.
Запись файлов и reset имеют видимый всем эффект. Это не изолированные ветви
`Solution`. **Сессия** — выборы и корреляция конкретного MCP-клиента, которые
нельзя заменить значениями другого клиента. **Идентичность host** — ограничения
запущенного процесса/загруженного кода, значимые для его пригодности. Это список
входов для task-01, не готовый состав ключа. **Конфликт** — текущая семантика
непосредственно не переносится на shared host без решения design.

В колонке «смена / падение» смена означает смену привязки живого фасада либо
плановую передачу роли; падение — смерть процесса host. Если host совмещён с
фасадом, его собственная MCP-сессия также погибает; сохраняются сессии только
выживших MCP. Новый host не получает RAM старого процесса. Имена `LoadSessionId`
и input generation в коде не означают идентификатор MCP-сессии или будущее
поколение IPC-host.

## Инвентарь

### Старт, DI и контекст процесса

| ID / элемент → место в коде | Текущая семантика | Класс и допустимый общий эффект | Смена / падение | Последствие |
|---|---|---|---|---|
| S01 — порядок старта, [Program.cs](../../../../Program.cs): `MsBuildBootstrapper.Register`, force-load, `PluginStartup.Run` | Register до Roslyn; затем CSharp.Workspaces/CSharp, Features и CSharp.Features; смена cwd, logger/config, регистрация MCP, plugins, Build/Run host. Выбор роли отсутствует | **Конфликт / идентичность host**: тяжёлые действия безусловны | Promotion потребует тяжёлой инициализации; demotion не отменяет уже выполненную. Crash теряет provider и RAM | U-C-04/06/07/08: нынешний entry point ещё не чистый proxy |
| S02 — `AddRoslynMcpCoreServices` / `RegisterCore`, [DI](../../../../Hosting/RoslynMcpServiceCollectionExtensions.cs) | Singletons: `IOptions<McpToolProfileOptions>`, surface, одна runtime collection под двумя service types, activation, `TimeProvider.System`, provenance service, file settings (TryAdd), `SolutionManager`, каждый `McpToolCatalog.HostTypes`; options provenance привязаны к configuration | **Смешение общего, сессии и идентичности**. Workspace/provenance можно делить при совместимости; surface/activation — выбор клиента. Один контейнер сейчас обслуживает один stdio | Provider нельзя считать переносимым session context; на новом host строится заново | U-C-02/05/08: повторно использовать core DI целиком без разделения состояния нельзя |
| S03 — tool host singletons, [каталог](../../../../Hosting/McpToolCatalog.cs), [фабрика](../../../../Hosting/McpToolDescriptor.cs): `CreateFactory` | Зарегистрированы как singleton, но instance tool SDK создаёт через `ActivatorUtilities.CreateInstance(request.Services ?? services, hostType)` на вызов. Встроенные hosts в основном держат manager/logger, не собственные сессии | **Общий** provider зависимостей, **сессия** request services. Само singleton-объявление не гарантирует один экземпляр tool host | In-flight экземпляры и их контекст не переносятся; crash теряет их | U-C-02/08: классифицировать зависимости, а не предполагать lifetime по регистрации |
| S04 — cwd / `ROSLYN_MCP_WORKSPACE`, [Program](../../../../Program.cs): `ApplyOptionalWorkspaceRootFromEnvironment`, [bootstrap](../../../../Diagnostics/MsBuildBootstrapper.cs): `ResolveStartupSearchDirectory` | На старте optional env меняет cwd только для существующего каталога; bootstrap читает env ещё до этой смены. `load_workspace` cwd не меняет. Относительные пути в разных tools разрешаются по cwd либо workspace root | **Сессия / идентичность host / конфликт**: cwd — стартовый процессный контекст, нельзя мутировать для другого клиента | Фасад сохраняет собственный стартовый контекст; новый host не наследует cwd первого попавшегося процесса. На диске контекст не хранится | U-C-01/02/07/08: совпадение workspace path само по себе недостаточно |
| S05 — `Environment`, SDK и CLI, [DotNetSdkEnvironment](../../../../Services/Build/DotNetSdkEnvironment.cs), [DotNetHostResolver](../../../../Services/Build/DotNetHostResolver.cs), [GlobalJsonSdkReader](../../../../Services/Build/GlobalJsonSdkReader.cs), [RipgrepRunner](../../../../Services/RipgrepRunner.cs) | `DOTNET_ROOT`, PATH, архитектура, SDK/MSBuild override variables; subprocess наследует окружение и права пользователя ОС, runner удаляет мешающие SDK overrides и применяет pin `global.json`. PATH ищет rg только при opt-in. [NuGet resolver](../../../../Services/Resolve/NuGetFallbackAssemblyResolver.cs) читает `NUGET_PACKAGES` или user profile | **Идентичность host** для design-time evaluation и OS user; **сессия/вызов** для CLI/search. Одинаковые inputs допускают одинаковую семантику; произвольный env другого клиента — нет | При переключении не заменять контекст CLI окружением чужого host. Crash теряет процессные значения; env наследуется при новом старте, диск остаётся | U-C-01/02/08: учесть также env, влияющий на MSBuild imports/conditions, и feeds/credentials через окружение; точный перечень — task-01 |
| S06 — `RoslynMcp.jsonc`, [RoslynMcpFileSettings](../../../../Config/RoslynMcpFileSettings.cs): `LoadFromDefaultLocations`, `Merge`, `ResolveWorkspacePathAgainstWorkingDirectory` | Читается один раз после env-cwd: exe, затем cwd; cwd overrides по ключам, массив plugins заменяется целиком. Relative workspace/plugins paths — от cwd. Parse failure и unknown keys диагностируются. Lazy load через [SolutionManager.EnsureWorkspaceFromConfigAsync](../../../../Services/Workspace/SolutionManager.cs) | **Сессия**: workspace/configuration/platform/TFM defaults, max-results/preview/ripgrep-path; **идентичность host**: эффективные inputs графа/plugins. Не всё содержимое файла является host identity | Фасад сохраняет свои merged defaults и источник загрузки; переключение не должно читать настройки найденного host как свои. Crash не стирает файл, но перечитывание может дать другую версию | U-C-01/02/03: отсутствие аргумента не разрешает чужие defaults |
| S07 — generic Host configuration и provenance options, [Program](../../../../Program.cs): `Host.CreateApplicationBuilder(args)`, [DI](../../../../Hosting/RoslynMcpServiceCollectionExtensions.cs), [AnalyzerProvenanceCaptureOptions](../../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs) | Помимо jsonc есть configuration стандартного Host. `AnalyzerProvenanceCapture` options: CleanupAttempts=5, CleanupRetryDelay=50ms; singleton capture service хранит options, clock и counters | **Идентичность host / общий** служебный capture контекст; retry cleanup не клиентский build proof | Новый provider заново получает options/counters. Старый экземпляр остаётся при demotion, пока жив контейнер | U-C-01/02/04: jsonc не исчерпывает конфигурацию процесса |
| S08 — MSBuild Locator, [MsBuildBootstrapper](../../../../Diagnostics/MsBuildBootstrapper.cs), [MsBuildEnvironmentInfo](../../../../Diagnostics/MsBuildEnvironmentInfo.cs) | Однократная регистрация SDK/VS с учётом bitness и startup global.json. Static mutable properties: QueriedInstanceLines, RegisteredInstanceName/Version/MsBuildPath/VisualStudioRootPath, ProcessDescription, RegistrationSummary | **Идентичность host**; диагностические данные **общие** для этого процесса | Смена workspace/reset не перерегистрирует уже загруженный toolset. После demotion регистрация остаётся; crash снимает её вместе с процессом | U-C-01/02/04/07: пригодность promoted процесса зависит от истории и startup toolset |
| S09 — MEF / Roslyn / BuildHost, [SolutionManager](../../../../Services/Workspace/SolutionManager.cs): `MsBuildHostServices`, `LoadMefAssemblies`, `LoadCoreAsync`; [csproj](../../../../RoslynMcpServer.csproj) | Static MEF host services, загруженные CLR assemblies; MSBuildWorkspace использует SDK Roslyn BuildHost, binlog capture открывает workspace. Публикация не single-file | **Идентичность host / общий**: одинаковый workspace может использовать один graph/MEF; BuildHost не MCP-host | Dispose workspace закрывает его ресурсы, но не доказывает выгрузку CLR/MEF или всех дочерних процессов после crash. Новый host делает ordinary load | U-C-04/06/07: SDK BuildHost остаётся неизменным; MCP-host не следует путать с ним |

### Workspace, load, analyzer и build context

| ID / элемент → место в коде | Текущая семантика | Класс и допустимый общий эффект | Смена / падение | Последствие |
|---|---|---|---|---|
| S10 — граф и snapshots, [SolutionManager](../../../../Services/Workspace/SolutionManager.cs): `_workspace`, `_solution`, `GetPublishedSolution*`, `GetOrCreateSanitizedPublishedSolution` | Один active workspace, опубликованный и sanitized snapshots; compilation/semantic caches принадлежат Roslyn. Читатели не получают raw workspace вместо опубликованного. Нет изолированной ветви для клиента | **Общий**: клиенты видят согласованные saved-disk изменения и одну admission policy | После смены привязки нельзя использовать старые ProjectId/DocumentId/snapshot как данные нового host. Crash теряет весь прогрев; disk edits остаются | U-C-03/06/08: общая RAM, общие CPU/кеши; handoff не мигрирует прогрев |
| S11 — сериализация, write-base и публикация, [SolutionManager](../../../../Services/Workspace/SolutionManager.cs): `_workspaceLock`, `_operationContexts`, `_lastPublishedWriteContext`, `_rawWorkspaceRevision`, `ApplySolutionChangesToDiskAsync`; [WorkspaceWriteResult](../../../../Services/Workspace/WorkspaceWriteResult.cs) | Load/prepare, flush и write boundary сериализованы; контекст привязан к load session, mapping, raw revision, base snapshot. Preflight до записи, inverse overlay, persist/apply/reconcile. LastWriteResult описывает возможную частичную запись | **Общий** admission/freshness, **вызов** base/result. Lock не сериализует весь срок жизни любых semantic/CLI запросов | Старые operations не получают право писать в новое поколение. Crash может произойти после части disk writes, но до ответа; journal/replay здесь отсутствует | U-C-03/05/08: согласовать in-flight при release; записи не повторяются автоматически, disk side effects общие |
| S12 — watcher/input state, [SolutionManager](../../../../Services/Workspace/SolutionManager.cs): `StartDiskWatcherUnderLock`, `StopDiskWatcherUnderLock`, `PullInputSnapshot`; [WorkspaceInputSession](../../../../Services/Models/WorkspaceInputSession.cs), [WorkspaceDiskWatcherStarter](../../../../Services/Workspace/WorkspaceDiskWatcherStarter.cs) | FileSystemWatchers, subscriptions, dirty C#/non-C# paths, cached texts, missing paths, refresh-all, graph-stale flags; input generation, membership/input revisions, coverage, pending events/own-write tracking. Callbacks привязаны к session; flush при обращении | **Общий** для одного графа: external saves и server writes меняют свежесть для всех. Это OS file subscriptions, не MCP subscriptions | Stop закрывает watcher session и disposes watchers; при новом load создаются заново. Crash теряет очереди/ревизии/coverage, а не файлы. Новый generation не продолжает старые cursors | U-C-01/03/06/08: совместимость не заменяет freshness/admission; write одного клиента виден остальным |
| S13 — load defaults и cache matcher, [SolutionManager.LoadAndPrepareAsync / LoadCoreAsync](../../../../Services/Workspace/SolutionManager.cs), [MsBuildWorkspaceProperties](../../../../Services/Workspace/MsBuildWorkspaceProperties.cs) | Эффективные Configuration/Platform/TFM: passed → loaded → file. Matcher сравнивает только переданные свойства и path, graph-stale запрещает hit. `Any CPU` нормализуется в `AnyCPU`, raw spelling хранится для CLI. Переход на другой path тоже вычисляет omitted defaults из прежних Loaded* до очистки | **Сессия**: намерение/история defaults; **идентичность host**: эффективные graph inputs; **конфликт** при наследовании от другого клиента | Фасад сохраняет выбранный path и эффективные значения/семантику omission. Существующий LoadCoreAsync сначала освобождает старый graph: атомарный proxy-switch с сохранением старой привязки пока не реализован | U-C-01/02/03: не приравнивать omitted к SDK default или текущему host; повторный load восстанавливается из контекста клиента |
| S14 — `LoadedBuildArgs`, [SolutionManager](../../../../Services/Workspace/SolutionManager.cs): `ApplySessionBuildArgs`, [DotNetBuildArguments](../../../../Services/Build/DotNetBuildArguments.cs), [BuildTools](../../../../Tools/BuildTools.cs), [TestTools](../../../../Tools/TestTools.cs) | Build suffix — CLI-only, не MSBuildWorkspace property и не cache-key графа. Непустой аргумент load обновляет suffix даже при cache hit; null/whitespace сохраняет прежний. Configuration/Platform наследуются build/test; load TargetFramework не добавляется автоматически в обычный CLI build | **Сессия**, **конфликт** process-wide LoadedBuildArgs. Различие CLI suffix не доказывает несовместимость Roslyn графа, но меняет build context и disk outputs | При смене host нужны args этого клиента; cache hit чужого клиента не должен менять его suffix. Crash теряет RAM args; фасад выжившего клиента должен их помнить | U-C-01/02/08: task-01 решает, что разделять, что включать в совместимость; silent inheritance недопустим |
| S15 — sticky analyzer policy, [SolutionManager](../../../../Services/Workspace/SolutionManager.cs): `ShadowCopyAnalyzersEnabled`, `AnalyzerShadowMapping`, `AnalyzerProvenanceSnapshot`, `LoadSessionId`, `_publicationState`; [AnalyzerExecutionGate](../../../../Services/Analyzers/AnalyzerExecutionGate.cs) | Opt-in true готовит mapping; cached false/omitted не выключает и не refresh-ит его. Reset/reopen снимает session mapping. Provenance отсутствует/Failed/Incomplete/foreign → Unavailable; cached true не чинит. Banned/NoOverlay/AllowedMapping и excluded refs переживают edit/flush/reconcile | **Общий** только с совместимой execution policy; **сессия** выбора opt-in; **идентичность host / конфликт** если уже исполнялся иной analyzer. Fail-closed не приватен одному клиенту | На новом host нужна новая capture/prepare/admission. Нельзя переносить mapping GUID или считать старую admission подтверждением нового graph. Падение теряет provenance/mapping, но не опубликованные shadow файлы | U-C-01/02/03/04: host с активным overlay не реализует молча запрос клиента на иной режим |
| S16 — analyzer CLR history, [InProcessAnalyzerAssemblyLoader](../../../../Services/Analyzers/InProcessAnalyzerAssemblyLoader.cs), [AnalyzerExecutionGate](../../../../Services/Analyzers/AnalyzerExecutionGate.cs), [AnalyzerLoaderContract](../../../../Services/Analyzers/AnalyzerLoaderContract.cs) | Manager-owned process-lifetime loader: loadedByPath/Identity, dependency-location/resolve queues, AssemblyResolve handler; `Assembly.LoadFrom`. Gate проверяет также assemblies всего процесса. Same-identity новые bytes требуют restart; private helper resolution не поддержан | **Идентичность host**. Изменение bytes уже занятой identity — несовместимость с этим живым процессом, даже после reset | Demotion оставляет assemblies и resolver. Новый proxy restart не выгружает analyzer в прежнем host. Нужен restart процесса, где analyzer загружен; crash host снимает CLR history, но теряет workspace | U-C-01/02/04/06/07: «workspace освобождён» не значит «процесс пригоден для любой новой analyzer identity» |
| S17 — build proofs, [SessionBuildState](../../../../Services/Build/SessionBuildState.cs), [BuildContext](../../../../Services/Build/Models/BuildContext.cs), [TestDllEnsure](../../../../Services/Build/TestDllEnsure.cs) | Один state в manager: proofs, building slots, контекстные admission/waiters, input cursors, generation/membership, dirty marks и contested outputs. BuildContext содержит config/platform/TFM/buildArgs/outputPath. Observe нового generation/membership принимает новый baseline и сбрасывает неподтверждённое. У pull нет input content hashes: auto не skip, never не получает current proof | **Сессия / поколение общего workspace / конфликт** при обмене proof между клиентами. Совпавший путь DLL или успешный чужой CLI не равен proof | State не пересылается как свежий при handoff/crash. Даже CLI, переживший host, не доказывает свежесть нового generation. Reset не заменяет объект State; последующий Observe переоценивает его | U-C-02/03/08: нужны проверяемые generation/context/output/input boundaries независимо от места запуска CLI |
| S18 — lazy load и диагностика, [SolutionManager](../../../../Services/Workspace/SolutionManager.cs): `EnsureWorkspaceFromConfigAsync`, `WorkspaceLoadSource`, `LastDiagnostics`, `LastLazyLoadFailureReport`, `WorkspaceLoadInProgress` | Lazy load не отменяет уже выполняющийся load; ожидает boundary. Текущая попытка использует токен своего вызова, отдельного multi-client single-flight/admission нет. Last* относятся ко всему manager | **Общий** результат загрузки; **сессия** источник/запрос и его cancellation | Выживший фасад сохраняет запрос/defaults, но не выдаёт Last* другого host за свой load. Crash теряет in-flight и failure RAM | U-C-02/03/05: отмена одного будущего waiter не должна отменять общую загрузку остальных; текущая блокировка сама этого не доказывает |

### MCP, plugins, наблюдаемость и внешнее состояние

| ID / элемент → место в коде | Текущая семантика | Класс и допустимый общий эффект | Смена / падение | Последствие |
|---|---|---|---|---|
| S19 — протокол / IDs / request cancellation, [AddRoslynMcpServerTools](../../../../Hosting/RoslynMcpServiceCollectionExtensions.cs), [CreateFactory](../../../../Hosting/McpToolDescriptor.cs), [McpInboundProtocolLogger](../../../../Diagnostics/McpInboundProtocolLogger.cs) | Один stdio SDK endpoint; initialize/negotiation и outstanding requests принадлежат SDK-соединению, tool получает request services/token. Logger видит JSON-RPC IDs, но не делает глобальные IDs и не хранит recovery journal | **Сессия**: negotiated version/capabilities, request IDs и соответствие response, request cancellation. Совпадающие IDs разных клиентов не одна операция | Живой фасад сохраняет MCP endpoint без нового initialize, а не перепривязывает его к SDK-сессии другого клиента. In-flight вызов при crash требует явного отказа; исполненную запись не replay | U-C-02/03/05: mapping внутренних запросов/поколений — task-01/03/04 |
| S20 — progress, [McpToolProgressReporter](../../../../Tools/McpToolProgressReporter.cs), [CliProgress](../../../../Services/Build/CliProgress.cs), [DotNetCliRunner](../../../../Services/Build/DotNetCliRunner.cs) | SDK binds IProgress для текущего запроса (token-less NullProgress — no-op); reporter хранит `_lastProgress`; heartbeat через Task.Delay каждые 5s на CLI step. Failure отправки не меняет CLI outcome | **Сессия/вызов**, нельзя рассылать token/progress соседнему клиенту | Сохранить корреляцию у выжившего фасада; закрыть/отменить старый in-flight канал по исходу. Прогресс не доказательство completion или отсутствия side effects | U-C-05/08: отдельно от результата и срока жизни CLI |
| S21 — runtime tools/profile, [McpToolProfileOptions](../../../../Hosting/McpToolProfileOptions.cs), [McpToolActivationService](../../../../Hosting/McpToolActivationService.cs), [McpRuntimeToolCollection](../../../../Hosting/McpRuntimeToolCollection.cs) | `ROSLYN_MCP_TOOL_PROFILE` full/lite и `ROSLYN_MCP_TOOL_GROUPS` читаются при регистрации. Collection/activation singleton; dynamicGroups/order под lock. EnableGroup в lite добавляет отсутствующие tools, один Changed через DeferChangedEvents; повтор идемпотентен; full — no-op | **Сессия**, текущая process-wide collection — **конфликт** для нескольких клиентов. Built-in catalog — общая неизменяемая метаинформация, не общая activation | Фасад сохраняет startup/dynamic groups и видимый список при переключении и crash чужого host. Не сбрасывать lite и не расширять tools другого клиента | U-C-02/05/08: одних одинаковых workspace descriptor недостаточно для общей tool collection |
| S22 — MCP subscriptions, [интеграционные тесты](../../../../RoslynMcpServer.Tests/Hosting/McpToolGroupEnablementIntegrationTests.cs), [handshake helper](../../../../RoslynMcpServer.Tests/Support/McpInitializeHandshake.cs) | Для 2025-11-25 tools/list_changed приходит после initialize; для 2026-07-28 — после subscriptions/listen/acknowledgement. Listen остаётся открытым до cancellation. Production подключает SDK notifications, собственного registry подписок нет | **Сессия** SDK-фасада; не FileSystemWatcher subscriptions | Listen и negotiated semantics живут в выжившем фасаде, а не пересоздаются новым initialize агента. Погибший совмещённый фасад теряет свою подписку | U-C-05: сохранять доступные подписки, не предполагать одинаковые правила версий; это чтение существующих тестов, не новый spike |
| S23 — plugins и DI, [PluginStartup.Run](../../../../Plugins/PluginStartup.cs), [IRoslynMcpPlugin](../../../../Plugins/IRoslynMcpPlugin.cs), [RoslynMcpPluginContext.AddToolsFrom](../../../../Plugins/RoslynMcpPluginContext.cs), [PluginAssemblyLoad](../../../../Plugins/PluginAssemblyLoad.cs) | Discovery: drop-in exe/plugins, config plugins, `ROSLYN_MCP_PLUGINS`; Register до provider, plugins могут добавлять services/singletons. Validation provider пробует constructors; фабрика per invocation использует общий DI и тот же SolutionManager. Accepted names и MethodInfo — в startup Report. Plugin ALC не collectible | **Идентичность host**, произвольное plugin state — **конфликт**, пока не проверена его семантика для нескольких клиентов. Built-in read/process classification не доказывает side effects plugin | При demotion plugins/provider/MethodInfo/ALC остаются. Crash теряет instances, но не plugin sources. Подмена DLL при том же имени/версии не подтверждает совместимость | U-C-01/02/06/07/08: pure proxy нужен tool metadata для tools/list/help, но загружать plugin assemblies нельзя; источник метаданных ещё неизвестен |
| S24 — static plugin reservations, [RoslynMcpPluginContext](../../../../Plugins/RoslynMcpPluginContext.cs): `Reservations` | ConditionalWeakTable по IServiceCollection, reservation locks/names защищают коллизии регистрации; registrar откатывает добавленные descriptors при отказе | **Общий** startup registry одного контейнера, не MCP session activation | Живёт с collection/ссылками, новое создание provider не мигрирует plugin state; crash теряет | U-C-02/08: registration bookkeeping не разделяет состояния plugin-сервисов |
| S25 — logs и harness id, [Program](../../../../Program.cs), [HarnessSessionId](../../../../Diagnostics/HarnessSessionId.cs), [ToolTelemetry](../../../../Diagnostics/ToolTelemetry.cs), [ToolLogAnalyzer](../../../../Diagnostics/ToolLogAnalyzer.cs) | Token harness+random suffix один на процесс, определён по parent process; Serilog LogContext и глобальный sink, Information, shared `exe/logs/mcp-.log`, daily rolling, 14 файлов. `MCP_LOG_INCOMING_RPC`/`MCP_LOG_INCOMING_RPC_MAX_CHARS` замыкаются в filter при регистрации; `ROSLYN_MCP_LOG_TOOL_OUTPUT` читается для full response logging | **Общий** sink/файл процесса, **сессия** attribution и logging preference; process SessionId не идентифицирует всех клиентов shared host. Log files уже могут содержать записи разных PID | Живой фасад не теряет свою корреляцию; новый host имеет иной process token. Файлы остаются при crash, но не являются completion journal; чтение tail без фильтра не изолирует клиента | U-C-02/04/05: не выдавать log одного процесса за одну MCP-сессию; не наследовать чужую настройку логирования параметров |
| S26 — Console и stdio, [Program](../../../../Program.cs), [MsBuildBootstrapper](../../../../Diagnostics/MsBuildBootstrapper.cs), [PluginStartup](../../../../Plugins/PluginStartup.cs), [HarnessSessionId](../../../../Diagnostics/HarnessSessionId.cs) | Диагностика старта/ошибки идут в Console.Error; стандартный transport владеет stdin/stdout. В production исходниках вне транспорта прямых Console.In/Out/WriteLine не найдено; stdout в test/spike programs не часть сервера | **Сессия/процесс** для stdio, **общий** stderr процесса. Plugin-код может обращаться к Console — контракт его не запрещает | Host-client при EOF подчинён текущему stdio/Host lifetime; удержание роли после EOF ещё не доказано. Crash убивает endpoint своего фасада | U-C-04/05/07: diagnostics не писать в MCP stdout; harness lifetime проверяет task-05 |
| S27 — process-wide navigation overflow, [NavigationOverflowStore](../../../../Services/Navigation/NavigationOverflowStore.cs) | Static Gate/Order/ById и clock, cursors с движущимся offset; 8 entries, 2M chars, TTL 30min, chunks 16k; нет owner/session/generation. max-results: явный аргумент → `ROSLYN_MCP_MAX_RESULTS` → file → default; preview из file | **Сессия / конфликт**: чужой запрос может вытеснить entry, cursor позволяет получить старый текст другого workspace при наличии ID | При смене host store не переезжает; существующий cursor не означает новый поиск. Crash уничтожает payload/offset; explicit unavailable лучше смешивания поколений, точная политика task-01 | U-C-02/05/06/08: обработать ownership, lifetime и отсутствие replay; фасад должен знать судьбу выданного cursor |
| S28 — process reports, [DiagnosticReportStore / DiagnosticReportAttachment](../../../../Services/DiagnosticReportStore.cs) | Static Gate/Order/ById и clock; 4 reports, cap 1M chars каждый, TTL 15min, chunks 16k, redaction и offset. Cursor не привязан к клиенту/поколению; хранит build/test/run/raw-build output | **Сессия / конфликт** при общем store, eviction и выдаче по одному ID | Если CLI/result принадлежит живому proxy, host crash не обязан стирать его report. Если store в погибшем host — RAM потеряна; диск-логи не заменяют cursor payload | U-C-02/05/06/08: происхождение report и поколение отдельно от текущего workspace |
| S29 — NuGet DLL cache, [NuGetFallbackAssemblyResolver](../../../../Services/Resolve/NuGetFallbackAssemblyResolver.cs): `NuGetDllCache` | Static ConcurrentDictionary root+assemblyName → найденный путь/отрицательный результат; остаётся после reset. Это memoized disk lookup, не build proof и не CLR analyzer loader | **Общий** при одинаковом package root; **конфликт** freshness при изменении packages, не признак клиентской изоляции | Demotion сохраняет cache; crash сбрасывает, файлы packages остаются. Новый host может разрешить иначе после изменения диска | U-C-01/02/06/08: одинаковый assemblyName не подтверждает build/plugin identity |
| S30 — scratchpad, [UtilityTools.ManageAgentScratchpad](../../../../Tools/UtilityTools.cs) | Read/write/append/clear `.agent_memory/scratchpad.md` под process cwd; даже read создаёт каталог. Нет client namespace или блокировки между процессами | **Конфликт / сессия контекста cwd**, сам файл **общий** на диске: сейчас два MCP в одном cwd уже разделяют его | Host switch не должен менять выбор файла на cwd host. Crash не откатывает write/append/delete; retry append может дублировать текст | U-C-02/05/08: не объявлять scratchpad изолированным по MCP-сессии |
| S31 — reset и stop, [WorkspaceTools.ResetWorkspace](../../../../Tools/WorkspaceTools.cs), [SolutionManager.ClearWorkspaceAsync](../../../../Services/Workspace/SolutionManager.cs), [ServerLifecycleTools.StopMcpServer](../../../../Tools/ServerLifecycleTools.cs) | Reset закрывает watchers, disposes workspace, очищает loaded values/mapping/publication/diagnostics; файл config остаётся и lazy load снова возможен. Stop после 150ms вызывает IHostApplicationLifetime.StopApplication, при ошибке Environment.Exit(1) | **Конфликт**: reset общего graph виден всем; stop завершает весь процесс, включая совмещённый фасад. Это не disconnect одного клиента | Reset не unload CLR; stop прежнего host затронет всех клиентов. Семантика planned release/stop в shared mode не задана. Нельзя считать restart proxy эквивалентом restart analyzer host | U-C-02/03/04/07: требуются явные lifecycle semantics, выбор — task-01 |
| S32 — временные каталоги, [AnalyzerProvenanceCaptureService](../../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs), [AnalyzerReferenceShadowCopier](../../../../Services/Analyzers/AnalyzerReferenceShadowCopier.cs), [AnalyzerShadowGenerationPublisher](../../../../Services/Analyzers/AnalyzerShadowGenerationPublisher.cs), [AnalyzerShadowCacheCleanup](../../../../Services/Analyzers/AnalyzerShadowCacheCleanup.cs), [PluginShadowCopy](../../../../Plugins/PluginShadowCopy.cs) | Capture binlogs в temp/RoslynMcpServer-PID/GUID, bounded cleanup retry. Analyzer generations под temp/RoslynMcpServer.AnalyzerShadowCopy, content hashes/manifest, namespace v2-main-only; опубликованные поколения не удаляются reset и автоматически не GC. Plugin dev copy под temp/RoslynMcpServer/plugins, уникальные каталоги, старые/частичные копии не удаляет | **Общий** disk artifacts при проверенной generation identity; **идентичность host** loaded copies; cleanup может затронуть другие процессы | Crash оставляет файлы/partial staging; новый host не получает RAM mapping из одного наличия каталога. Cleanup опубликованных analyzer copies требует остановленных владельцев; plugin copies не доказательство готовности | U-C-01/04/06: shadow copy plugins/analyzers не является bootstrap shadow copy самого host |
| S33 — фоновые работы, [DotNetCliRunner](../../../../Services/Build/DotNetCliRunner.cs), [GitChangedFilesHelper](../../../../Services/GitChangedFilesHelper.cs), [RipgrepRunner](../../../../Services/RipgrepRunner.cs), [AnalyzerProvenanceCaptureService](../../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs), [StopAfterResponseAsync](../../../../Tools/ServerLifecycleTools.cs) | Request-scoped CLI/rg/git output reads, timeout/cancel и попытка kill process tree; heartbeat tasks, bounded output drain, Task.Run binlog replay, cleanup delays, отложенный stop. Watchers работают через callbacks. Независимый idle timer/host discovery daemon не зарегистрирован | **Вызов/сессия**, общие disk/CPU эффекты; **конфликт** если lifetime привязать только к host первого чата | Graceful cancellation пытается остановить child tree; sudden crash не исполняет finally/kill. Продолжение child CLI зависит от ОС/harness; успешный exit без ответа не разрешает replay | U-C-03/04/05/08: дедлайны/отмена/владение CLI требуют отдельного решения и spike |

### Остальные static-объекты и тестовые seams

Это закрывает также static readonly с изменяемым типом: readonly запрещает
замену ссылки, но не делает коллекцию неизменяемой. В текущем production-коде
следующие таблицы/настройки заполняются при инициализации и не являются mutable
состоянием пользовательской сессии. Классификация — **общий**, с идентичностью
версии реализации host; при demotion остаются, при crash создаются заново.
Последствия для U-C-01/06/08 — учитывать code identity/остаточную память, но не
переносить их как клиентские данные.

- Каталоги и help: [McpToolCatalog](../../../../Hosting/McpToolCatalog.cs)
  (`All`, `HostTypes`, `WithheldUntilV2`, `ReservedBootstrapToolNames`),
  [McpToolGroups.All](../../../../Hosting/McpToolGroups.cs),
  [McpToolHelpCatalog.Entries](../../../../Hosting/McpToolHelpCatalog.cs).
  Withheld 9 методов не входят в 54 публичных tools; новые defaults/activation
  не выводятся из этих static-таблиц.
- Наборы исключений: [CodeSkeletonTools.SkippedDirectorySegments](../../../../Tools/CodeSkeletonTools.cs),
  [UtilityTools.ExcludedDirectories](../../../../Tools/UtilityTools.cs),
  [WorkspaceDiskPathFilter](../../../../Services/Workspace/WorkspaceDiskPathFilter.cs),
  [WorkspaceLoadGuidance](../../../../Services/Workspace/WorkspaceLoadGuidance.cs),
  [ProjectRenameHelper](../../../../Services/Editing/ProjectRenameHelper.cs),
  [TestAttributeMatcher.TestAttributeRoots](../../../../Services/Testing/TestAttributeMatcher.cs),
  [RoslynMcpFileSettings.KnownKeys](../../../../Config/RoslynMcpFileSettings.cs).
- Lookup/contract tables: [BclAssemblyPackageMap.Map](../../../../Services/Resolve/BclAssemblyPackageMap.cs),
  [AnalyzerHostContractCatalog.Contracts/Names](../../../../Services/Analyzers/AnalyzerHostContractCatalog.cs),
  [PluginDiscovery._checkedAssemblies](../../../../Plugins/PluginDiscovery.cs),
  [WorkspaceInputMapBuilder.WalkUpFileNames](../../../../Services/Workspace/WorkspaceInputMapBuilder.cs),
  [DotNetSdkEnvironment.MsBuildSdkOverrideVariables](../../../../Services/Build/DotNetSdkEnvironment.cs),
  [SolutionProjectTargetResolver](../../../../Services/Build/SolutionProjectTargetResolver.cs).
- Regex/format policies: [DirectoryBuildPropsReader](../../../../Services/Workspace/DirectoryBuildPropsReader.cs),
  [SolutionConfigurationCatalog](../../../../Services/Workspace/SolutionConfigurationCatalog.cs),
  [DecompilerHost](../../../../Services/Resolve/DecompilerHost.cs),
  [ProjectRenameHelper](../../../../Services/Editing/ProjectRenameHelper.cs),
  [DotNetBuildProbe](../../../../Services/Build/DotNetBuildProbe.cs),
  [BuildOutputReport](../../../../Diagnostics/BuildOutputReport.cs),
  [MsBuildLogHighlighter](../../../../Diagnostics/MsBuildLogHighlighter.cs),
  [TruncatedProcessLog](../../../../Diagnostics/TruncatedProcessLog.cs),
  [VstestOutputParser](../../../../Diagnostics/VstestOutputParser.cs),
  [WorkspaceDiagnosticFormatter](../../../../Diagnostics/WorkspaceDiagnosticFormatter.cs),
  [WorkspaceLoadDiagnosticsReporter](../../../../Diagnostics/WorkspaceLoadDiagnosticsReporter.cs),
  [ToolLogAnalyzer.HighlightSubstrings](../../../../Diagnostics/ToolLogAnalyzer.cs),
  [ProcessOutputRedactor regexes](../../../../Services/DiagnosticReportStore.cs),
  [PatchMatchHelper.RegexMatchTimeout](../../../../Tools/PatchMatchHelper.cs).
- Serializer settings: [NuGetTools.PrettyJson](../../../../Tools/NuGetTools.cs),
  [AnalyzerShadowManifest.JsonOptions](../../../../Services/Analyzers/AnalyzerShadowManifest.cs),
  [WorkspaceSnapshotCodec](../../../../Services/Workspace/StateCache/WorkspaceSnapshotCodec.cs).
  Последний используется изолированными cache experiments, не production restore
  route этой темы. Его наличие не делает восстановление RAM частью task-00.
- Stateless sentinels/comparers: [InputPathCanon](../../../../Services/Workspace/InputPathCanon.cs),
  [SourceTextEncoding.BomFreeUtf8](../../../../Services/Workspace/SourceTextEncoding.cs),
  [StableFileRead](../../../../Services/Workspace/StableFileRead.cs),
  [WorkspaceWriteOperationContext.Unverified](../../../../Services/Workspace/WorkspaceWriteResult.cs),
  [InputCoverageScope.WholeGraph](../../../../Services/Models/InputCoverageScope.cs),
  [SemanticPublicationState](../../../../Services/Analyzers/SemanticPublicationState.cs),
  [AnalyzerExecutionObservation.None](../../../../Services/Analyzers/AnalyzerLoaderContract.cs),
  [PluginStartup.Report.Empty](../../../../Plugins/PluginStartup.cs),
  [RoslynMcpPluginRegistration.Empty](../../../../Plugins/RoslynMcpPluginRegistration.cs),
  [AstModificationHelper.UsingDirectiveComparer](../../../../Services/Editing/AstModificationHelper.cs),
  [RoslynCodeFixBridge reflection handles](../../../../Services/Editing/RoslynCodeFixBridge.cs).
  Reflection handles и MEF могут удерживать CLR metadata; reset не обнуляет их.

Mutable test/diagnostic seams в production assembly учтены отдельно: это
**общие процессные** значения, не клиентские настройки. На normal path fault
injection не устанавливается; counters могут увеличиваться. При crash
сбрасываются, при demotion не обязаны сбрасываться. U-C-02/06: не превращать
их в shared-client API и не путать счётчики с proof.

- [RipgrepRunner](../../../../Services/RipgrepRunner.cs): `ResolveBinaryOverride`,
  `FindOnPathOverride`; [WorkspaceDiskWatcherStarter](../../../../Services/Workspace/WorkspaceDiskWatcherStarter.cs):
  `FailNextStartForTests`.
- [AnalyzerExecutionGate](../../../../Services/Analyzers/AnalyzerExecutionGate.cs):
  `AssemblyEvaluationCount`; [AnalyzerPrivateDependencyInspector](../../../../Services/Analyzers/AnalyzerPrivateDependencyInspector.cs):
  `InspectCount`; [AnalyzerReferenceShadowCopier](../../../../Services/Analyzers/AnalyzerReferenceShadowCopier.cs):
  `ForcedAccessFailurePathContains`, `PathProbeCount`.
- [AnalyzerShadowGenerationPublisher](../../../../Services/Analyzers/AnalyzerShadowGenerationPublisher.cs):
  `AnalyzerFileIoCount`, `RemainingForcedCopyFailures`,
  `RemainingForcedAccessFailures`, `RemainingForcedMoveFailures`,
  `RemainingForcedDiskFullFailures`, `ForcedDiskFullStage`,
  `RemainingForcedPdbCopyFailures`, `BeforeMoveGatePath`, `BeforeMoveGateTimeout`,
  `MutateSourceAfterFirstHash`, `AfterRequiredCopyBeforeManifest`, `BeforeMove`.
- Clocks `TimeProviderForTests` в S27/S28; per-manager failure/callback hooks в
  [SolutionManager](../../../../Services/Workspace/SolutionManager.cs) и
  `_nextFailureMode` capture service не static, но разделяют lifetime singleton.
  Они также не состояние отдельного MCP-клиента.

## Инструменты и этапы исполнения

Ниже покрыты **все 54 built-in public tools** из
[McpToolCatalog.Build](../../../../Hosting/McpToolCatalog.cs). «Оба» означает
кандидатную допустимость выполнения этапа при явно сохранённом контексте, а не
принятую маршрутизацию или готовность нынешнего DI для чистого proxy.
`host` означает необходимость workspace/его admission; `proxy` — принадлежность
клиентского MCP-фасада. Plugin tools перечисляются отдельно, поскольку их
набор и побочные эффекты определяются загруженными plugins.

Общее для каждого CLI-этапа ниже: side effects на диске/сети не откатываются
сменой привязки. В живом исполнителе результат относится к исходному вызову,
даже если клиент уже сменил workspace. При смерти исполнителя завершение и
side effects могут быть неизвестны; CLI автоматически не повторяется. Если
умирает только другой процесс host, уже запущенный в живом proxy CLI физически
может продолжить, но принятие workspace proof требует новой проверки контекста.
Это граница task-01, не реализованный recovery protocol.

| Инструмент / этап → код | Нужен workspace | cwd / сессия / build proofs | Допустимое место | Потеря контекста при смене/падении |
|---|---|---|---|---|
| `load_workspace`: defaults / выбор, [WorkspaceTools](../../../../Tools/WorkspaceTools.cs), [LoadAndPrepareAsync](../../../../Services/Workspace/SolutionManager.cs) | Для физического load ещё нет; нужен контекст клиента | Relative path от cwd; omitted args, file defaults, opt-in и CLI suffix клиента | proxy для контекста; host для physical load | Не заимствовать Loaded* прежнего/нового host; старую привязку proxy сохранять до успешного перехода по scope |
| `load_workspace`: physical load / capture / prepare / publication / summary, [SolutionManager](../../../../Services/Workspace/SolutionManager.cs) | Создаёт и публикует | Toolset/graph/analyzer admission, токен вызова, load session | host | Crash теряет attempt/RAM; release согласует in-flight, одна новая ordinary load на владельца, не во всех proxy |
| `reset_workspace`, [WorkspaceTools](../../../../Tools/WorkspaceTools.cs) | Меняет общий manager, даже если graph пуст | Config и CLR history остаются; loaded args сбрасываются | host; policy вызова — task-01 | Влияет на всех пользователей этого graph; reset не restart и не новый чистый analyzer process |
| `get_class_skeleton`, `get_diagnostics_for_file`, [CodeAnalysisTools](../../../../Tools/CodeAnalysisTools.cs) | Да, document/semantic snapshot | Path от workspace, publication gate; diagnostics может исполнять analyzers | host | Старый snapshot/result не становится данными нового поколения; crash теряет вычисление |
| `find_symbol_definition`, `find_usages`, `find_symbol_references`, `find_implementations`, `get_call_graph`, [NavigationTools](../../../../Tools/NavigationTools.cs) | Да, кроме чтения готового overflow | IDs, sanitized published snapshot, max-results/preview клиента | host для поиска; оба для owned cursor payload | Crash теряет RAM search/cursors; старый cursor не означает выполнение на новом graph |
| `add_using`, `add_method_to_class`, `update_method_body`, [AstTools](../../../../Tools/AstTools.cs) | Да | Document и write-base/admission, path от workspace; disk write общий | host | После частичной записи/потери ответа не replay; старый base не применим в новом generation |
| `get_code_fixes`, `apply_code_fix`, [CodeFixTools](../../../../Tools/CodeFixTools.cs) | Да | Diagnostics/fix selection и stale base, analyzer/write gate | host | Index/fix прежнего snapshot не подтверждает исправление нового; побочные эффекты могут уже быть записаны |
| `rename_symbol`, [UtilityTools](../../../../Tools/UtilityTools.cs); `generate_test_method_stub`, [TestTools](../../../../Tools/TestTools.cs) | Да | Semantic document, Roslyn rename или stub, write boundary | host | Дисковые изменения видны всем; crash в записи не разрешает повтор |
| `list_projects`, `get_project_graph`, [UtilityTools](../../../../Tools/UtilityTools.cs) | Да | Published graph и его IDs; без proofs | host | Нельзя заменить graph проектом другого клиента при смене привязки |
| `rename_project`, `add_package_reference`, `remove_package_reference`, [ProjectTools](../../../../Tools/ProjectTools.cs), [ProjectRenameHelper](../../../../Services/Editing/ProjectRenameHelper.cs) | Зависит от этапа: пути могут разрешаться без graph; активный manager участвует в записи/сбросе | Graph files и диск; rename использует searchRoot/workspace/cwd; package helpers делают Path.GetFullPath от cwd. Tracked writes и последующий ClearWorkspaceAsync для package edits и применённого rename | host для связанного manager; оба для автономной disk части при согласовании | Меняют общие project/solution files, graph устаревает или очищается; crash оставляет side effects, не replay |
| `get_file_content`, `read_file_range`, [RoslynTools](../../../../Tools/RoslynTools.cs), [UtilityTools](../../../../Tools/UtilityTools.cs) | Нет семантического graph | Relative path через manager.ResolvePathAgainstWorkspace, иначе cwd; чтение диска | оба после определения пути | Смена контекста может поменять относительный путь; ответ относится к прочитанному файлу |
| `get_method_body`, [UtilityTools](../../../../Tools/UtilityTools.cs); `get_code_skeleton`, [CodeSkeletonTools](../../../../Tools/CodeSkeletonTools.cs) | Нет graph, но **нужен Roslyn parser** | Method path от workspace/cwd, skeleton path от cwd; CSharpSyntaxTree, syntax rewriting/formatting, без build proofs | host для Roslyn-этапа при требовании чистого proxy; оба для disk read с готовым путём | Отсутствие workspace не означает допустимость загрузки Roslyn в pure proxy; disk payload/result относится к исходному файлу |
| `list_directory_tree`, [UtilityTools](../../../../Tools/UtilityTools.cs) | Нет | Явная директория от cwd, disk обход | оба | Нет RAM workspace для переноса; не разрешать путь заново по чужому cwd |
| `search_code`, [UtilityTools](../../../../Tools/UtilityTools.cs), [RipgrepRunner](../../../../Services/RipgrepRunner.cs) | Нет при explicit directory; omitted roots читают loaded file + project dirs | Roots, extensions/regex/caps/timeout; ripgrepPath/jsonc/PATH и opt-in; rg запускает процесс, несмотря на read classification каталога | host для graph-derived roots; оба для сканирования с готовыми roots | Старые roots/result не подменяются новым workspace; rg crash/cancel — отдельный исход, не proof |
| `update_file_content`, `apply_patch`, [EditingTools](../../../../Tools/EditingTools.cs), [UtilityTools](../../../../Tools/UtilityTools.cs) | Может не быть; существующий workspace участвует в tracked write/admission | Path resolution, preflight для известного document, fallback disk paths; patch text/encoding | host для manager-related write; оба для независимой disk части при согласовании | Частичный/успешный disk write остаётся; новый host должен увидеть disk и заново пройти freshness, не replay |
| `explore_assembly`, `decompile_type`, `get_decompiled_class_skeleton`, `get_decompiled_method_body`, [CodeAnalysisTools](../../../../Tools/CodeAnalysisTools.cs), [DecompilerHost](../../../../Services/Resolve/DecompilerHost.cs), [AssemblyReferenceResolver](../../../../Services/Resolve/AssemblyReferenceResolver.cs) | Explicit DLL может быть без workspace; resolving references использует graph | DLL path, deps.json, NUGET_PACKAGES, BCL map/cache; не SessionBuildState | host для разрешения через graph; оба для standalone DLL, с учётом запрета Roslyn в pure proxy | RAM lookup теряется при crash; DLL на диске может уже измениться. Перенос этапа требует проверки фактических зависимостей |
| `run_dotnet_build`: target/defaults, [BuildTools](../../../../Tools/BuildTools.cs), [SolutionProjectTargetResolver](../../../../Services/Build/SolutionProjectTargetResolver.cs) | Graph не обязателен: solution target читается с диска | Path.GetFullPath от cwd, root/global.json; Configuration/Platform/BuildArgs от session. TFM load не наследуется | оба с defaults исходного клиента | Захватить исходный target/config до CLI; не брать Loaded* нового host посреди вызова |
| `run_dotnet_build`: probe / parse / report, [DotNetBuildProbe](../../../../Services/Build/DotNetBuildProbe.cs), [BuildTools](../../../../Tools/BuildTools.cs) | Нет после подготовки | CLI env/cwd, progress/cancel, bounded steps и report store. Не создаёт DLL-route session proof | оба; proxy — кандидат | CLI может пережить удалённый host; exit относится к исходному build, не подтверждает новый generation |
| `run_dotnet_test`, `run_specific_test`, `run_test_by_filter`: target/defaults/filter, [TestTools](../../../../Tools/TestTools.cs), [TestFilterHelper](../../../../Services/Testing/TestFilterHelper.cs) | Обычный project route может без graph; DLL route — да | Relative workspace/binaries paths через manager; config/platform/buildArgs; defaults noBuild различаются между tools | оба для project route с контекстом; host для semantic/DLL resolution | Не терять выбранный project/filter/policy. Переключение не меняет исходный target вызова |
| `run_specific_test`: уточнение filter до project/DLL route, [RunSpecificTest](../../../../Tools/TestTools.cs) | Запрашивает sanitized published solution (может вызвать config lazy load); без solution есть fallback filter | Semantic атрибуты/имена tests при наличии graph, sanitized retry; это дополнительная зависимость даже project route | host для semantic filter, затем CLI — отдельный этап | При потере graph не подменять уже выбранный filter новым; перенос всего run_specific_test в proxy пропустит эту зависимость |
| Те же 3 tools, project route: prebuild → test → parse/report, [DotNetTestArguments](../../../../Services/Build/DotNetTestArguments.cs), [TestTools](../../../../Tools/TestTools.cs) | Нет после target/defaults | Optional pre-test build и `dotnet test --no-build`, общий timeout, outputs/progress; не DLL proof | оба; proxy — кандидат | Успешный CLI не становится proof для DLL route другого generation; crash может оставить bin/obj/results |
| Те же 3 tools, DLL route: resolve/admission, [TestTools.ExecuteDllTestsAsync](../../../../Tools/TestTools.cs), [TestAssemblyPathResolver](../../../../Services/Testing/TestAssemblyPathResolver.cs), [TestDllEnsure](../../../../Services/Build/TestDllEnsure.cs) | **Да**: PublishedSolutionAfterDiskSync, ProjectId/AssemblyName hints, loaded solution | BuildContext, PullInputSnapshot, SessionBuildState, mapping checks; context admission/waiters | host для workspace/admission; место proof state решает task-01 | Старый ProjectId/proof не относится к новому host; отказ при смене generation/membership перед стартом tests |
| Те же 3 tools, DLL route: build/test launch и post-build confirmation, [TestDllEnsure](../../../../Services/Build/TestDllEnsure.cs) | Launch сам нет; gate/confirmation да | Build loaded solution `-t`, output stamp/existence, repeated pull, context gate; один rebuild при движении inputs в том же timeout | оба для CLI с согласованным admission; host для pull/gate | При crash host во время proxy CLI нужно заново решать confirmation; success старой сборки не разрешает tests нового generation |
| `run_dotnet_run`, [RunTools](../../../../Tools/RunTools.cs) | Нет, host держит только logger | Explicit csproj через cwd, default workDir от project/global.json или override; отдельный build, затем run --no-build, один timeout; не наследует manager Loaded* | оба; proxy — кандидат | Приложение/его side effects могут продолжиться в живом исполнителе; результат не workspace proof |
| `execute_dotnet_command`, [UtilityTools](../../../../Tools/UtilityTools.cs), [DotNetCliRunner](../../../../Services/Build/DotNetCliRunner.cs) | Нет | Explicit workingDirectory или process cwd, arbitrary arguments/env; special report только для build verb; не подтверждает SessionBuildState | оба; proxy — кандидат | Команда может менять диск/сеть; потерянный ответ не разрешает автоматический retry |
| `get_changed_files`, [UtilityTools.ResolveGitAnchorPath](../../../../Tools/UtilityTools.cs), [GitChangedFilesHelper](../../../../Services/GitChangedFilesHelper.cs) | Нет при explicit path; omitted берёт первый loaded project либо cwd | Git working tree/index/anchor; без build proofs | host для loaded default; оба для git с готовым anchor | Диск/index общие; смена workspace не должна сменить anchor текущего запроса |
| `run_format`, [UtilityTools](../../../../Tools/UtilityTools.cs) | Нет Roslyn workspace сервера | Path/cwd, dotnet/global.json; verifyOnly либо disk writes; watchers увидят результат | оба; proxy — кандидат | Изменённые файлы остаются при crash; output не заменяет graph reload для composition changes |
| `list_nuget_packages`, `run_nuget_audit`, `list_outdated_packages`, `search_nuget_registry`, [NuGetTools](../../../../Tools/NuGetTools.cs) | Нет (NuGetTools держит только logger) | Explicit paths/query, root/cwd, SDK, feeds/credentials/network, package cache. Нет build proofs | оба; proxy — кандидат | CLI/network result и cache effects не переезжают с graph, повтор запроса — по явному исходу |
| `get_test_list`, [TestTools](../../../../Tools/TestTools.cs), [TestDiscoveryHelper](../../../../Services/Testing/TestDiscoveryHelper.cs) | Да | Published solution + disk sync, Roslyn syntax/attributes/project selection; это не VSTest процесс | host | Потеря snapshot/IDs при смене; нельзя принимать список проекта другого клиента |
| `get_mcp_server_info`, [ServerLifecycleTools](../../../../Tools/ServerLifecycleTools.cs), [McpServerInfoHelper](../../../../Services/McpServerInfoHelper.cs) | Читает состояние, но не запускает lazy load | Binary/base/log/cwd/config/plugins процесса и activation клиента смешаны в ответе | оба, с явным источником частей — task-01 | Выживший proxy и новый host имеют разные binary/session/workspace facts; нынешний единый ответ их не разделяет |
| `list_tool_groups`, `get_tool_help`, `enable_tool_group`, [ToolHelpTools](../../../../Tools/ToolHelpTools.cs), [McpToolHelpFormatter](../../../../Hosting/McpToolHelpFormatter.cs) | Нет graph; plugin help читает startup Report | Profile/dynamic activation клиента; plugin method metadata сейчас требует загруженного plugin | proxy для клиентского surface; host как источник plugin metadata ещё требует design | Сохранять tools/list/activation без initialize; не объявлять plugin отсутствующим только из-за нового host |
| `read_log_tail`, `tail_tool_log`, [UtilityTools](../../../../Tools/UtilityTools.cs), [LogTailReader](../../../../Services/LogTailReader.cs) | Нет | Default log — exe/logs процесса, explicit path и фильтры; HarnessSessionId не client authorization | оба с указанным источником | На новом host default указывает другой binary/log directory; crash не удаляет старый log, tail не completion proof |
| `manage_agent_scratchpad`, [UtilityTools](../../../../Tools/UtilityTools.cs) | Нет | Только process cwd; общий disk file, write/append/clear | оба технически, proxy — кандидат для сохранения cwd | Переключение host не меняет файл клиента; disk effects/replay риски — S30 |
| `stop_mcp_server`, [ServerLifecycleTools](../../../../Tools/ServerLifecycleTools.cs) | Нет graph, но останавливает IHost процесса | Process lifetime и задержка ответа, не workspace reset | текущий процесс; shared policy не определена | Host-stop влияет на все его proxy; proxy-stop не выгружает analyzer host. Это конфликт, не готовое правило маршрутизации |
| Plugin tools, [RoslynMcpPluginContext](../../../../Plugins/RoslynMcpPluginContext.cs), [sample](../../../../samples/RoslynMcpPlugin/SampleTools.cs) | Определяет plugin, доступ к общему SolutionManager разрешён | Arbitrary DI/static/disk/CLI state; name prefix предотвращает коллизии, не изоляцию | host при текущем запрете plugin load в pure proxy | Неизвестны side effects/lifetime стороннего plugin; его повтор и миграция state не безопасны по одному имени tool |

### Что нужно чистому proxy до promotion

По [Program.cs](../../../../Program.cs) и
[регистрации tools](../../../../Hosting/RoslynMcpServiceCollectionExtensions.cs)
фасаду нужны stdio MCP endpoint, клиентские profile/groups/config/cwd/log
correlation, metadata встроенных tools и совместимый список/schema/help plugin
tools для первого tools/list. Ему не нужен MSBuild Register, static MEF,
force-load Roslyn, provenance service, workspace/watchers или исполнение
PluginStartup.Run. Текущий каталог хранит `Type`/`MethodInfo` реальных hosts,
а plugin Report также хранит MethodInfo: готового способа получить эти metadata
без тяжёлых assemblies в исходниках нет. Это зависимость для task-01/03, а не
предложение конкретного manifest или protocol.

При promotion добавляется host-only состояние S08–S18 и plugins S23. При
demotion можно закрыть active workspace/watchers, но оставшийся процесс уже
не «чистый proxy»: сохраняются Locator/MEF, Roslyn/analyzer/plugin assemblies,
resolver handler, DI singletons и static stores. Дополнительно
`ClearWorkspaceAsync` не обнуляет `_sanitizedPublishedSolution` и
`_sanitizedPublishedSolutionSource`; `GetOrCreateSanitizedPublishedSolution`
при `_solution == null` возвращает null, не освобождая эти ссылки. Поэтому
старый snapshot может удерживаться даже после Dispose workspace, до замены
cache или смерти manager/process. Исправление/замер — вне docs-only task-00.
**Исправлено в 1.5.18** по ревью (F-02): кеш сбрасывается при reset и при свежем
load, где он также держал старый граф рядом с новым; см. «Главное», п. 6.

## Неизвестно и передача в следующие задачи

| ID | Что неизвестно и почему | Кому / последствие |
|---|---|---|
| N01 | Точный descriptor/key и канонизация путей, полный hash server DLL/plugins, env/config inputs. Код пока одно-клиентский, matcher пропущенных аргументов не является matcher совместимости клиентов | Task-01, U-C-01: S04–S09/S13–S17/S23 — входы, не принятое решение |
| N02 | SDK seam сохранения endpoint, ID mapping, cancellation/progress/listen и plugin schemas без загрузки assemblies в proxy. Тесты одной session подтверждают текущий handshake, но не switching/recovery | Task-03/04/01, U-C-05/08; нужна проверка обеих protocol revisions |
| N03 | Кто владеет CLI, admission/proof state и report payload после switching/crash; как подтверждать результат старого CLI в новом поколении. Нынешние delegate PullSnapshot и ProjectId — in-process | Task-01/04, U-C-02/03/08; таблица не выбирает перенос целого test tool |
| N04 | Поведение настоящих harness при Reload/EOF/kill, продолжение parent/children/отделённых процессов и memory baseline ~20 MCP | Task-05, U-C-04/06/07; процессные эксперименты не выполнялись, переносить graceful kill guarantees на crash нельзя |
| N05 | Бюджеты single-flight, release/handoff/admission, last waiter cancellation и кандидат с уже загруженными assemblies после demotion. В коде нет IPC owner/generation/readiness | Task-04/01, U-C-03/04/07; semaphore одного manager не доказывает multi-process exclusivity |
| N06 | Фактическая память после ClearWorkspace/demotion, lifespan SDK BuildHost после disposal/crash и конкурентные CPU/latency. Исходники показывают удерживаемые ссылки/assemblies, но не величину освобождаемой RAM | Task-05/02/01, U-C-04/06/08; working-set warning 1.5GB не eviction policy |
| N07 | Произвольное состояние и фоновые процессы сторонних plugins. Register допускает дополнительные DI services и код, startup constructor check не доказывает multi-client безопасность | Task-01 и конкретный plugin audit, U-C-01/02/08; для встроенного sample вывод нельзя распространить на все plugins |
| N08 | Механизм безопасного bootstrap/shadow-copy самого server host и межплатформенный IPC/ACL. Нынешние analyzer/plugin copies решают другие задачи | Task-04/01/05, U-C-01/04/07; не выдавать копию plugin за готовый host bootstrap |

## Проверки и итог приёмки task-00

Проверен scope task-00: static mutable/DI, cwd/env, Console/stdio, bootstrap/
BuildHost, runtime groups и обе protocol revisions, plugins/DI/metadata,
logging/config, omission/sticky policy/build args/proofs, reset/stop/scratchpad,
фоновые работы/watchers/temp/shadow-copy, pure proxy/promotion/demotion,
полный публичный built-in catalog и этапы смешанных инструментов. Каждая строка
имеет ссылку на исходник; различены текущие факты, необходимые границы и неизвестные.
U-C-01…U-C-08 связаны с конкретными строками; разрешения на design/activation нет.

Validation, 2026-10-26:

- Локальные Markdown links пяти затронутых документов проверены по существованию
  файлов; ссылки на код разрешаются от каталога evidence. Покрытие таблицей всех
  54 имён public catalog проверено сравнением с `McpToolCatalog.Build`:
  54/54, пропущено 0; основных строк состояния S01–S33 — 33.
- `git diff --check` — PASS; просмотр diff и сверка task/index/topic/docs index
  — PASS. Изменены только Markdown; production, tests, fixtures, конфигурация
  и exports не менялись.
- Самопроверка соответствия заданию выполнена; независимых раундов ревью — 0,
  исправлений по независимому ревью — 0. Независимая приёмка design/эпохи и
  решение владельца о модели не подменяются этим результатом.
- Build, main CI, SourceStructure и AnalyzerLifecycle **не запускались**:
  задание docs-only предписывает link/diff checks, C# и тесты не менялись.
  MCP load с incomplete assets не выдаётся за build/test validation.

Task-00 завершён в scope документированной инвентаризации. Task-01–task-05
остаются planned; архитектура shared host, реализация E0 и production readiness
не приняты. Статусы синхронизированы в задании, карте эпохи и индексах темы/docs.
