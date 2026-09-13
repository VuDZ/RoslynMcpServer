# E5-S1 — результаты исследования provenance metadata

Дата: 2026-09-12. Статус: **исследование выполнено; matcher и rollout не
изменялись, приёмка E5-S4 не выставлялась**.

Политика U-ARB-01 после этого отчёта: **capture на load**; запас Alt-2/Alt-3
если канал не взлетит. Норматив выбора — [UNRESOLVED-v2.md](UNRESOLVED-v2.md).

Окружение измерения: Windows 10 (build 26200), .NET SDK/MSBuild 10.0.204,
Roslyn `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0, x64
`C:\Program Files\dotnet`.

## Вердикт

Связь имеет разные ответы на двух границах:

- в публичной модели Roslyn 5.9.0 связи
  `AnalyzerReference -> ProjectId/project file` **нет**;
- в результате стандартного SDK design-time target chain связь
  `@(Analyzer) -> исходный ProjectReference` **есть**:
  `%(Analyzer.MSBuildSourceProjectFile)` содержит абсолютный путь
  referenced `.csproj`;
- обычная MSBuild evaluation без выполнения targets видит точный
  `@(ProjectReference)` и его `FullPath`, но ещё не создаёт соответствующий
  project-produced `@(Analyzer)`. Для получения объединённой связи нужен
  `ResolveProjectReferences`/`GetTargetPath` либо захват эквивалентного результата
  уже выполняемой design-time загрузки;
- Roslyn BuildHost сворачивает evaluated `@(Analyzer)` в compiler argument
  `/analyzer:<path>`. Metadata `MSBuildSourceProjectFile`, выбранный TFM и исходный
  item после этой границы теряются.

Это факт о доступной metadata, а не выбор missing/inaccessible policy. Filename,
даже совпадающий с уникальным `AssemblyName`, остаётся только кандидатом.

## Что видно в чистой MSBuild evaluation

На fixture эпохи 1 запрос evaluated items до выполнения target показал:

```text
ProjectReference.Identity = ..\Generator\Generator.csproj
ProjectReference.FullPath = <root>\Generator\Generator.csproj
OutputItemType = Analyzer
ReferenceOutputAssembly = false
ReferenceSourceTarget = ProjectReference
Analyzer = []
```

Доступны также authored/custom metadata и built-in item metadata
`OriginalItemSpec`/`DefiningProjectFullPath`. Последний указывает проект,
**объявивший** item, а не referenced analyzer project; для источника нужен
`ProjectReference.FullPath`.

Pure evaluation не даёт готовой строки
`analyzer output -> source project`: `@(Analyzer)` из ProjectReference появляется
только после target execution. Вычисленный `TargetPath` referenced проекта можно
получить отдельной evaluation, но это ещё не metadata исходного analyzer item и
не заменяет TFM/configuration negotiation на ребре.

## Что даёт design-time target result

Стандартная цепочка SDK/MSBuild:

```text
AssignProjectConfiguration
  -> _SplitProjectReferencesByFileExistence
  -> _GetProjectReferenceTargetFrameworkProperties
  -> _GetProjectReferencePlatformProperties
  -> PrepareProjectReferences
  -> ResolveProjectReferences
  -> nested GetTargetFrameworks/GetTargetPath
```

`ResolveProjectReferences` направляет output вложенного `MSBuild` task в item с
именем из `%(ProjectReference.OutputItemType)`, то есть в `@(Analyzer)`.
Измеренный project-produced item содержал:

```text
Analyzer.Identity = <root>\Generator\bin\Debug\netstandard2.0\Generator.dll
MSBuildSourceProjectFile = <root>\Generator\Generator.csproj
ReferenceSourceTarget = ProjectReference
ReferenceOutputAssembly = false
OutputItemType = Analyzer
OriginalItemSpec = ..\Generator\Generator.csproj
NearestTargetFramework = netstandard2.0
```

`MSBuildSourceProjectFile` — точная связь с project file. Она не зависит от
существования DLL: на свежем multi-target probe target вернул
`bin\Debug\net10.0\Generator.dll` с пустыми file timestamps, хотя output не
строился и на диске отсутствовал.

`MSBuildSourceTargetName` наблюдался как `GetTargetFrameworks`, потому что metadata
переносится между промежуточными items. Его нельзя использовать как доказательство
финального шага `GetTargetPath`; provenance даёт именно
`MSBuildSourceProjectFile`.

## Что остаётся в Roslyn 5.9.0

На solution fixture эпохи 1 Roslyn загрузил два проекта, `Consumer` и `Generator`,
но graph показал:

```text
Consumer -> (none)
Generator -> (none)
```

При `ReferenceOutputAssembly=false` loader загружает referenced project для
discovery и не добавляет обычный Roslyn `ProjectReference`. Публичный
`ProjectReference` имеет только `ProjectId`, `Aliases` и `EmbedInteropTypes`;
`OutputItemType` в нём отсутствует.

У Consumer остался один path-based `AnalyzerReference`:

```text
Display = Generator
FullPath = <root>\Generator\bin\Debug\netstandard2.0\Generator.dll
```

Публичный `AnalyzerReference` предоставляет `FullPath`, `Display`, `Id` и методы
получения analyzers/generators. Project path, `ProjectId` и MSBuild item metadata
не предоставляются. Декомпилированный `ResolveAnalyzerReferences` берёт только
`CommandLineAnalyzerReference.FilePath`, создаёт references и выполняет
`Distinct(AnalyzerReferencePathComparer.Instance)`.

У отдельно загруженного Generator доступны `FilePath`, `AssemblyName`,
`CompilationOutputInfo.AssemblyPath` и `OutputFilePath`. Это данные кандидата, но
сами по себе они не связывают конкретный consumer analyzer item с Generator.

## Configuration и TFM

### Configuration

Одна и та же solution fixture была загружена с SDK default и с
`Configuration=Release`:

```text
default: Consumer analyzer = Generator\bin\Debug\netstandard2.0\Generator.dll
         Generator CompilationOutputInfo = Generator\obj\Debug\netstandard2.0\Generator.dll
Release: Consumer analyzer = Generator\bin\Release\netstandard2.0\Generator.dll
         Generator CompilationOutputInfo = Generator\obj\Release\netstandard2.0\Generator.dll
```

В обоих случаях Roslyn graph не получил project-reference edge. Source project
identity в MSBuild остаётся тем же, но resolved path и loaded project output
зависят от Configuration.

Configuration нельзя восстанавливать только из parent global property. При
standalone evaluation Consumer с `Configuration=Release` ребро имело пустой
`SetConfiguration` и `GlobalPropertiesToRemove=Configuration;Platform`, поэтому
nested Generator остался Debug. В solution evaluation configuration mapping дал
Release. Следовательно, устойчивый ключ должен учитывать фактические effective
global properties/solution mapping ребра, а не предполагать наследование
Configuration.

### TargetFramework

На probe `Generator` с `TargetFrameworks=netstandard2.0;net10.0` стандартная
negotiation дала:

```text
Consumer net10.0:
  source = Generator.csproj
  NearestTargetFramework = net10.0
  SetTargetFramework = TargetFramework=net10.0
  analyzer path = bin\Debug\net10.0\Generator.dll

Consumer netstandard2.0:
  source = Generator.csproj
  NearestTargetFramework = netstandard2.0
  SetTargetFramework = TargetFramework=netstandard2.0
  analyzer path = bin\Debug\netstandard2.0\Generator.dll
```

Путь source project стабилен, выбранный inner TFM — нет и должен входить в
идентичность связи. Metadata `NearestTargetFramework`/`SetTargetFramework` либо
global properties соответствующего `ProjectGraphNode` позволяют различить
измеренные inner builds. Одного `MSBuildSourceProjectFile` недостаточно, если
один project file загружен несколькими inner projects. Повторный разбор
unevaluated `TargetFrameworks` не нужен и не доказывает выбранный inner TFM.

Измерение относится к стандартным SDK targets 10.0.204. Custom target,
самостоятельно создающий `@(Analyzer)` без `MSBuild` project-reference protocol,
может не иметь `MSBuildSourceProjectFile`; отсутствие metadata не доказывает
foreign origin.

## Цена и lifecycle evaluation

Для маленькой fixture из Consumer + Generator выполнено по пять отдельных
x64 `dotnet msbuild` процессов с тёплым filesystem cache:

| Операция | Wall time, ms | Среднее |
| --- | --- | --- |
| Pure evaluation + `GetItems(ProjectReference)` | 704.5, 756.8, 674.9, 803.1, 830.5 | 753.9 |
| Design-time `ResolveProjectReferences` + `GetItems(Analyzer)` | 928.5, 1057.6, 845.2, 843.3, 883.3 | 911.6 |

Числа включают запуск отдельного `dotnet`/MSBuild процесса и не являются SLA или
оценкой большого solution. Они показывают, что target-level provenance не
является бесплатным lookup: выполняются evaluation referenced projects,
TFM/platform negotiation и nested `GetTargetPath`. Compilation/build analyzer
project не выполнялись (`DesignTimeBuild=true`,
`BuildProjectReferences=false`, `SkipCompilerExecution=true`).

`MSBuildWorkspace` уже платит за design-time project load, чтобы получить compiler
arguments. Отдельный проход ради provenance дублирует часть этой работы; цена
масштабируется по уникальным `(project path, effective global properties)` и
inner TFM nodes. `ProjectGraph` также рекурсивно evaluates такие nodes; его
`ConstructionMetrics` может дать `ConstructionTime`, `NodeCount` и `EdgeCount`,
но публичное API не отдаёт item metadata прямо на edge — его нужно читать из
source `ProjectInstance`.

Входы provenance snapshot:

- solution/project path и solution configuration mapping;
- `Configuration`, `Platform`, `TargetFramework` workspace load;
- edge metadata `SetConfiguration`, `SetPlatform`, `SetTargetFramework`,
  `GlobalPropertiesToRemove`, `UndefineProperties` и additional properties;
- evaluated `ProjectReference` items и imported SDK/props/targets;
- выбранный SDK/MSBuild toolset и фактически загруженные inner projects.

Считать snapshot допустимо только:

1. при workspace load;
2. при явной смене/refresh project graph после `.csproj`, `.sln/.slnx`,
   `Directory.Build.props/targets`, `Directory.Packages.props`, `global.json`
   либо смены load global properties/toolset.

Не считать при semantic query, document edit или сохранении обычного `.cs`.

## Foreign same-name

Metadata-only probe добавил missing explicit analyzer
`..\foreign\Generator.dll` рядом с project reference на multi-target
`Generator.csproj`. Оба item имели filename `Generator.dll`; build и execution
не выполнялись:

```text
explicit foreign:
  Analyzer = ..\foreign\Generator.dll
  MSBuildSourceProjectFile = (empty)

project-produced:
  Analyzer = <root>\Generator\bin\Debug\net10.0\Generator.dll
  MSBuildSourceProjectFile = <root>\Generator\Generator.csproj
  NearestTargetFramework = net10.0
```

Таким образом, на MSBuild target-result boundary same-name foreign item и
project-produced item различимы без filename heuristic. Пустая metadata сама по
себе не является общим доказательством foreign: custom targets могут её не
проставить.

Lifecycle tests эпохи 1, matcher и assertions не изменялись и не перегонялись.
Существующий oracle по-прежнему читает exact
`GeneratedMarker.Version` через `IFieldSymbol.ConstantValue`; записанный результат
обоих foreign cases — `no-constant`, а не выбранный маркер `V1`/`FOREIGN`.
E5-S1 не переинтерпретирует этот результат как execution identity и не ставит
E5-S4 acceptance.

## Проверенные источники

- `.NET SDK 10.0.204\Microsoft.Common.CurrentVersion.targets`:
  `PrepareProjectReferences`, `ResolveProjectReferences`, `GetTargetPath`,
  `TargetPathWithTargetPlatformMoniker`.
- `.NET SDK 10.0.204\Roslyn\Microsoft.CSharp.Core.targets`: передача
  `@(Analyzer)` в `Csc.Analyzers`.
- Roslyn 5.9.0 decompile:
  `MSBuildProjectLoader.Worker.CreateProjectInfoAsync`,
  `ResolveReferencesAsync`, `ResolveAnalyzerReferences`.
- Публичные Roslyn 5.9.0 types: `ProjectReference`, `AnalyzerReference`,
  `Project`, `CompilationOutputInfo`.
- Fixture и oracle:
  `GeneratorConsumerFixture.cs`, `SourceGeneratorOracle.cs`,
  `Epoch1LifecycleMatrixTests.cs`, `epoch-1-results.md`.
- Runtime measurement: `load_workspace` default/Release с
  `logProjectOutputDiagnostics=true`, `get_project_graph`, а также
  no-build `dotnet msbuild` с `-getItem` и design-time
  `ResolveProjectReferences`.

## Ответы на evidence gaps U-ARB-01

1. **Какие metadata есть:** в Roslyn — path-only reference без project origin; в
   pure MSBuild evaluation — точный `ProjectReference.FullPath`; после design-time
   `ResolveProjectReferences` — точный
   `Analyzer.MSBuildSourceProjectFile` и TFM negotiation metadata.
2. **Стабильность Configuration/TFM:** source project file стабилен; output и
   выбранный inner project меняются с effective configuration/TFM. Связь пригодна
   только вместе с effective globals/selected inner TFM, а не по project path
   в одиночку.
3. **Цена/lifecycle:** target-level проход дороже pure evaluation и рекурсивно
   evaluates referenced nodes; собирать только на load/явной смене графа, не на
   semantic path.
4. **Foreign same-name:** standard project-produced item имеет exact
   `MSBuildSourceProjectFile`, explicit missing same-name item — нет. Это
   измеренная различимость metadata; execution acceptance и missing/inaccessible
   policy данным исследованием не выбирались.
