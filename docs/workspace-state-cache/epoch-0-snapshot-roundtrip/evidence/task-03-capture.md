# E0/task-03 — Capture ordinary workspace

Дата: **2026-10-08**, Europe/Moscow. Исполнитель: Grok 4.7.
Статус реализации: **accepted, 2026-10-08**, round 1, ordinary capture schema v1 с уже загруженного контроля. [Независимое ревью](task-03-review-round-1.md) и [финальная validation](task-03-validation.md) завершены; task/index синхронизированы координатором. Приёмка эпохи не присвоена.
Коммит не выполнялся. Version / AssemblyVersion / FileVersion не менялись (остаётся **1.5.5**).

Принятые входы: [schema v1](task-02-schema.md), [S-001](task-08-capability.md),
[H-001/H-002](../../human-decisions.md), [P-001](../../change-ledger.md#p-001),
[P-010](../../change-ledger.md#p-010). Codec, store, hydrate и `SolutionManager` не менялись.

## Adapter

Production capture живёт в `Services/Workspace/StateCache/`, не в Tests:

- `OrdinaryWorkspaceCapture` — снимок schema v1 с уже загруженного ordinary solution и его
  pre-overlay `ProjectInfo`. Workspace он не открывает.
- `SnapshotOptionFacts` — публичные parse/compilation facts и finite `/debug+` witness.
- `SnapshotSigningProfile` — допуск H-002 до появления snapshot. Это не runtime route и не лог.
- `SnapshotOccurrenceBinder` — перенос одного поколения `WorkspaceInputMap` на portable project keys.
  Guid поколения в DTO не пишется.
- `SnapshotDocumentBinding` — связь live document id с portable document на время capture.
  Id в snapshot не попадает.

Профиль **`s001-single-target-debug-base-query-h001-h002-v1`**, schema **1**.
Обычная загрузка — публичный `MSBuildProjectLoader` с absent request/globals.
`loadGraphComplete` у карты остаётся false: это загруженный public graph, не closure.

## Контроль

HEAD репозитория на старте правок: `f29bf2cbfb68d02ff672a26c4cbae5fc1b6eb37d`.
Новые capture-файлы поверх этого commit ещё не закоммичены. Исходники и csproj контроля
`samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj` не редактировались. Signing flags и references
ради pass не менялись. Загруженный server включает новые `.cs` adapter-а, поэтому source count
больше исторического task-08/task-02.

Наблюдение: `TestResults/task-03/capture-observation.json` (локальный, в git не входит).
SHA256 indented JSON: `61754AD1A14CD28C81943282196E6E564229702503FB7A4C156E6EE9D7C240B4`.
Абсолютные пути и checksums не являются proof среды. Roslyn **5.9.0.0**.
Оба проекта: **Debug / AnyCPU / net10.0**. Raw request и loader globals absent.

| Проект | Source | Additional | Config | Metadata | Analyzers | Project refs |
|---|---:|---:|---:|---:|---:|---:|
| RoslynMcpPlugin | 5 | 0 | 2 | 226 | 13 | 1 (на server) |
| RoslynMcpServer | 225 | 0 | 2 | 236 | 13 | 0 |

Occurrences: **224 UserInput**, **6 Unknown**, **4 AnalyzerConfig**, **462 MetadataDependency**,
**26 AnalyzerDependency**, **4 Output**, **2 EvaluationInput**. Generated в этом контроле нет.
References не сокращались.

EvaluationInput — только два загруженных csproj. `Directory.Build.props` в snapshot нет:
walk-up, imports, restore и NuGet closure этим adapter не объявляются manifest.
Путь под `obj` и суффикс вроде `AssemblyInfo.cs` / `.g.cs` не назначают producer.
Unknown остаётся Unknown, producer у него пустой. Generated producer берётся только из
public `IsGenerated`, source `public-document-generated-flag`. Metadata producer есть только
при единственном output и направленном project reference; без ребра или при двух output
producer пустой.

H-001: у каждого metadata reference policy `H-001-external-xml-excluded`. Aliases и
`EmbedInteropTypes` сохранены, включая stock reference (Newtonsoft.Json или System.Runtime
на каждом проекте). Комментарий загруженного `WorkspaceSnapshot.cs` после capture совпал
побайтно и в JSON snapshot не попал. Parse `DocumentationMode` взят с live options.
H-002: policy `H-002-signing-independent-empty-search-paths`. Unsigned `InternalsVisibleTo`
не исключает проект. Emit/signing из hydrated compilation не делались.

Analyzer identity — original path из ordinary descriptor, source
`ordinary-pre-overlay-public-loader-descriptor`. Без witness capture возвращает
`original-analyzer-paths-unproven`. Путь под shadow root или с сегментом
`RoslynMcpServer.AnalyzerShadowCopy` даёт `analyzer-identity-from-shadow-path`, без подбора
«настоящего» пути.

## Неподдержанные случаи

Явный отказ, snapshot не создаётся, ordinary load этим отказом не блокируется:

- `original-analyzer-paths-unproven`
- `analyzer-identity-from-shadow-path`
- `ambiguous-project-instance`
- `signing-dependent` — PublicSign, CryptoKeyFile, AssemblyKeyFile, signed InternalsVisibleTo
- `signing-state-unknown` — неразрешённый attribute; неизвестный StrongNameProvider в том же profile
- `generator-dependent-scope-unsupported` — если есть source-generated documents.
  У этого контроля их нет, отдельный generator fixture не запускался
- `outside-debug-option-witness-profile` — evaluated Configuration не Debug.
  Положительный путь наблюдён; отдельный Release load не запускался
- `evaluated-instance-unavailable-or-multitarget`
- `non-file-metadata-unsupported`, `non-file-analyzer-unsupported`
- `parse-options-descriptor-live-mismatch` — live parse options, включая preprocessor symbols, разошлись с descriptor
- `document-descriptor-live-mismatch` — live name, folders или SourceCodeKind разошлись с descriptor
- `document-generated-flag-unproven` — public descriptor flag и live flag расходятся,
  либо descriptor говорит generated, а live flag прочитать нельзя
- отказ option witness (`compilation-option-witness-unavailable`,
  `metadata-resolver-factory-witness-unavailable`, `custom-descriptor-tree-options-unsupported`
  и соседние unknown resolver/comparer/policy). На реальном Debug capture witness прошёл:
  восстановленные public options совпали с live `Equals`

Walk-up и прочие MSBuild categories, которых нет на public graph, не копируются и не
помечаются UserInput. Это объявленная граница, не silent drop загруженных references.

## Проверки

Roslyn MCP `run_specific_test`, `className=OrdinaryWorkspaceCaptureTests`,
`configuration=Release`, `noRestore=true`, `timeoutSeconds=360`.
Последний результат после закрытия расхождения descriptor/live: **13 tests / 13 passed / 0 failed / 0 skipped**.
Theory `Signing_dependent_state_is_unsupported` входит четырьмя случаями.
`Live_descriptor_divergence_is_unsupported` на том же контроле меняет live preprocessor symbols,
имя, folders и SourceCodeKind и ожидает отказ, а не старые значения.

SourceStructure и полный `Category!=AnalyzerLifecycle` выполнены координатором после
независимого ревью: см. [validation](task-03-validation.md). AnalyzerLifecycle не требуется:
load/prepare/publication, shadow-copy lifecycle и `SolutionManager` не менялись. Capture
только читает уже загруженный graph и отказывается от shadow path.
