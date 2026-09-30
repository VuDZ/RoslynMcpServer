# Раскладка тестов по функциональным областям

Статус: **выполнено**.

Основание: [code-style.md](../../code-style.md), раздел «Applying the rules to existing code». Стиль требует применять правила при изменении кода и не смешивать массовое форматирование со сменой структуры. Эта серия — только структура каталогов `RoslynMcpServer.Tests`. Порядок членов, длина методов и переносы строк сюда не входят.

## Зачем

В корне тестового проекта лежит 86 файлов в одном пространстве имён `RoslynMcpServer.Tests`. Уже выделены две области, их эта серия не переносит: `AnalyzerLifecycle/` (`RoslynMcpServer.Tests.AnalyzerLifecycle`) и `SourceStructure/` (`RoslynMcpServer.Tests.SourceStructure`, включая проект `DeadlockWitnessHost`). Остальные тесты названы по типу прод-кода, но лежат плоско, поэтому соседство не показывает область.

Каталоги повторяют функциональные области продукта, а не слои `Services` / `Tools` / `Diagnostics`. Парсер вывода сборки остаётся рядом со сборкой, а не в общей папке «все парсеры».

## Что серия не делает

- Не переименовывает тестовые классы. Шарды CI режут только `Category!=AnalyzerLifecycle` и `Category=AnalyzerLifecycle`. Имени класса в фильтре нет; серия всё равно его не меняет.
- Не переносит файлы из `AnalyzerLifecycle/` и `SourceStructure/` и не кладёт юнит-тесты анализаторов в `AnalyzerLifecycle/`. Коллекция xUnit с `DisableParallelization` задаётся атрибутом, не папкой; общая папка с хостом lifecycle всё равно читается как «эти тесты сериализованы».
- Не меняет прод-код, описания MCP-инструментов, версию в csproj и фильтры `.github/workflows/test-suite.yml`.
- Не разносит несколько типов из одного файла по файлам. Это отдельный шаг после переноса: иначе diff смешивает `git mv` с новыми файлами.
- Не выравнивает порядок членов и не прогоняет форматтер по нетронутым строкам.

## Пространства имён

Каталог и пространство имён совпадают, как уже сделано для `AnalyzerLifecycle` и `SourceStructure`.

| Каталог | Пространство имён |
|---|---|
| `Support/` | `RoslynMcpServer.Tests.Support` |
| остальные новые каталоги | `RoslynMcpServer.Tests.<Каталог>` |
| `AnalyzerLifecycle/` | без изменений |
| `SourceStructure/` | без изменений |

`internal` виден всей сборке, но из дочернего пространства имён тип без `using` не находится. Общие фикстуры (`SolutionManagerTestFactory`, `TestEnvironmentLocks`) переезжают в `Support/`. В тестовый проект добавляется один `GlobalUsings.cs`:

```csharp
global using RoslynMcpServer.Tests.Support;
```

Других правок в телах тестов нет: ни перестановок членов, ни новых `using`, кроме одного. `SolutionManagerAnalyzerOverlayTests` берёт `typeof(WorkspaceWriteBoundaryTests)`, а этот тип уезжает в `Workspace`. В том файле добавляется `using RoslynMcpServer.Tests.Workspace;`. SDK-style проект подхватывает `*.cs` рекурсивно, `Compile Include` в csproj нет. `Compile Remove` на `SourceStructure\DeadlockWitnessHost\**` уже есть и остаётся: исходники хоста принадлежат своему проекту.

## Куда класть файл

После серии в корне нет `*Tests.cs`. Там остаются `GlobalUsings.cs`, каталоги `AnalyzerLifecycle/` и `SourceStructure/` (с `DeadlockWitnessHost`). `obj/` и `bin/` — вывод сборки.

### Support

- `SolutionManagerTestFactory.cs`
- `TestEnvironmentLocks.cs`

### Workspace

Загрузка, диск, пути, запись, здоровье workspace.

- `DirectoryBuildPropsReaderTests.cs`
- `MsBuildWorkspacePropertiesTests.cs`
- `ProjectOutputDiagnosticsLoggerTests.cs`
- `SkippedWorkspaceWriteDiskPolicyTests.cs`
- `SolutionConfigurationCatalogTests.cs`
- `SolutionManagerDiskWatcherCallbackTests.cs`
- `SolutionManagerPassedLoadArgsTests.cs`
- `SolutionManagerPathResolutionTests.cs`
- `SolutionManagerWatchRootsTests.cs`
- `SolutionProjectTargetResolverTests.cs`
- `SourceTextEncodingTests.cs`
- `WorkspaceDiagnosticFormatterTests.cs`
- `WorkspaceDiskPathFilterTests.cs`
- `WorkspaceDocumentDiskSyncTests.cs`
- `WorkspaceHealthReporterTests.cs`
- `WorkspaceLoadDiagnosticsReporterTests.cs`
- `WorkspaceLoadGuidanceTests.cs`
- `WorkspaceRootResolverTests.cs`
- `WorkspaceWriteBoundaryTests.cs`
- `WorkspaceWriteEncodingTests.cs`

### Analyzers

Юнит-тесты overlay, shadow и provenance. Не хост lifecycle.

- `AnalyzerLoaderContractTests.cs`
- `AnalyzerProvenanceBindingTests.cs`
- `AnalyzerProvenanceCaptureGateTests.cs`
- `AnalyzerReferenceShadowCopierTests.cs`
- `AnalyzerShadowGenerationPublisherTests.cs`
- `AnalyzerShadowPublicationPlannerTests.cs`
- `SemanticPublicationStateTests.cs`
- `SolutionManagerAnalyzerOverlayTests.cs`
- `VirtualReferenceClassifierTests.cs`
- `VirtualReferenceFilterTests.cs`
- `WorkspaceAnalyzerSanitizerTests.cs`

### Build

`dotnet build`, MSBuild, SDK, общий CLI-runner. Отдельный каталог `Cli` не заводим: runner проверяется вместе со сборкой, три файла ради слоя не окупаются.

- `BuildProgressIntegrationTests.cs`
- `CliProgressTests.cs`
- `DotNetBuildArgumentsTests.cs`
- `DotNetBuildDiagnosticParserTests.cs`
- `DotNetBuildProbeTests.cs`
- `DotNetCliRunnerHangTests.cs`
- `DotNetCliRunnerLocaleTests.cs`
- `DotNetConfigurationArgumentsTests.cs`
- `DotNetHostResolverTests.cs`
- `DotNetSdkEnvironmentTests.cs`
- `MsBuildInstanceSelectorTests.cs`
- `MsBuildLogHighlighterTests.cs`
- `SdkMismatchDiagnosticsTests.cs`

### TestRunner

- `DotNetTestArgumentsTests.cs`
- `TestAssemblyPathResolverTests.cs`
- `TestDiscoveryHelperTests.cs`
- `TestFilterHelperTests.cs`
- `TestToolsRunTestByFilterTests.cs`
- `VstestOutputParserTests.cs`

### Navigation

- `CodeSkeletonToolsTests.cs`
- `FileDeclarationCollectorTests.cs`
- `FindImplementationsMultiBaseTests.cs`
- `NavigationOverflowStoreTests.cs`
- `RenameAndCallGraphDisambiguationTests.cs`
- `SourcePositionHelperTests.cs`
- `SymbolDeclarationResolverTests.cs`

### Editing

- `PatchMatchHelperTests.cs`
- `ProjectRenameHelperTests.cs`

### Search

- `UtilityToolsSearchCodeTests.cs`
- `UtilityToolsSearchCodeRipgrepTests.cs`
- `UtilityToolsSearchScopeTests.cs`

### Utility

- `GitChangedFilesHelperTests.cs`

### Hosting

- `McpToolActivationTests.cs`
- `McpToolCatalogTests.cs`
- `McpToolGroupEnablementIntegrationTests.cs`
- `McpToolGroupEnablementTests.cs`
- `McpToolHelpTests.cs`

### AssemblyResolve

- `AssemblyReferenceResolverTests.cs`
- `DepsJsonAssemblyPathResolverTests.cs`
- `NuGetFallbackAssemblyResolverTests.cs`

`NuGetFallbackAssemblyResolver` решает путь к сборке, это не аудит пакетов.

### NuGet

- `NuGetAuditReportParserTests.cs`

### Logging

Префикс строки лога (`HarnessSessionId`) лежит рядом с разбором и усечением вывода.

- `DiagnosticReportStoreTests.cs`
- `HarnessSessionIdTests.cs`
- `ProcessOutputExcerptTests.cs`
- `ToolLogAnalyzerTests.cs`
- `TruncatedProcessLogTests.cs`

### Plugins

- `PluginAssemblyLoadTests.cs`
- `PluginDiscoveryTests.cs`
- `PluginRegistrationTests.cs`
- `PluginShadowCopyTests.cs`
- `PluginStartupTests.cs`
- `SamplePluginShapeTests.cs`

### Config

- `RoslynMcpFileSettingsTests.cs`

### AnalyzerLifecycle

Каталог и имена файлов не трогаем.

## Шаги реализации

1. `git mv` по таблицам выше. История файла сохраняется.
2. В каждом перенесённом файле заменить объявление `namespace` на пространство имён каталога. Тела не трогать.
3. Добавить `GlobalUsings.cs` с `global using` на `Support`.
4. Собрать тестовый проект и прогнать шард `Category!=AnalyzerLifecycle`. Шард `Category=AnalyzerLifecycle` не гонять: его файлы не меняются.
5. Убедиться, что корень `RoslynMcpServer.Tests` не содержит `*Tests.cs`.

Порядок членов и «один тип на файл» — следующая серия, после того как этот перенос принят. Сейчас в одном файле сидят лишние типы (не разносить в этом шаге):

- `RoslynMcpFileSettingsTests.cs` — `RoslynMcpJsoncParserTests`, `RoslynMcpFileSettingsTests`, `MsBuildWorkspacePropertiesPassedArgsTests`
- `DiagnosticReportStoreTests.cs` — `DiagnosticReportStoreTests`, `DiagnosticReportAttachmentTests`
- `NavigationOverflowStoreTests.cs` — `NavigationOverflowStoreTests`, `NavigationListingHelperTests`
- `AnalyzerShadowGenerationPublisherTests.cs` — `AnalyzerShadowGenerationPublisherTests`, `AnalyzerShadowPublisherCollection`
- `PluginRegistrationTests.cs` — тестовый класс и фикстуры плагина в том же файле
- `AnalyzerLifecycle/GeneratorConsumerFixture.cs` — `OutputPathMode` и фикстура; каталог серия не трогает

Вложенные private-типы стиль разрешает и выносить не требует. `LifecycleEnvironment` и `AnalyzerLifecycleTheoryAttribute` уже лежат в своих файлах.
