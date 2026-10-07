# Implementation packet E0/task-02.1 — gate Stage A

Дата: **2026-10-04**. Автор/integration owner: `/root/dependency_remediation`.
Координатор `/root` владеет final validation и task/index status.
Проверенный HEAD: `d3c8d5241c1c1c5d95042ce75bc057b747284cf1`.
**Полный positive implementation packet ещё не принят. Ограниченный executable L021-1 реализован и принят по review/final validation 2026-10-04: Release build exit0, focused21/21, main1228/1228; reparse subcase NotRun.**
Решения владельца A021-01/02/03/06 и D021-04/05 приняты 2026-10-04; Stage A в целом ещё не принят, Stage B не разрешён до finite proof и executable packet. Решения/proofs — в
[decisions](task-02.1-decisions.md), фактический scope — в
[evidence](../evidence/task-02.1-dependency-admission.md).

## Точный allowlist и владение

Единственный implementer владеет тремя новыми файлами:

- `docs/backlog/workspace-load-cache/epoch-0-feasibility/design/task-02.1-decisions.md`;
- `docs/backlog/workspace-load-cache/epoch-0-feasibility/design/implementation-packet-task-02.1.md`;
- `docs/backlog/workspace-load-cache/epoch-0-feasibility/evidence/task-02.1-dependency-admission.md`.

В shared files разрешены только append-only gate supplements task-02.1 без
изменения прежнего нормативного текста/declarations/acceptance summaries:

- `docs/backlog/workspace-load-cache/epoch-0-feasibility/design/implementation-packet-task-01.md`;
- `docs/backlog/workspace-load-cache/epoch-0-feasibility/design/implementation-packet-task-03.md`;
- `docs/backlog/workspace-load-cache/epoch-0-feasibility/design/implementation-packet-task-04.md`.

Implementer владеет supplements; coordinator — итоговым bookkeeping. Packet-04
уже был dirty на dispatch: прежний diff сохраняется и не присваивается task-02.1.
Параллельного writer supplements нет. Raw artifacts записываются в отдельный
task021 temp artifact root. Retained fixtures только читаются/query без изменения
source/request bytes; existing tests сохраняют собственные mutation/restore/temp
contracts и запускаются координатором.

**Исторический investigative scope: C# write allowlist был пуст. Текущий exact exception L021-1 ниже имеет приоритет и добавляет только перечисленные7files+runner; portable admission/semantic schema не меняется.**
Tests/expectations/fixture bytes/methods в blocked scope не редактируются.
Точный разрешённый integration diff task-04: только названный packet supplement;
`IndependentWorkspaceOracle.cs`, tests/reader/models — **ноль правок**.
Packets-01/03 также потребляют неизменённые interfaces. Пустой diff не является
планом положительной интеграции. После owner decisions design должен выдать новый
packet с exact producer/model/codec/host/consumer declarations и пройти review
до первой правки consumer.

Denylist: всё вне конечного allowlist, особенно production `Services/`, `Tools/`,
`Diagnostics/`, SolutionManager, все C# tests/models, csproj/solution, package
versions, runtime/build configurations, global configs, installed SDK/pack/workload,
spec.md, design.md, cache-contract.md, UNRESOLVED-v2.md, verification.md, fixture
literals и task-04 stages 2–7. Исключение для перечисленных ниже normative amendments разрешено владельцем. Task/index status — coordinator. Commit/push запрещены.
Перед последующей C# правкой/style review прочитать docs/code-style.md полностью.
Этот factual audit interfaces не является style review.

## Проверенные declarations текущего кода

Experiment namespace: `RoslynMcpServer.Tests.WorkspaceLoadCache`.
Ниже existing declarations, не новые proposed types. Пути относительно
`RoslynMcpServer.Tests/WorkspaceLoadCache/`; все C# файлы read-only.

`DependencyEvidenceRunner.cs`, `internal sealed class DependencyEvidenceRunner`:

```csharp
public Task<AdmissionEvidence> InspectAsync(
    ExperimentRequest request,
    string artifactDirectory,
    CancellationToken cancellationToken);
public Task<AnalyzerProvenanceSnapshot?> RevalidateOverlayAsync(
    SemanticSnapshot snapshot,
    AdmissionEvidence evidence,
    Guid newSessionId,
    Solution baseSolution,
    CancellationToken cancellationToken);
internal static Task<ImmutableArray<string>> RecheckObservedFilesAsync(
    IEnumerable<EvidenceEntry> entries,
    CancellationToken cancellationToken);
internal static Task<DependencyCommandResult> RunAsync(
    string workingDirectory,
    string artifactDirectory,
    string label,
    IEnumerable<string> arguments,
    CancellationToken cancellationToken);
internal static DependencyObservations Replay(string path, CancellationToken cancellationToken);
internal static Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken);
```

Первые четыре реализации имеют modifier async; выше опущен только modifier тела.
Audited private seams: CollectObserved, CreateObservedRegions, InspectXml, AddFile,
RequireOwnedFixture, ReadXml, CompileGlob, NormalizePattern. Они не объявляются
новыми interfaces. Inspect безусловно добавляет три closure reasons, возвращает
пустые edges и false/false readiness. Revalidate ожидает file recheck, не использует
failure array и всегда возвращает null. Это safe refusal, не R021-08.

Каждый record находится в `Models/<TypeName>.cs`:

```csharp
internal sealed record ExperimentRequest(
    string WorkspacePath, string? Configuration, string? Platform,
    string? TargetFramework, ImmutableArray<string> SelectedRoots,
    string Scope, bool MetadataMode);
internal sealed record InstanceKey(
    string ProjectPath, string InnerTargetFramework,
    ImmutableSortedDictionary<string, string> EffectiveProperties);
internal sealed record ProjectEdge(
    InstanceKey From, InstanceKey To, ImmutableArray<string> Aliases,
    bool EmbedInteropTypes);
internal sealed record EvidenceEntry(
    string Category, InstanceKey? Owner, string PathOrRegion, string? Sha256,
    bool IsAbsent, string Source, string SourceVersion, string RawEvidenceLocator);
internal sealed record AdmissionEvidence(
    string ProfileVersion, string Disposition,
    ImmutableArray<EvidenceEntry> Entries, ImmutableArray<RegionEntry> Regions,
    ImmutableArray<InstanceKey> ExpectedInstances, ImmutableArray<ProjectEdge> ExpectedEdges,
    ImmutableArray<string> ExpectedCompilerInputPaths, ImmutableArray<string> Reasons,
    bool AnalyzerInventoryComplete, bool OverlayReady);
internal sealed record AnalyzerEntry(
    InstanceKey Consumer, string OriginalPath, string Sha256,
    string OriginDirectory, ImmutableArray<string> DependencyPaths);
internal sealed record SemanticSnapshot(
    string Schema, ExperimentRequest Request, ImmutableArray<ProjectEntry> Projects,
    ImmutableArray<ProjectEdge> ProjectReferences);
internal sealed record DependencyCommandResult(
    int ExitCode, string StandardOutput, string StandardError);
internal sealed record DependencyContextObservation(
    string Project, int InstanceId, Dictionary<string, string> Properties);
internal sealed record DependencyImportObservation(
    string Project, string Imported, string Unexpanded, bool Ignored, string Message);
```

InstanceKey содержит собственные Equals/GetHashCode: platform-aware path comparison
и value comparison properties. Одной записью constructor equality не описывается.
`Models/DependencyObservations.cs`: internal sealed class с public get-only
`List<DependencyImportObservation> Imports`, `List<string> TaskOutputAnalyzers`,
`TaskOutputOwners`, `CompilerCommands`, `CompilerOwners`,
`List<DependencyContextObservation> ProjectContexts`, все initialized empty;
`public int CoreCompileCount { get; set; }`. Это mutable raw observations,
не portable completeness facts. Path-only owners теряют exact inner context;
runtime ID двух logs не образуют общее пространство identity.

`Models/RegionEntry.cs`, `Models/RegionMember.cs`, `Models/ProjectEntry.cs`,
`Models/ReferenceEntry.cs`, `Models/DocumentEntry.cs`, `Models/EncodingPolicy.cs`,
`ExperimentAnalyzerAdmission.cs` сохраняют точные declarations packet-01.
Category/region/schema invariants оттуда включены без изменений. Новый provenance
source kind или graph mapping field здесь не добавляется.

Потребители read-only:

```csharp
// OracleFixtureBuilder.cs
public static Task<OracleFixtureCase> CreateAsync(
    FixtureScenario scenario, string parentDirectory, CancellationToken cancellationToken);
// IndependentWorkspaceOracle.cs
public Task<OracleComparison> CompareAsync(
    OracleFixtureCase fixture, string artifactDirectory, CancellationToken cancellationToken);
public Task<ExperimentSession> OpenOrdinaryAsync(
    ExperimentRequest request, string artifactDirectory, CancellationToken cancellationToken);
// HydrateHostExperiment.cs
public HydrateHostExperiment(ExperimentAnalyzerAdmission? analyzerAdmission = null);
public Task<ExperimentSession> HydrateAsync(
    SemanticSnapshot snapshot, AdmissionEvidence admission,
    string artifactDirectory, CancellationToken cancellationToken);
```

У builder есть internal overload с `Action<string>? afterFileWritten` перед token
для existing tests; packet его не вызывает/не меняет. CompareAsync вызывает Inspect
до candidate capture/hydrate. Fixtures ожидают unknown; изменение assertions без
proof запрещено. Будущая доказанная supported row должна давать positive NotRun
до собственной реализации task-04; здесь такой row и pipeline не создаются.

## Existing production evidence API — read-only

Namespace: `RoslynMcpServer.Services.Analyzers`.
`Services/Analyzers/AnalyzerProvenanceCaptureGate.cs`, internal static class:

```csharp
public static string? TryGetUnsuitableReason(
    AnalyzerProvenanceSnapshot? snapshot, Guid sessionId);
public static bool IsSuitableForOptInOverlay(
    AnalyzerProvenanceSnapshot? snapshot, Guid sessionId);
```

Без I/O/cancellation, immutable borrowed inputs. Gate отвергает null, пустую/чужую
session, Incomplete/Failed. Complete с правильной session даёт suitable; зависимости
gate самостоятельно не проверяет.

`Services/Analyzers/AnalyzerProvenanceCaptureService.cs`:

```csharp
internal sealed record AnalyzerProvenanceSnapshot(
    Guid LoadSessionId, string LoadedPath,
    ImmutableDictionary<string, string> RequestedGlobalProperties,
    AnalyzerProvenanceToolset Toolset, AnalyzerProvenanceCaptureStatus Status,
    ImmutableArray<AnalyzerProvenanceProjectContext> ProjectContexts,
    ImmutableArray<CapturedAnalyzerProvenanceItem> AnalyzerItems,
    ImmutableArray<AnalyzerProvenanceBinding> Bindings,
    ImmutableArray<string> FailureCodes, AnalyzerProvenanceCaptureMetrics Metrics);
internal sealed record AnalyzerProvenanceToolset(
    string? RegisteredMsBuildPath, string? RegisteredMsBuildVersion,
    string RuntimeMsBuildAssemblyPath, string? RuntimeMsBuildAssemblyVersion,
    string? DotNetSdkPath, string? DotNetSdkVersion);
internal sealed record AnalyzerProvenanceBinding(
    AnalyzerProvenanceBindingStatus Status, ProjectId? ConsumerProjectId,
    ProjectId? SourceProjectId, string Identity, string? SourceProjectFile,
    string? SelectedInnerTargetFramework,
    ImmutableDictionary<string, string> EffectiveGlobalProperties);
```

Supporting declarations в том же файле: ProjectContextKey:58, EventContextKey:68,
ProjectContext:86, CapturedAnalyzerProvenanceItem:94, Metrics:120.
Captured item требует binlog ordinal, consumer context и task context. SDK evaluated
item нельзя включать через вымышленные task contexts. Snapshot здесь не создаётся.

`Services/Analyzers/AnalyzerPrivateDependencyInspector.cs`:

```csharp
public static AnalyzerDependencyInspection Inspect(string assemblyPath);
internal sealed record AnalyzerDependencyInspection(
    AssemblyName? Identity, IReadOnlyList<AssemblyName> References,
    IReadOnlyList<AssemblyName> PrivateReferences, string? FailureReason);
```

Public method на internal static type читает PE/AssemblyName metadata, возвращает
bounded failure для поддержанных missing/malformed cases и изменяет test counter.
Это refusal detector, не полный dynamic-read proof/loader. Production API здесь
не вызывается для искусственного получения positive evidence.

## Ownership, error, cancellation и lifetime

Audit владеет только своим JSON/script evidence, берёт retained fixtures после
завершения coordinator tests, сверяет relevant hashes до/после property query,
не пишет project/source/global/installed files. Сохраняет exit code/stdout/stderr
и exact arguments. Audit не загружает analyzers; ordinary discovery builds existing
tests могут выполнять SDK tasks/generators в дочерних процессах. Candidate-owned
no-load не означает process-wide no-execution.

Existing Inspect требует owned temp marker, caller-exclusive fixture,
artifactDirectory и закрытие processes/loggers. Invalid arguments — contract error;
cancellation сохраняет token; ожидаемые I/O/XML/JSON evidence failures дают unknown.
Revalidate требует nonempty newSessionId, заимствует immutable DTO/evidence/base,
не запускает processes/locks, сохраняет cancellation и сейчас всегда отказывает.
File recheck сообщает unreadable/changed/appeared. Caller flag/empty DTO не создают
fresh closure.

Будущая R021-08 требует independently retained portable facts того же runner,
exact equality analyzer/dependency sets до empty shortcut, новые baseSolution
bindings и действительно новый snapshot. Malformed/ambiguous/stale facts дают
refusal до loading; ресурсами до завершения semantic reads владеет session.
Эти обязательства сохраняются, но docs packet их не реализует.

## Источники, bounds и отложенная интеграция

Прочитаны все mandatory inputs task-02.1; проверены current code, installed SDK XML,
fresh None/Embed observations, compiler/TaskOutput sets, команды и hashes.
Отрицательный вывод следует действующему spec; список observed files не становится
complete discovery. Existing bounds сохраняются: 100000 entries, depth64,
256 MiB matching bytes; reparse/unreadable дают отказ. Bounds не ослабляются.

R021-01/03/04/06/07/08 зависят от D021-01/02/03 owner decisions/proofs.
R021-02/05 graph/import/region implementation отложена до положительного packet;
независимый C# patch не dispatch-ится до обязательного design gate.
R021-09 не имеет authorized expectation change: positive admission не получен.
D021-05 сохраняет negative scope Razor/Web без выдуманных globs/goldens.

После нормативных решений, finite source-backed closure contracts и exact
model/provenance schema design заменяет этот gate packet и обновляет affected
packets до dispatch. Следующий packet перечисляет каждый новый type/file/signature
и одного owner shared host/codec/fixture/oracle files; потребители не додумывают
fields. Coordinator владеет final Release build/main, independent review и statuses.
Passing baseline tests не закрывают blocked task-02.1 и не разрешают public
activation, next epoch или series completion.

## Продолжение после решений владельца — 2026-10-04

Владелец принял A021-01 (строгое отдельное workload extension), A021-02 (отдельный
SDK-evaluated-item provenance), A021-03 (source-backed finite closure узкого
unchanged package-free subset без downloads/NuGet-reference fallback/overrides), D021-04 (полный
independent exact graph/rebinding, ambiguity refuses), D021-05 (Razor/Web пока
negative, без CSS/golden исправлений). Отсутствие owner approval больше не blocker.

Дополнительный конечный write allowlist, один owner `/root/dependency_remediation`:

- `docs/backlog/workspace-load-cache/epoch-0-feasibility/spec.md` — только A021-01/02,
  строгая граница A021-03 и выбранный negative scope D021-05;
- `docs/backlog/workspace-load-cache/cache-contract.md` — согласование source/provenance
  и strict closure с этими принятыми решениями;
- `docs/backlog/workspace-load-cache/UNRESOLVED-v2.md` — запись тех же owner choices
  U-ARB-04/06; остальные gates не менять;
- `docs/backlog/workspace-load-cache/epoch-0-feasibility/design.md` — те же contracts,
  truthful provenance, существующие positive proof prerequisites;
- `docs/backlog/workspace-load-cache/epoch-0-feasibility/design/task-02.1-source-contracts.md`
  — новый audit точных binary/source revisions, конечных inputs/algorithms/schema
  и конкретных незакрытых seams до C# implementation.

Эти exact exceptions имеют приоритет над прежним denylist normative files.
Ранее разрешённые decisions/packet/evidence и supplements packets01/03/04 можно
согласовать с owner choices, сохраняя historical observations и отрицательную
приёмку. Proof contracts не выдаются за executed tests. C# exception теперь задан принятым limited L021-1 ниже; installed binaries/config только читать.
Raw source downloads/metadata evidence принадлежат отдельному task temp directory.
### Уточнение ordered workload selection — 2026-10-04

Принятая A021-03 запрещает NuGet/restore/reference fallback, downloads и overrides;
SDK выбирается exact pin с `rollForward:disable`. Штатный ordered выбор workload
manifests по feature-band входит в A021-01 только как доказанный version-specific
algorithm. Название upstream `FallbackForMissingManifest` не исключает эту ветвь
и не разрешает произвольный fallback. Для текущей установки отсутствие primary
`10.0.300`, выбор secondary `10.0.100`, installstate, workloadsets, installer/userlocal
markers и все более приоритетные положительные/отрицательные selectors входят
в independent no-build re-resolution, повторный probe и mutation matrix.

## Limited executable packet L021-1 — на независимое review, 2026-10-04

**ACCEPT independent reviewer `/root/independent_review`, 2026-10-04: только limited executable L021-1. R021L-01/02/03 исправлены и перепроверены. Code review/final coordinator validation ещё обязательны.**
Этот конечный scope реализует части R021-02/03/05: independent evaluated graph
observations, source-backed framework file selection и disk-only input recheck.
Он не закрывает полный R021-02, R021-03, R021-05, Stage A или remediation.
Workload source audit и оставшиеся executable closure contracts продолжаются отдельно;
S021-02 workload selector остаётся private research script, C# workload resolver в
этом dispatch не реализуется до audit composition/AutoImport/localization.

### Exact write allowlist L021-1

Добавляются только следующие C# файлы под
`RoslynMcpServer.Tests/WorkspaceLoadCache/`, один owner `/root/dependency_remediation`:

- `DependencyGraphDiscovery.cs`;
- `DependencyFrameworkSelection.cs`;
- `DependencyInputProbe.cs`;
- `DependencyPartialProofTests.cs`;
- `Models/DependencyGraphEvidence.cs`;
- `Models/DependencySelectionEvidence.cs`;
- `Models/DependencyDirectoryObservation.cs`.

Меняется только существующий `DependencyEvidenceRunner.cs`: вызов graph discovery,
обход discovered exact inner instances вместо только input roots, сохранение partial
graph artifact/ExpectedEdges, запись framework selection artifact и явная обработка
recheck failures до неизменного null overlay refusal. Existing paths records и
portable semantic/admission schema не меняются. Все три unproven reasons и оба
false readiness остаются безусловно. Остальные files — denylist, включая host,
loader, ExperimentAnalyzerAdmission, production provenance, codec, oracle, builder,
fixture literals/expectations и preexisting task04 scaffold. Supplements01/03/04
получают только описание partial producer evidence; signatures consumers прежние.
Coordinator owns final validation/status. Новые DTO — private experiment evidence
JSON, не production cache format и не вход авторизации overlay.

### Проверенные используемые APIs и новые exact declarations

Все новые типы `internal sealed`, logic classes `static`; тесты `public sealed`.
Namespace `RoslynMcpServer.Tests.WorkspaceLoadCache`. Records каждый в своём файле:

```csharp
internal sealed record DependencyGraphEvidence(
    ImmutableArray<InstanceKey> Instances,
    ImmutableArray<ProjectEdge> Edges,
    ImmutableArray<string> Reasons);
internal sealed record DependencyDirectoryObservation(
    string Path,
    ImmutableArray<string> Entries);
internal sealed record DependencySelectionEvidence(
    string SourceVersion,
    ImmutableArray<EvidenceEntry> Inputs,
    ImmutableArray<DependencyDirectoryObservation> Directories,
    ImmutableArray<EvidenceEntry> SelectedFiles,
    ImmutableArray<string> Reasons);
internal static class DependencyGraphDiscovery
{
    public static Task<DependencyGraphEvidence> InspectAsync(
        ExperimentRequest request, string artifactDirectory,
        CancellationToken cancellationToken);
}
internal static class DependencyFrameworkSelection
{
    public static DependencySelectionEvidence Inspect(
        InstanceKey owner, string sdkDirectory,
        CancellationToken cancellationToken);
}
internal static class DependencyInputProbe
{
    public static EvidenceEntry ReadFile(
        string path, string category, InstanceKey? owner, string source,
        CancellationToken cancellationToken);
    public static DependencyDirectoryObservation ReadDirectory(
        string path, CancellationToken cancellationToken);
    public static Task<ImmutableArray<string>> RecheckAsync(
        ImmutableArray<EvidenceEntry> entries,
        ImmutableArray<DependencyDirectoryObservation> directories,
        CancellationToken cancellationToken);
}
```

`ReadFile` возвращает для отсутствующего file-or-directory path только EvidenceEntry
с Category=known-absent, IsAbsent=true, Sha256=null; переданная category сохраняется
только для существующего file с IsAbsent=false и непустым SHA-256. Required absence
записывается в Inputs и добавляет reason; optional sidecar/manifest absence также
только в Inputs. SelectedFiles содержит только существующие required metadata/
analyzer files, никаких absent records. Существующий directory на месте file —
expected I/O failure. SourceVersion exact contract constant `sdk-10.0.300/caa81fa4971f74880cdab61990cb1b11420939ec`;
RawEvidenceLocator равен source filename/path для disk proof. Owners borrowed
immutable. Graph использует existing `DependencyEvidenceRunner.RunAsync` и
WriteJsonAsync, которые уже audited выше; command labels задаются числом ordinal
и inner ordinal, а не Project.Name, чтобы одинаковые basenames не перезаписывали logs.
`DependencyPartialProofTests` имеет public constructor `(ITestOutputHelper output)`
и public Task/void facts ниже; не создаёт production integration API.

### Graph observations — точный ограниченный algorithm

Сохранить различие null и explicit empty Configuration/Platform/TargetFramework.
Null Configuration/Platform имеют existing effective defaults Debug/AnyCPU;
empty explicit значение даёт `graph-empty-request-property`, не default.
Scope full/selected допустим; other scope и MetadataMode дают reason. Корни
canonicalize+sort по platform path comparer; duplicates и missing roots — причины.
WorkspacePath должен существовать и совпадать с одним явным project root; solution/
filter entrypoints вне этого bounded traversal дают `graph-workspace-kind-unproven`.
Список roots подтверждается реально выполненными evaluations каждого root, но
это **не доказательство полноты request scope**: постоянный
`graph-root-scope-and-resolution-unproven` сохраняется в Reasons.

Для каждого canonical project сначала outer `dotnet msbuild -nologo -getProperty:TargetFramework,TargetFrameworks`
с exact Configuration/Platform; explicit requested TFM передаётся как property.
Absent TFM => все непустые различные declared inner TFMs; никаких outer InstanceKey.
Каждый inner отдельно evaluates `TargetFramework,TargetPath` и ProjectReference
items через getItem с exact effective properties. InstanceKey properties ровно
Configuration/Platform/TargetFramework; неизвестные inherited/additional global
properties не выдумываются. Discovery запрашивает полную metadata каждого evaluated
ProjectReference и сохраняет raw JSON до интерпретации.

Транзитивный target сначала оценивается outer, разрешена только единственная
same-TFM ветвь. Неоднозначная negotiation, SetTargetFramework, SetConfiguration,
SetPlatform, AdditionalProperties, GlobalPropertiesToRemove, UndefineProperties,
ReferenceOutputAssembly=false, OutputItemType или Targets дают
`graph-reference-selection-unproven`; edge для такого item не объявляется.
Aliases split comma/semicolon, empty=>[] (default global), сохраняются sorted ordinal;
EmbedInteropTypes принимает absent/false/true, иное=>reason. Target path canonicalize
из FullPath либо Identity relative consumer project directory. Missing target отдельный
`graph-missing-project-reference`; Compile inventory empty само по себе не missing.
Cycle активных exact keys даёт `graph-cycle`; visited keys не дублируют instances.
Bounds:256 project paths/1024 exact instances/4096edges/recursion64. Превышение даёт
reason, partial results никогда не называются complete. Каждый child evaluated с
собственным TargetFramework; входящий TFM не присваивается слепо.

Independent evaluated edge facts сохраняются в ExpectedEdges, однако actual
reference-resolution/binlog context association ещё unproven. В этом dispatch
compiler/TaskOutput path-only bindings не улучшаются ложными mappings; permanent
graph reason и общие refusal остаются. Existing CollectObserved продолжает
собирать inputs отдельно для каждой discovered inner evaluation. Names labels
во всём Inspect уникальны на ordinal exact instance. Unknown third-party properties
и removed/dynamic graph edges не превращаются в empty complete graph.

### Framework selection — finite source contract

Применяется только SDK directory basename10.0.300, net9.0/net10.0 и exact source
contract S021-04. Читает `<sdk>/Microsoft.NETCoreSdk.BundledVersions.props` без DTD;
selects ровно один KnownFrameworkReference с Include=Microsoft.NETCore.App,
TargetFramework=<tfm>, TargetingPackName=Microsoft.NETCore.App.Ref, literal numeric
TargetingPackVersion; иные/missing/duplicate values=>reason. Root packs вычисляется
как sibling sdk parent, разрешён только `<dotnet>/packs/Microsoft.NETCore.App.Ref/<version>`.
Pack version directory path segments проверяются, `..`/rooted/expressions refused.

Читает data/FrameworkList.xml; пропускает ReferencedByDefault=false. Managed files
выбираются как ref/<tfm>/<AssemblyName>.dll; нет path fallback. Analyzer entries
Type=Analyzer/Language=cs выбираются по относительному Path строго внутри pack.
Hash XML, selected DLL и рядом существующий .xml documentation sidecar (отсутствие
тоже input), data/PlatformManifest.txt и data/PackageOverrides.txt presence/bytes.
Сохраняет membership directory pack/data, ref/<tfm> и analyzers/dotnet/cs для
обнаружения added/missing members. Обязательные file absence=>reason, optional
sidecar/manifest absence допустим как negative input. Не выполняет DLL.

Сравнение SelectedFiles категорий metadata/analyzer со observed compiler files
данного exact owner исключает SDK pair из pack-analyzer comparator по path root;
missing/extra pack files дают reason. SourceVersion связывает algorithm, а binary
hash verification/full task producer-input closure всё ещё не completed: permanent
`framework-task-input-closure-unproven` в Reasons даже при совпавших paths.
Никаких выводов о analyzer executable safety по этим file identities.

### No-build recheck, equality, ошибки и lifetime

File SHA-256 uppercase; paths canonical absolute, platform comparer; semantic
strings/properties/aliases ordinal. ImmutableArray/record aggregate equality не
используется как set equality. Directory Entries сортируются ordinal; entry string
`D:<name>` или `F:<name>` сохраняет child kind и case. Missing directory representation
— одно значение `A:`; empty present directory=>пустой array. File и directory probes проверяют
сам запрошенный path и все его существующие ancestor components на reparse,
включая missing descendant через reparse ancestor; directory probe дополнительно
отвергает reparse children. Recheck повторяет эти проверки для каждого file и
directory probe в каждой фазе. Никаких бесконечных рекурсий; path depth64, file bytes limit256MiB,
100000 combined entries и256MiB total selected bytes в selection/recheck attempt.
Bound violation/unreadable/changed/appeared/type-changed=>refusal reason, не empty
success. Malformed XML/JSON/metadata=>reason, unexpected programmer errors не прячутся.

Recheck повторяет file presence/hash и exact directory membership дважды и
сравнивает обе последовательности со saved inputs. Проверяются также file bytes
между directory probes; изменение, обнаруженное любой фазой, остаётся failure.
Это bounded race detection, не OS atomic snapshot. Stable adversarial ABA между
probes не обещается исключить. SHA/path inputs не наделяют evidence полномочиями.
Нет process/build/restore/Open* /DLL load/cache write; token проверяется до I/O и
в loops; OperationCanceledException с исходным token наружу. Caller владеет files,
методы закрывают streams/XML/JSON; shared mutable cache отсутствует.

Invalid null/empty API arguments — ArgumentException/ArgumentNullException;
expected I/O/UnauthorizedAccess/XmlException/JsonException внутри Inspect=>Reasons,
ReadFile/ReadDirectory throwing helpers внутри caught caller scope. Recheck=>failure
strings `content-changed`, `known-absent-appeared`, `membership-changed`, `unreadable`,
`probe-limit`; empty result означает только unchanged перечисленных probes.
Overlay всегда возвращает null, включая caller-supplied true flags.

### Finite test matrix и review boundary

Новые facts (точные test method names):

- `Graph_discovers_transitive_instances_without_authorizing_admission` — owned
  package-free root->child, child отсутствует в input roots, distinct same basenames,
  aliases/EmbedInteropTypes, реальные outer/inner evaluations, full raw preserved;
- `Graph_preserves_inner_frameworks_and_refuses_missing_or_ambiguous_reference` —
  net9/net10 roots, missing target и unsupported metadata дают distinct reasons;
- `Framework_selection_matches_installed_pack_files_without_loading_analyzers` —
  оба actual installed packs, counts164/167 и6 analyzers, paths/bytes повторно читаются;
  installed baseline changes дают explicit mismatch, не skip/auto expected refresh;
- `Input_recheck_detects_content_absence_membership_and_kind_changes` — owned temp
  only: same-path content, deletion, appeared absent file/directory, new/removed member,
  empty directory and kind change; restore owned bytes in finally;
- `Input_probe_refuses_reparse_limits_and_preserves_cancellation` — cancellation,
  oversized owned input и reparse root/ancestor/missing descendant/child witnesses
  only when OS can create them; unavailable reparse
  creation отдельно сообщается, не silent pass на branch;
- `Partial_evidence_cannot_create_overlay_from_forged_readiness` — existing empty
  AdhocWorkspace/DTO with forged flags still null; no analyzer load.

Focused minimum: FullyQualifiedName~WorkspaceLoadCache.Dependency (existing15 plus
new facts), existing IndependentWorkspaceOracleTests/host regressions remain in full
main. Coordinator final Release solution build+Category!=AnalyzerLifecycle after
final diff; no production edits => SourceStructure separately not required, it
remains inside main. AnalyzerLifecycle not required for this limited scope since
host/loader/publication paths untouched; pending positive adapter would change that.
M021 rows remain partial/NotRun for full admission/overlay, all13fixtures retain
unknown/false expectation. Task04 supported row remains NotRun/pipeline missing.






### L021-1 code-review corrections: R021C-01/02/03

Same author исправляет code findings без расширения admission. Следующая exact
signature delta предложена на review до реализации. Новых файлов/types-потребителей
нет. В DependencyInputProbe добавляются overloads/method:

```csharp
public static EvidenceEntry ReadFile(
    string path, string category, InstanceKey? owner, string source,
    ref long remainingBytes, ref int remainingEntries,
    CancellationToken cancellationToken);
public static (EvidenceEntry Entry, byte[] Bytes) ReadFileContent(
    string path, string category, InstanceKey? owner, string source,
    ref long remainingBytes, ref int remainingEntries,
    CancellationToken cancellationToken);
public static DependencyDirectoryObservation ReadDirectory(
    string path, ref int remainingEntries,
    CancellationToken cancellationToken);
```

Прежние overloads выделяют local256MiB/100000 budget; composite selection/recheck
используют один remaining budget для всех файлов, directory roots/members и
повторных probes. ReadFileContent используется только для двух XML: захваченные
bytes одновременно хешируются и затем разбираются MemoryStream/XmlReader без
повторного filesystem read. Caller владеет returned byte[]; bytes не включаются
в portable evidence. Method records absent как known-absent с пустым byte[];
existing file positive record сохраняет requested category. Нет byte[] content
allocation для обычного ReadFile.

Remaining values — nonnegative caller-owned counters; invalid negative =>
ArgumentOutOfRangeException. Перед следующим input probe резервируется entry;
перед каждым enumeration step должен оставаться entry budget, иначе отказ без
дальнейшего enumeration. Каждый фактически полученный child списывает entry.
Перед file content read проверяется length <= remaining; каждый stream read
ограничен remaining, фактически прочитанные bytes списываются сразу. Никакой
следующий file/XML/directory не считывается с уже исчерпанным budget. Conservative
refusal при точно исчерпанном directory budget допустим; превышение никогда не
выдаётся за complete. Private nested `ProbeLimitException : IOException` отличает
expected bound refusal внутри DependencyInputProbe; не выходит как новая consumer
schema. Recheck возвращает probe-limit, selection — existing failure reason.
Тест aggregate limit использует тот же budget overload: два owned files суммарно
больше заданного малого remaining budget, первый прочитан, второй отказ до чтения;
несколько directories аналогично делят один counter. Existing default256MiB test
сохраняется. Это meaningful aggregate consumption witness без создания400MiB data.

R021C-01: root outer evaluation получает именно explicit request.TargetFramework,
включая условные project declarations; absence остаётся absence. Root selection
при explicit TFM проверяет evaluated TargetFramework, не список, полученный под
другим absent request. Transitive GetFrameworks продолжает independently absent
negotiation и same-TFM match; root explicit cache не используется как transitive
absent cache. Existing second graph test получает conditional default-vs-explicit
project и проверяет сохранённые command arguments; invalid explicit empty сохраняет
reason. R021C-03 applies mandatory one argument per line только к новым/changed
expanded calls; existing unrelated formatting не изменяется.

Same-reviewer code recheck 2026-10-04: **ACCEPT L021-1 code**, R021C-01/02/03 закрыты.
Coordinator final execution validation pending; full positive Stage A/overlay не принят.

### Предложенная bounded test delta: Windows reparse witness

Current focused fact passed, но reparse subcase был NotRun: CreateSymbolicLink
IOException/Win321314 (required privilege). Read-only owned probe подтвердил, что
обычный Windows junction создаётся без elevation. Предлагается только test setup
в DependencyPartialProofTests, без изменения production/probe APIs: private
`static Task<(bool Created, string Kind, string? Detail)> CreateReparsePointAsync(string link, string target, CancellationToken cancellationToken)`.
Сначала Directory.CreateSymbolicLink. На Windows при expected IO/Unauthorized
failure — один дочерний `powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden`
через absolute executable `%SystemRoot%/System32/WindowsPowerShell/v1.0/powershell.exe`
с ProcessStartInfo.ArgumentList/CreateNoWindow и finite encoded `New-Item -ItemType Junction`
для этих exact owned temp paths;
paths canonical, direct siblings внутри unique marker-owned test root under temp;
setup has10second timeout+caller cancellation, kill/wait/dispose собственного child
при timeout/cancel. Caller cancellation propagates unchanged. No restore/build/MSBuild/
analyzer execution/elevation. Не-Windows fallback не запускает Windows process.
Stdout/stderr/exitcode и исходный exception/HResult сохраняются в owned witness JSON;
failure не считается Passed, unavailable остаётся explicit NotRun. Успех требует
actual ReparsePoint attributes перед теми же root/ancestor/missing/child/recheck
assertions. Cleanup Directory.Delete(link), без recursion и без операции над target.
Scope меняет только доступность test witness; limited admission flags остаются false.
Реализация после same-reviewer contract acceptance и завершения текущего main.

Same-reviewer 2026-10-04: **ACCEPT limited executable Windows reparse witness delta**.
Same implementer реализовал delta только в DependencyPartialProofTests.cs; C# frozen
для coordinator build/focused/main. Code review и итоговая execution acceptance
этой test delta пока pending. Earlier main failure сохраняется в evidence.

## P021-2. Кандидат полного positive scope — не executable dispatch

Этот раздел фиксирует конкретный integration seam и remaining proof; он не
отменяет accepted L021-1 flags=false и не разрешает C# до полного packet/review.
Initial candidate — unchanged ProjectGraph, App+Lib, net10.0, exact SDK10.0.300,
pack10.0.8. MultiTarget, Razor/Web и остальные13fixtures сохраняют отдельно
доказанный refusal/NotRun; source literals/goldens не меняются. Required generator
positive будет отдельным owned input после определения exact reachable contract.

### Категории и точный недостающий контракт

1. Graph: recursive evaluated instances уже есть, но binlog ConsumerContext должен
   связываться с canonical ProjectFile+TargetFramework+effective globals, с
   equality всех independently discovered instances/edges. Нельзя присвоить root
   instance всем Csc events дочерней Lib. ProjectReference selection требуется
   сверить с ResolveProjectReferences target outputs/ReferencePath metadata и
   compilation project edges; .dll path без consumer/target match недостаточен.
2. Workload: S021-02/03/03a source algorithms выбирают16manifest,12AutoImport,
   positive/negative bands/markers/installstate, RID aliases и catalogs. Не хватает
   actual child culture и executable bounded selector/recheck для membership,
   conditions/transitive imports. Any unresolved path/redirect/culture branch
   сохраняет toolset-unproven; .NET SDK directory целиком не является input set.
3. Framework: L021-1 доказал independent FrameworkList refs/analyzers selection
   для167+6net10 files. ResolveFrameworkReferences task parameters и target-side
   conditions/metadata ещё должны совпасть; known selection не есть task closure.
4. Restore: S021-06 controls сохранили assets/dgspec/imports. Implicit request
   реально выбирает4NuGet configs и configured VS fallback folder, explicit
   --configfile выбирает1. Нужен точный no-package unreachable selection proof
   и все folder/config negative selectors; никакой fallback asset selection или
   download не разрешён. ResolvePackageAssets cache/mtime/settings и RAR StateFile
   должны быть independently checked либо отказ. Actual paths не выводятся из DTO.
5. Tool tasks: S021-06a SourceLink no-repository branch имеет exact .git/HEAD inputs,
   но actual supplied Hosts/SourceRoots metadata, RAR SearchPaths/Find flags,
   GetFrameworkPath output getters и consumed target conditions ещё не замкнуты.
6. Executable analyzer: 8compiler DLL per consumer (SDKpair+6pack) остаются в полном
   inventory. Host runtime/Roslyn/Composition identity mapping должен быть exact,
   source-backed и совместим с demanded AssemblyRefs. Текущий strict loader не
   принимает older Roslyn references; один ALC не допускает mixed pack versions.
   Для initial net10 mixed versions отказываются. SDK no-generator early-return
   является фактом одного operation, не основанием исключить SDK diagnostics или
   объявить полную closure. Resource/culture/config/dynamic-loading callback
   boundaries и required permitted generator witness остаются обязательными.
7. Fresh overlay: повторно прочитать и независимо выбрать весь input set без
   MSBuild/process/DLL execution; только после этого однозначный rebind к fresh
   ProjectId, actual session/toolset, equality independently computed inventories.
   Current runner возвращает null; fresh Complete ещё не реализован/не наблюдался.

### Truthful distinct-source envelope: выбранный seam для дальнейшей детализации

`CapturedAnalyzerProvenanceItem.TaskContext` обязателен и остаётся только для
реально replayed TaskOutput. SDK evaluated Analyzer items туда не помещаются,
нулевые/выдуманные task IDs не создаются. Proposed isolated delegate result будет
обёрткой над existing production snapshot и отдельными SDK evaluated source rows.
Production files/types/gate не меняются. Snapshot.AnalyzerItems сохраняет genuine
TaskOutput subset; envelope SDK rows несут independent instance, evaluated item
identity, defining SDK import path+hash, selection proof identity и fresh consumer
binding. Existing snapshot Bindings может содержать union реальных confirmed
consumer bindings, включая SDK source без fabricated SourceProjectFile. Overall
Complete выдаётся только после доказательства union coverage, не из наличия двух
непустых arrays. Gate проверяет same-session Complete existing snapshot; isolated
host дополнительно требует exact typed-source union equality с DTO/compiler,
однозначный source для каждого DLL, exact per-consumer bindings и closure hash.

`HydrateHostExperiment.AdmitAnalyzersAsync` сейчас требует AnalyzerItems.Any для
каждой DLL. Его future reviewed change обязан проверять discriminated envelope
union; просто удалить эту проверку или пропустить SDK pair нельзя. Production
CaptureService.Bind не используется как binder analyzer-free base, потому что
его контракт требует AnalyzerReferences/source project. Fresh isolated binder
должен отдельно доказать path+innerTFM+effective properties; ambiguous duplicate
path или unmatched instance => refusal до loader construction.

Exact new type names/signatures/schema/error limits для P021-2 ещё не объявлены:
полный executable packet будет только после закрытия перечисленных source guards.
Это явная граница design investigation, не placeholder разрешение для реализации.

Windows subprocess witness delta отозван из current implementation после validation:
focused21 с setup process дважды дал12child launch failures, diagnostic20 без setup
прошёл20/20 на том же binary. Root cause/security policy не доказаны. Coordinator
выбрал revert процесса; CreateReparsePointAsync удалён, Windows PowerShell не
запускается. Сохранён прежний symbolic-link-only setup: при expected unavailable
NotRun JSON содержит exact exception type/HResult/message; если link создаётся,
все root/ancestor/missing/child/recheck assertions остаются обязательными. Никакая
security policy или privileged/native workaround не применяется. R021C-04 process
lifetime path исчезает вместе с fallback. Reparse matrix subcase остаётся NotRun
в недоступной среде, а full positive task acceptance по-прежнему отсутствует.

P021-2 semantic review update: вариант, где SDK bindings существуют только в
isolated envelope, конфликтует с literal R021-08 placement полных bindings в
AnalyzerProvenanceSnapshot. Concrete proposed clarification D021-06 находится в
[decisions](task-02.1-decisions.md); **не принято** и не меняет normative files.
Предыдущий paragraph про возможный union Bindings внутри production snapshot тоже
не accepted contract: production Confirmed/source-project semantics требуют
truthful representation, которую нельзя получить фиктивными полями. Никакой
adapter C# по этому candidate не разрешён. Это новый ограниченный semantic gate,
не повтор A021-02 и не отмена продолжающегося independent finite source proof.

### A021-06 принято — 2026-10-04

Владелец выбрал typed envelope и exact R08 amendment из D021-06. Earlier semantic
pending paragraphs выше — история proposal, не current approval gate. Finite
normative write allowlist этого решения: task-02.1 R08 paragraph only (status/header
coordinator-owned), spec A02 clarification, cache-contract A02 clarification,
packets01/02.1 consumer seam, decisions. Production/C#/packet04 не менялись.
Current chosen representation: genuine production snapshot сохраняет native
items/bindings semantics; полный typed-source union и fresh consumer bindings
принадлежат isolated envelope. Existing gate обязателен вместе с отдельными host
union/closure/binding checks. Никаких fake fields/Confirmed и ослабления R07/M08/M10.
Positive executable packet пока не готов: source/host contracts продолжаются;
перед next C# нужны exact types/signatures/schema/allowlist и independent ACCEPT.

### Итог исполнения L021-1 — 2026-10-04

Coordinator принял только limited L021-1: final Release build exit0, focused21/21,
main1228/1228, same-reviewer C01–C04 CLOSED. Reparse actual subcase NotRun/Win321314;
PowerShell fallback отсутствует. Final raw hashes и сохранённые earlier failures
находятся в [evidence](../evidence/task-02.1-dependency-admission.md).
Это исполненный partial packet, не completed positive Stage A/R02101–09/remediation.
A021-06 binding representation принят, но его adapter ещё не реализован и
positive executable packet не accepted. L021-1 flags остаются false.

P021-2 additional provenance constraint: fresh revalidation не создаёт новых
MSBuild events. Genuine TaskOutput rows сохраняют реальные capture-time contexts
как source observations, с исходной provenance fingerprint; новые consumer ProjectId
и LoadSessionId относятся к fresh verified envelope. Нельзя представлять прежние
TaskId как task, запущенный в новой session. Exact positive schema обязан разнести
capture-time source identity и fresh binding identity и доказать релевантность
исходных facts повторной independent selection всех producers/inputs. Просто
заменить LoadSessionId в сохранённом snapshot по-прежнему запрещено.

Same reviewer `/root/independent_review` 2026-10-04: **ACCEPT limited normative
consistency A021-06** (task R08/spec/cache-contract/packets01/02.1/decisions).
Positive executable packet и source proof этим verdict не принимаются.
