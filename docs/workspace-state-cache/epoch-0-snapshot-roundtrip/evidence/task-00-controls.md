# E0/task-00 — seams и выбранный контроль

Дата: 2026-10-07 (Europe/Moscow). Статус: **accepted — docs-only inventory seams и выбор real control**.
Независимое [ревью round 1](task-00-review-round-1.md): два замечания;
[recheck round 2 / validation](task-00-review-round-2.md), 2026-10-07: оба закрыты. Capability verdict отсутствует.
Контроль выбран для bounded S-001; capture-ready ему не присвоен.
Исполнитель: Grok 4.7. Отдельное значение reasoning в сессии не задавалось.

Задача — inventory и выбор. Executable S-001 остаётся
[task-08](../task-08-real-control-capability-hi.md). Production код не менялся.
C# build/test suite для этой docs-only правки не запускался.

## Provenance

- HEAD: `7ab1d8eb7e1811d9c1602845fa53acd945d720e0`.
  Сообщение: `docs: resolve U-001 with bounded strong-name MVP scope`.
  Дата: 2026-10-07 23:03:57 +0300.
- Source version в `RoslynMcpServer.csproj`: `1.5.5` /
  `AssemblyVersion` `1.5.5.0` / `FileVersion` `1.5.5.0`.
  Roslyn package pin в том же файле: `5.9.0`
  (`Microsoft.CodeAnalysis.CSharp`, `CSharp.Workspaces`, `CSharp.Features`,
  `Features`, `Workspaces.MSBuild`).
- Running MCP: `1.5.4.0`, binary
  `E:\Devel\RoslynMcpServer\bin\Release\net10.0\win-x64\publish\RoslynMcpServer.exe`,
  modified 2026-10-01T17:41:09.1475207+03:00.
  Process CWD: `C:\Users\VuDZ`. Tool profile: `full`. Registered tools: 54.
  Версия процесса не совпадает с source `1.5.5` и не доказывает идентичность HEAD.
- Файл `global.json` в корне репозитория отсутствует (`Test-Path` = false).
  `load_workspace` сообщил: global.json not found, pinned SDK none,
  resolved SDK directory not resolved.
- CLI `dotnet --version`: `10.0.300`. Установленные SDK:
  `9.0.314`, `10.0.201`, `10.0.300` в `C:\Program Files\dotnet\sdk`.
  Workflow [test-suite.yml](../../../../.github/workflows/test-suite.yml)
  по-прежнему ставит `10.0.300`. Это pin CI, не evaluated SDK уже
  запущенного хоста 1.5.4. Полный SDK/restore closure audit не выполнялся.
- U-001: принят вариант **B** по [H-001/H-002](../../human-decisions.md),
  2026-10-07. Точный перенос XML documentation provider и непубличного
  strong-name state в поддержанный срез не входит. Signing-dependent и
  unknown идут через ordinary load до операции. Emit/signing из hydrated
  compilation вне среза.

### Dirty inventory на момент выбора

`git status --porcelain` до записи этого отчёта:

- modified: `RoslynMcpServer.Tests/WorkspaceLoadCache/DependencyEvidenceRunner.cs`
- untracked: `.agents/skills/prepare-github-release/`,
  `.cursor/rules/roslyn-mcp.mdc`, `.zcodeignore`, `iotester.py`, `scripts/`,
  и новые файлы `RoslynMcpServer.Tests/WorkspaceLoadCache/`
  (`DependencyFrameworkSelection.cs`, `DependencyGraphDiscovery.cs`,
  `DependencyInputProbe.cs`, `DependencyPartialProofTests.cs`,
  `IndependentWorkspaceOracle.cs`, `IndependentWorkspaceOracleTests.cs`,
  `WorkspaceObservationReader.cs`, models `Dependency*`, `Oracle*`,
  `ScenarioOutcome`, `WorkspaceObservation`).

Эти файлы задача не редактировала и не принимает как доказанный ordinary
capture. Незакоммиченный oracle прямо оставляет positive stages not-run.
Повторный dependency-closure research не выполнялся.

Этот отчёт сам становится новым untracked файлом после записи.

## Наблюдение хоста

Один явный `load_workspace`:

- workspace path: `E:\Devel\RoslynMcpServer\RoslynMcpServer.sln`
- configuration, platform, targetFramework, buildArgs: не передавались
- host: MSBuild Configuration / Platform / TargetFramework =
  SDK/workspace default; BuildArgs none
- projects loaded: 5; restore assets ok 5/5
- `list_projects`: у каждого проекта один TFM `net10.0`

| Project | Path | Output | Project references |
|---|---|---|---|
| RoslynMcpServer | `RoslynMcpServer.csproj` | Exe | none |
| RoslynMcpPlugin | `samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj` | unknown | RoslynMcpServer |
| RoslynMcpServer.LifecycleTestHost | `RoslynMcpServer.LifecycleTestHost/RoslynMcpServer.LifecycleTestHost.csproj` | Exe | RoslynMcpServer |
| DeadlockWitnessHost | `RoslynMcpServer.Tests/SourceStructure/DeadlockWitnessHost/DeadlockWitnessHost.csproj` | Exe | none |
| RoslynMcpServer.Tests | `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj` | unknown | RoslynMcpServer, LifecycleTestHost, DeadlockWitnessHost |

Пять csproj объявляют `<TargetFramework>net10.0</TargetFramework>`, не
`TargetFrameworks`. Это наблюдение solution-load. Отдельный
`OpenProject` выбранного контроля здесь не выполнялся.
Evaluated `SignAssembly` не измерялся: в csproj контроля и в
`Directory.Build.props` свойства нет. Отсутствие объявления не является
доказательством signing-independent profile (H-002).

## Seams текущего пути

Production load/prepare/write/input-map, сверено по символам загруженного
workspace. Исторический baseline audit не подменяет этот список.

- **Load.** `SolutionManager.LoadAsync` вызывает `LoadAndPrepareAsync`
  с `shadowCopyInSolutionAnalyzers: false`. Эффективные
  Configuration/Platform/TFM: переданный аргумент, иначе уже загруженное
  значение, иначе `RoslynMcpFileSettings`. `LoadCoreAsync` создаёт
  `MSBuildWorkspace` через `MsBuildWorkspaceProperties.Create`; пустой
  набор свойств идёт в `MSBuildWorkspace.Create(MsBuildHostServices)`
  без словаря. Открытие делает
  `AnalyzerProvenanceCaptureService.OpenAndCaptureAsync`: `.sln`/`.slnx`
  → `OpenSolutionAsync`, `.csproj` → `OpenProjectAsync`, logger —
  исходный `BinaryLogger`.
- **Prepare.** Ветка overlay (`shadowCopyInSolutionAnalyzers: true`)
  после физического open вызывает
  `PrepareInSolutionAnalyzerReferencesUnderLock`. Непригодный provenance
  capture оставляет publication unavailable. Наблюдение этой задачи шло
  с `shadowCopy` false, поэтому overlay prepare не выполнялся.
  `AnalyzerExecutionGate.EvaluateInSolutionAnalyzers` вызывается на
  обычном open.
- **Input map.** После open `StartDiskWatcherUnderLock` строит карту
  `WorkspaceInputMapBuilder.Build` по уже загруженному `Solution`
  (instances, occurrences, regions, watchers, coverage) и запускает
  `WorkspaceDiskWatcherStarter.Start`. Builder не разбирает item groups
  и не стартует watchers.
- **Write.** `WorkspaceWriteBoundary.Preflight` стоит до apply.
  Единственный production вызов `Workspace.TryApplyChanges` —
  `SolutionManager.TryApplyWorkspaceChanges`, строка 1541,
  `workspace.TryApplyChanges(cleaned)`.
  `WorkspaceDocumentDiskSync` обновляет тексты известных документов.
  `WorkspaceNonCSharpDiskSync` меняет additional/analyzer-config text на
  опубликованном snapshot и `TryApplyChanges` для этого не вызывает.
- **Experiment.** Принятый hydrate host и codec лежат в
  `RoslynMcpServer.Tests/WorkspaceLoadCache/`. Production от этой сборки
  не зависит. Повторное использование writer в эксперименте идёт через
  существующие `WorkspaceWriteBoundary.Preflight` и persistence, без
  переноса isolated apply в production.

## Выбранный контроль

Маленький реальный неизменённый проект для bounded S-001 и последующего
round-trip:

`E:\Devel\RoslynMcpServer\samples\RoslynMcpPlugin\RoslynMcpPlugin.csproj`

Почему он:

- один объявленный TFM `net10.0`; в solution-load одна instance и одна
  project reference на `RoslynMcpServer`;
- в csproj объявлены два `PackageReference`: `ModelContextProtocol` 2.2.0
  (`PrivateAssets=all`, `ExcludeAssets=runtime`) и `Newtonsoft.Json` 13.0.4
  (`PrivateAssets=all`). Это объявления, не список `MetadataReferences`;
- фактические compile references этих пакетов сняты отдельно ниже;
- project reference в csproj: `RoslynMcpServer.csproj` с `Private=false` и
  `ExcludeAssets=runtime`. В Roslyn-графе ребро подтверждено и
  `list_projects`, и чтением `Project.ProjectReferences`.
- два ручных исходника: `SamplePlugin.cs`, `SampleTools.cs`;
- заранее названный символ уже разрешается в этом solution-load.

Ожидаемый символ: `RoslynMcpPlugin.SamplePlugin`.
`find_symbol_definition("SamplePlugin")` на загруженном решении вернул
ровно одно объявление: `samples/RoslynMcpPlugin/SamplePlugin.cs`, строка 5.
Это проверка идентичности символа в текущем solution snapshot, не результат
capture, hydrate или isolated open проекта.

Объявленные свойства контроля, не evaluated instance values:
`Nullable=enable`, `ImplicitUsings=enable`,
`CopyLocalLockFileAssemblies=true`, `PreserveCompilationContext=true`,
`PreserveCompilationReferences=false`,
`TrimDepsJsonLibrariesWithoutAssets=false`.
Target `WritePluginManifest` пишет `plugin.json` в output после
`CopyFilesToOutputDirectory`. Исходный `plugin.json` имеет
`CopyToOutputDirectory=Never`.

### Фактические metadata references

Способ: отдельный процесс, `MSBuildLocator.RegisterMSBuildPath` на
`C:\Program Files\dotnet\sdk\10.0.300`, затем
`MSBuildWorkspace.Create` с MEF host и `OpenSolutionAsync` того же
`RoslynMcpServer.sln` без словаря global properties. Публичные MCP tools
не печатают `Project.MetadataReferences` по проекту, поэтому это не объект
workspace внутри процесса MCP 1.5.4. Проект выбран по `Name == RoslynMcpPlugin`.

Наблюдение этого open, workspace failures: 0:

- `Project.FilePath`: `E:\Devel\RoslynMcpServer\samples\RoslynMcpPlugin\RoslynMcpPlugin.csproj`
- `AssemblyName`: `RoslynMcpPlugin`
- `Project.ProjectReferences`: `RoslynMcpServer`
- `Project.MetadataReferences.Count`: 226

Остальные 223 reference в этот inventory не входят: это не closure audit.
Совпадения с объявленными пакетами, все `PortableExecutableReference`,
`Kind=Assembly`, aliases нет, `EmbedInteropTypes=false`.
Identity снята через `AssemblyName.GetAssemblyName` с того же `FilePath`:

| DLL | Assembly identity | FilePath |
|---|---|---|
| `ModelContextProtocol.dll` | `ModelContextProtocol, Version=2.2.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51` | `C:\Users\VuDZ\.nuget\packages\modelcontextprotocol\2.2.0\lib\net10.0\ModelContextProtocol.dll` |
| `ModelContextProtocol.Core.dll` | `ModelContextProtocol.Core, Version=2.2.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51` | `C:\Users\VuDZ\.nuget\packages\modelcontextprotocol.core\2.2.0\lib\net10.0\ModelContextProtocol.Core.dll` |
| `Newtonsoft.Json.dll` | `Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed` | `C:\Users\VuDZ\.nuget\packages\newtonsoft.json\13.0.4\lib\net6.0\Newtonsoft.Json.dll` |

`ModelContextProtocol.Core` есть в metadata references и отсутствует среди
прямых `PackageReference`. Package `Newtonsoft.Json` 13.0.4 даёт assembly
version `13.0.0.0`. PublicKeyToken здесь — identity файла, не вывод о
signing-independent profile и не разрешение emit.

Начальный срез, который S-001 может проверять:

- один project file и его единственная loaded instance `net10.0`;
- ручные documents `SamplePlugin.cs` и `SampleTools.cs`;
- направленный project reference на `RoslynMcpServer`;
- три metadata references таблицы выше, а не текст двух `PackageReference`;
- публично читаемые parse/compilation options и reference properties
  в границе H-001/H-002.

Вне этого среза, без отдельного исследования: весь `RoslynMcpServer.sln`,
внутреннее состояние проекта `RoslynMcpServer`, Tests, publish/trimmer
настройки сервера, session/ProjectIds, shadow paths, исходный и
generated text как переносимый payload, XML text equality, скрытое
strong-name state, emit/signing.

Открытие одного plugin csproj загрузит referenced `RoslynMcpServer`.
Сохранение направленного ребра входит в срез. Пригодность обязательного
состояния самого сервера S-001 ещё должен показать; этот отчёт её не
утверждает.

Крупный внешний корпус `E:\Devel\roslyn-mcp-bench\clones\btcpay-web`
на диске есть. Он не выбран. Прежние v2 admission gates на него не
переносятся и заново не измерялись.

Отклонённые кандидаты внутри репозитория:

- `DeadlockWitnessHost` — в solution-load project references отсутствуют;
  csproj прямо ограничивается framework.
- `LifecycleTestHost` — хост analyzer/generator lifecycle; см. отдельный
  раздел ниже.
- `RoslynMcpServer.csproj` — продукт без собственных project references
  и с широким package surface; он остаётся целью project reference
  контроля, а не самим маленьким срезом.
- `RoslynMcpServer.Tests` — тянет сервер, lifecycle host и
  DeadlockWitnessHost.

## Generator-dependent scope

Поддержка отсутствует и в положительный срез не входит.

- `RoslynMcpServer.LifecycleTestHost.SourceGeneratorOracle` читает
  generated type `GeneratedMarker` и поле `Version` через
  `Project.GetCompilationAsync` и `GetSourceGeneratedDocumentsAsync`.
  E0 не делает generator output переносимым Document. Проект
  `LifecycleTestHost` контролем не является. Статус: **not-supported**.
- `SampleTools` помечен `[McpServerTool]`; регистрация идёт через
  `SamplePlugin.Register` → `AddToolsFrom<SampleTools>`.
  Текст `samples/RoslynMcpPlugin/obj/project.assets.json` не содержит
  подстрок analyzer/generator. Это наблюдение файла assets, не инвентарь
  `AnalyzerReferences` загруженной compilation. S-001 читает фактические
  ссылки. Generator execution этого контроля **not-run**.
- `WritePluginManifest` — custom target. Неизвестные дополнительные
  inputs такого target серия не считает поддержанными. Состав этих
  inputs здесь не исследовался.

## Прежний hydrate experiment

Источник: [task-01-hydrate-host.md](../../../backlog/workspace-load-cache/epoch-0-feasibility/evidence/task-01-hydrate-host.md).
Поиск отсутствующих getters заново не делался. Roslyn pin источника
по-прежнему 5.9.0.

Применимо как опыт synthetic fixture, не как ordinary capture этого
контроля:

- encode/decode в новый `AdhocWorkspace` сохраняет на fixture известные
  options, project edges, metadata aliases и linked memberships;
- writer fixture использует production `WorkspaceWriteBoundary` и
  persistence без рефакторинга production;
- encoding fixture сохраняет preamble/payload для UTF-8 без BOM, UTF-8
  с BOM и UTF-16 LE с BOM;
- stale session/base, unknown analyzer и конфликтующие linked edits
  отвергаются до записи;
- codec проверяет closed schema и предел 16 MiB.

Применимые representability gaps, уже известные:

- `DesktopStrongNameProvider.KeyFileSearchPaths` не имеет публичного
  getter в Roslyn 5.9.0. При U-001=B точное восстановление этого
  состояния не требуется для подтверждённого signing-independent scope.
  Для выбранного контроля такой scope ещё не подтверждён. Private
  reflection и подстановка null/default запрещены.
- У stock `PortableExecutableReference` нет публичного getter
  `DocumentationProvider`. По H-001 это само по себе не делает reference
  unsupported. Равенство внешнего XML текста не является acceptance
  requirement. Комментарии исходников загруженных проектов сохраняются.
- Несколько same-path inner-TFM instances из ordinary inventory
  однозначно не связываются; ambiguity отвергается. Выбранный контроль
  объявляет один TFM. Это не доказательство inner-TFM binding при
  будущем isolated open.
- Обычный MSBuild loader принимает logger только точного типа
  `BinaryLogger`. Ограничение относится к DTB-наблюдению, не к полю
  snapshot.
- Положительный ordinary capture и generator semantic equivalence тем
  экспериментом не доказаны. Успех synthetic fixture их не закрывает.

Новых исследований по этим gaps нет. Если S-001 получит negative или
partial verdict, dependent codec/hydrate остаются заблокированными с
причиной; references контроля не удаляются.
