# E0/task-02 — Переносимая schema и живой потребитель

Дата: **2026-10-08**, Europe/Moscow. Исполнитель: Codex (GPT-6; точный runtime model ID недоступен).
Статус реализации: **accepted, 2026-10-08**, round 1, schema v1 и живой isolated
ordinary consumer для S-001 Debug/base-query профиля. [Независимое ревью](task-02-review-round-1.md)
и [финальная validation / acceptance](task-02-validation.md) завершены; task/index
синхронизированы координатором. Коммит не выполнялся.

Принятые входы: [task-00 / round 2](task-00-review-round-2.md),
[S-001 / round 2](task-08-review-round-2.md), [capability](task-08-capability.md),
[H-001/H-002](../../human-decisions.md), [spec](../spec.md), [execution](../../execution.md)
и [benchmark](../../benchmark.md). Closed disputes не исследовались заново.

## Artifact и scope

Единственный executable DTO artifact: production-compatible, инертные record declarations
в [Services/Workspace/StateCache/Models](../../../../Services/Workspace/StateCache/Models/WorkspaceSnapshot.cs).
Schema version **1**, профиль **`s001-single-target-debug-base-query-h001-h002-v1`**,
observed Roslyn **5.9.0.0**. Все поля — primitive values, strings, arrays и другие
DTO этого artifact; Roslyn objects, IDs, sessions, operation contexts, source/generated
text и analyzer shadow paths отсутствуют. Это descriptor schema, а не envelope codec.

Живой минимальный consumer —
[RealControlSchemaCapture](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/RealControlSchemaCapture.cs),
finite options witness —
[SnapshotOptionsCapture](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/SnapshotOptionsCapture.cs),
tests — [SnapshotSchemaTests](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/SnapshotSchemaTests.cs).
Consumer заполняет и проверяет DTO из ordinary public `MSBuildProjectLoader` descriptors
и созданного из них `AdhocWorkspace`. Он не подключён к manager/runtime load entry.
Task-03 capture-generation/manifest binding, codec/store и hydrate не реализованы.
Production не зависит от Tests assembly. E1 использует этот versioned DTO; перенос
isolated consumer или его замена требует проверок фактически поставляемого artifact.

## Source contracts

- **Три уровня properties.** Raw request и independently resolved pre-open loader
  globals — разные `SnapshotPropertySet`, где `null` явно означает absent. Evaluated
  instance values находятся отдельно в `SnapshotEvaluatedProperties` с source и
  `UnknownProperties`. Consumer получает уже выбранный globals tuple; ordinary
  positive использует absent request и absent globals, не saved defaults. Public
  `Microsoft.Build.Evaluation.ProjectCollection` с теми же globals читает actual
  Configuration/Platform/TargetFramework; Configuration и TargetFrameworkMoniker
  дополнительно сверяются с compilation assembly attributes. Это ограниченный
  single-target witness, а не доказательство всей evaluation/DTB closure. Missing
  values, multi-target declaration или disagreement дают explicit unsupported.
- **Instances/bindings.** Ключ — canonical project path и witnessed
  Configuration/Platform/TFM, без Roslyn ID. Directed project references используют
  эти ключи и сохраняют aliases/EmbedInteropTypes. Более одной loaded instance на
  физический path отвергается как `ambiguous-project-instance`; schema не угадывает
  inner-TFM binding. Canonical path не разрешает symlinks и не доказывает portability
  между машинами. Public name, assembly, language, output/ref-output/default namespace
  и CompilationOutputInfo paths сохранены.
- **Options.** Parse DTO сохраняет specified/effective language version, kind,
  documentation mode, symbols/features; public constructor и `Equals` проверяют
  обратимость. Compilation DTO перечисляет public scalar properties, diagnostic
  mappings и crypto fields. DTO-derived reconstruction через public `/debug+` parser
  witness и public `With*` проходит `Equals`: unknown parser-owned state без getter
  даёт `compilation-option-witness-unavailable`, а не defaults. `/debug-` negative
  control воспроизводит отказ. SourceFileResolver search/base/path map, XmlFileResolver
  base, known comparer и public CommandLineProject metadata-resolver factory имеют
  закрытые identities; unknown resolver/comparer/factory/witness отказывается.
- **Tree options.** Public pre-add loader descriptor должен иметь null
  SyntaxTreeOptionsProvider. После AddProject допускается только fresh built-in
  `Microsoft.CodeAnalysis.ProjectState+ProjectSyntaxTreeOptionsProvider` из текущей
  Roslyn Workspaces assembly. Custom descriptor provider и неизвестный provider
  дают отказ; private state не читается. DTO сохраняет конкретную factory policy и
  все analyzer-config memberships. Provider identity нормализуется только для
  scalar `Equals`, не переносится как object. Это finite public-project construction
  witness; arbitrary provider/analyzer execution fidelity не заявляется.
- **Documents.** Каждый source/additional/config membership имеет kind, canonical
  path, name/folders, SourceCodeKind и public DocumentInfo.IsGenerated. SourceText
  читается только для encoding codepage/preamble, checksum algorithm/bytes и disk
  equality; text не попадает в DTO. Null encoding codepage явно отличает unknown
  encoding. Повтор kind/path в одном owner отвергается как
  `ambiguous-document-membership`; разные kind и linked owners сохраняются.
- **Occurrences.** По physical path сохраняются все occurrences с owner key,
  document kind, role/source, present и proven producer/source. Generated=true
  имеет descriptor source; suspicious source names с false flag остаются Unknown
  по conservative existing input-map policy и не становятся generated/user input
  из-за пути под obj. Outputs принадлежат своему declared owner. Metadata producer
  разрешён только при единственном matching output и подтверждённом directed edge;
  ambiguous/missing edge сохраняет null producer. Imports/restore/walk-up/regions
  closure этим минимальным graph consumer не объявляется complete.
- **References.** Все actual metadata occurrences сохранены с file path, kind,
  aliases/EmbedInteropTypes. H-001 external XML exclusion записан в каждом metadata
  reference; source comments остаются на диске вместе с source/parse contract.
  Analyzer identity — original canonical file path из ordinary pre-overlay public
  descriptor, при явном caller source witness и parity с project inventory.
  Без original-path witness возвращается `original-analyzer-paths-unproven`.
  Non-file references unsupported. Path identity не разрешает DLL execution и не
  заменяет fresh binary/provenance admission; shadow-copy host не является источником.

H-002 policy названа **signing-independent empty search paths**; exact hidden
strong-name state не заявляется. Consumer использует принятый isolated operation
gate для выбранного base query, проверяет effective options/attributes, не удаляет
signed external references. Signing-dependent/unknown и generator-dependent cases
дают unsupported candidate, оставляя ordinary route. Emit/signing, external binary
documentation и arbitrary analyzers/generators вне профиля. Production runtime log
integration и ordinary fallback до execution остаются отдельным E1 scope.

## Наблюдённый real control

HEAD: `b57831d6684afe7d1d1d3ae5e703da3a72836bab`; source **1.5.5**,
.NET SDK **10.0.300**, Roslyn **5.9.0.0**, Windows. Harness Release; ordinary
control `samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj` с absent globals даёт
**Debug / AnyCPU / net10.0** для plugin и server. Содержание контрольных
plugin csproj/исходников не менялось. Новые инертные DTO добавляют 18 server source
documents относительно task-08, поэтому inventory не выдаётся за byte-identical
исторический корпус task-08.

- Plugin: **5 source**, **0 additional**, **2 configs**, **226 metadata**,
  **13 analyzers**, **1 directed project reference** к server.
- Server: **220 source**, **0 additional**, **2 configs**, **236 metadata**,
  **13 analyzers**, **0 project references**.
- Occurrences: **219 UserInput**, **6 Unknown**, **4 AnalyzerConfig**,
  **462 MetadataDependency**, **26 AnalyzerDependency**, **4 Output**,
  **2 EvaluationInput**. References не сокращались ради pass.

Real test проверяет все memberships и checksum bytes, shared configs/all owners,
направление plugin → server, explicit absent/default distinction и отсутствие
original IDs/text в serialized observation. Связанные controls проверяют linked
source + AdditionalFile на одном path, всех owners и ambiguous/unique producer
bindings с missing/present directed edge. Их AdditionalFile success — synthetic
mapping control; real positive по-прежнему не содержит AdditionalFiles.

Повторяемый observation создаётся в `TestResults/task-02/schema-observation.json`
и не является reusable envelope. SHA256 последнего focused observation:
`A77728A1DBDCAF79F36B94D839E3A26C46E378C778B09194CE711F0CECD88B48`.
Absolute machine paths и source checksums в локальном observation не являются
environment/input-validity proof. Persistent cross-process restore и benchmark
не запускались, ускорение не заявляется.

## Validation

Фокусированный runner — Roslyn MCP `run_specific_test`, `className=SnapshotSchemaTests`,
`configuration=Release`, `noRestore=true`, `timeoutSeconds=180` с build перед test.
Последний результат: **7 tests / 7 passed / 0 failed / 0 skipped**.
Equivalent reproduction:

```powershell
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~SnapshotSchemaTests
```

Предварительные 5-test прогоны: два результата **4/5** при обязательном отказе
недоказанного tree provider; после ограничения public-descriptor/fresh-provider
witness **5/5**. Затем добавлены negative parser/provider controls: **7/7**.
Первая shell `dotnet test` с restore не дала результатов и была прервана; она
не считается pass. MCP runner с noRestore выполнил реальные tests.

Финальная validation координатора по code freeze: Release solution build **passed**,
весь SourceStructure **123/123 passed**, полный main CI suite
`Category!=AnalyzerLifecycle` **1285/1285 passed**, 0 failed / 0 skipped.
AnalyzerLifecycle дополнительно **не требуется** для этой правки: production
изменён только инертными DTO, analyzer/shadow-copy/load/prepare/publication paths,
lifecycle host и test/build configuration не менялись. Structural limit inventory
не ослаблялся. [Финальные результаты и acceptance](task-02-validation.md).
