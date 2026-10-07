# E0 execution contract — current baseline, experiments and consumers

Статус: **design contract; task-01/task-02 isolated research accepted 2026-10-03;
positive admission/equivalence не доказаны**. Это output task-00,
не production implementation, не acceptance эпохи и не новый admission profile.
Проверяемый current baseline и его ограничения — [audit](current-baseline-audit.md).
Исторический pin `9867318ddb5294ce144bf024b9a61a1a2e3814c3` / 1.3.21 сохраняется;
проектирование привязано к HEAD `07859db586c68452771c88ba76409bb78e0e289f`, source 1.5.4.
Roslyn packages 5.9.0, Microsoft.Build packages 18.6.3, target net10.0 подтверждены
csproj. Версия процесса, установленный SDK, dirty inventory и выполненные checks
фиксируются отдельно в audit/отчёте task-00; package version не доказывает runtime.

## 1. Последовательность и границы

Task-01 реализует изолированный host и общие experiment models; task-02 —
изолированное dependency evidence. После обоих task-02 вносит observed findings
в выделенный раздел packet-03. Только после приёмки этого дополнения запускается
task-03; затем task-04, затем task-05. Ни один шаг не меняет production C#,
MCP parameters, default, feature gates, reader/store protocol или normative spec.
U-ARB-03 не блокирует технические fixtures; performance без заранее утверждённого
budget не измеряется и остаётся deferred. O7 не даёт cache go.

Все C# declarations ниже и в packets — **proposed**, кроме явно обозначенных
existing APIs. Пакеты — полные границы работы, а не заявления о существовании файлов.
Producer интерфейса меняет все affected packets до dispatch потребителя. Ни одна
неопределённость результатов spike не поручает Luna выбирать архитектуру.

## 2. Current paths, которые нельзя заменить исторической моделью

- `Services/Workspace/SolutionManager.cs`: `LoadAndPrepareAsync` уже охватывает
  load/prepare acquisition и publication gate. `LoadCoreAsync` всё ещё dispose
  old до ordinary Open; `_workspace` имеет тип `MSBuildWorkspace`. Candidate
  retention и общий host ownership — delta E1, а не часть E0 experiment.
- `WorkspaceWriteBoundary.Preflight` и exact inverse уже проверяют session,
  raw base, admission/mapping. `ApplySolutionChangesToDiskAsync` и
  `PersistDocumentChangesAsync` ведут manager write boundary. E0 вызывает
  existing preflight/persistence из isolated adapter; не копирует их логику.
- `WorkspaceInputSession`, `WorkspaceInputReconciler`, `WorkspaceDiskEventClassifier`
  уже владеют revisions/own-write/coverage. `CreateRegions` с
  `completenessKnown=false`, unknown imports/restore/external globs/custom tasks
  не является evidence sdk-project-v1. E0 не создаёт второй production counter.
- `WorkspaceNonCSharpDiskSync` и published accessor сохраняют additional/config
  texts отдельно от raw workspace. Reconciliation не вызывает writer для них.
- `FlushDirtyDocumentsUnderLockAsync` снимает pending lists до await;
  cancellation retention — отдельная E1/E3 delta, не уже доказанная гарантия.
- WPF `QueueDiskPath` исключает wpftmp лишь вне LoadedProjectPaths; существующие
  WPF tests остаются regressions. WPF не объявляется supported E0 SDK fixture.
- `WorkspaceFilePersistence` пишет фактические payload bytes с BOM и регистрирует
  begin/commit/abandon. Сохраняются all-memberships и newer external overwrite.

## 3. Два независимых inventory

### 3.1 Semantic hydration DTO

DTO schema experiment `e0-semantic-1` содержит только values. Ни Solution,
ProjectId/DocumentId, session ID, shadow path, SyntaxTree, SourceText, source или
generated text в DTO нет. Все runtime IDs создаются заново при hydrate.

1. Request: абсолютный workspace path, nullable Configuration/Platform/TFM
   с отсутствием, различимым от explicit; canonical roots и graph scope;
   metadata mode. Источник — фактические аргументы вызова, не inferred defaults.
2. Project instances: canonical project path + exact inner TFM + effective
   allowlisted properties; name, assembly, language, output/output-ref paths.
   Источники — public `Solution.Projects`, `Project` properties и independently
   resolved MSBuild instance/edge evidence. `Project.Name` не источник TFM.
3. Directed project references: обе portable instance keys, aliases,
   EmbedInteropTypes. Источник — `Project.ProjectReferences`, привязанный к
   exact instance mapping task-02. Outer instance не становится inner instance.
4. Compile/additional/config membership: instance key, role, absolute path,
   name, folders, source kind, generated-on-disk marker, decoding policy.
   Public `Project.Documents/AdditionalDocuments/AnalyzerConfigDocuments`,
   `Document.SourceCodeKind`, `TextDocument.GetTextAsync`; один path хранит все
   memberships. SDK-generated compile/config в obj остаются disk inputs.
5. Parse/compilation options: public CSharp option values с closed property
   inventory, описанным в packet-01. Unknown/nonrepresentable options отвергаются;
   silent defaulting запрещён. Analyzer-config providers воссоздаются workspace
   из config documents; runtime service object не сериализуется.
6. Metadata references: absolute file, assembly/module kind, aliases,
   EmbedInteropTypes, documentation sidecar identity. Источник — public
   PortableExecutableReference/MetadataReferenceProperties и disk bytes.
   Non-file reference без portable representation — unsupported.
7. Analyzer identity: consumer path, inner TFM, original full DLL path,
   content hash, SDK/pack directory и transitive DLL identities. Источники —
   public AnalyzerReference.FullPath, compiler inputs, TaskOutput provenance,
   independently resolved directories и прямое чтение DLL. Наличие path в DTO
   не разрешает DLL loading. Task-02 решает readiness после повторной проверки;
   явный analyzer вне TaskOutput не overlay-ready.
8. Encoding: `SourceText.Encoding == null` сохраняется как null; при nonnull —
   codepage, preamble bytes, decoder/encoder fallback kind и replacement text.
   Bytes читаются с диска независимо. Roslyn decoding не подменяется
   `File.ReadAllText` с UTF-8 default. Непредставимый custom fallback — unsupported.

DTO не источник dependency completeness. DTO options/reference equality сама
по себе не доказывает encoding, generated output или graph completeness.

### 3.2 Dependency/admission evidence

Каждая запись несёт category, source API/version, owning project/inner TFM,
canonical path/region, SHA-256 либо explicit absence, и locator raw evidence.
Source version — SDK/MSBuild/Roslyn и detector `sdk-project-v1/e0-1`.

- Positive imports: `ProjectImportedEventArgs.ImportedProjectFile` из DTB
  binlog, непустой full path; hash direct bytes. XML root/imports читаются public
  `System.Xml.Linq` с запретом внешних сущностей. `ProjectImports=None` достаточно
  лишь после воспроизведения task-02, не по пересказу старого spike.
- Known absent: событие с UnexpandedProject и пустым ImportedProjectFile;
  resolve base/condition/Exists operands до bounded canonical path. Не использовать
  ImportIgnored как доказательство. Неразрешённое property/condition — unknown.
- Membership regions: include/remove XML проекта и реально импортированных
  props плюс стандартные SDK rules. Каталог проекта, не solution/monorepo root.
  Для Razor/Web также `**/*.cshtml`, `**/*.razor`, `wwwroot/**`, включая hidden
  `.well-known`. Enumerated matches не заменяют пустой потенциальный region.
- SDK/toolset: полный путь Sdk.props из imports, resolved SDK directory,
  runtime MSBuild identity, hashes/identity input set. Saved fingerprint не
  сравнивается только сам с собой. Packs разрешаются отдельно от SDK.
- Restore/reference inputs: assets, NuGet configuration и package DLL/content
  paths сверяются с фактическими compiler inputs. Не найденный supported source
  closure даёт unknown; наличие assets JSON не является доказательством полноты.
- Generated-on-disk compile/config: compiler inputs, file hashes и producer
  identity. obj не исключается из explicit probes. SDK producer закрывается
  SDK identity; custom Target producer — unknown.
- Custom Target в проекте/import вне SDK: unknown по конкретному правилу spec;
  узнаваемый non-SDK project или escaping/unbounded glob — unsupported.
  Обобщённый список unsupported в spec не превращает custom Target в supported.
  Оба результата отвергают request целиком до hydrate/DLL/publication.
- Expected roots/instances/edges/compiler inputs: request roots + MSBuild
  project graph/evaluated instances и compiler command/items в task-02;
  сверяются независимо с Roslyn inventory. Missing root/reference, soft warning,
  ambiguous TFM, незакрытая category — unknown, даже если open вернул Solution.
- Linked external .cs: явный canonical dependency и все memberships плюс
  positive external import; если bounded evidence отсутствует — negative
  admission. Профиль не расширяется внешним glob.

Ограничения источников сохраняются по категориям: binlog не показывает произвольное
чтение из task DLL, ProjectStarted.Properties может быть пуст, runtime context IDs
не переносимы. Evaluation, binlog, compiler arguments и XML дополняют друг друга;
ни один источник не объявлен полным по умолчанию.

## 4. Operation/capability matrix

| Операция | Adhoc capability | Writer/preflight | Outcome, partial, reload |
|---|---|---|---|
| Read/semantic | CurrentSolution, compilation, generated documents | Никто; admission до использования DLL | Ready только после полного prepare; missing output failed |
| Existing .cs text | Solution.WithDocumentText + TryApplyChanges | Existing preflight, затем WorkspaceFilePersistence; все memberships | SavedPaths только committed; partial не rollback; reload не нужен |
| SDK glob add .cs | AddDocument memory | Preflight + bounded create, исключить existing collision/Remove | Success при всех memberships; отказ до bytes при unsupported |
| SDK glob remove .cs | RemoveDocument memory | Preflight + delete known file; begin/commit own-write adapter semantics | Partial при ошибке; reconcile committed paths, не обещать rollback |
| SDK glob rename .cs | Remove + add memberships | Preflight обоих путей, create new затем delete old | Два успешных пути сообщаются; при delete failure partial, оба файла видимы |
| Explicit Compile без glob | Host может менять память, но этого недостаточно | Никаких writes | Unsupported до записи; нет csproj writer |
| PackageReference / rename_project | Existing helper вне TryApplyChanges | Production helper, вне E0 experiment | Graph stale, следующий ordinary load; E0 не вызывает helper |
| Analyzer reference | Только memory после admission | Нет .csproj writes | Base без overlay при recheck failure; original/shadow exact inverse |
| Known disk .cs reconciliation | Replace text всех memberships | Никто | No DTB, no csproj write; missing не recreate |
| Additional/config reconciliation | Отдельный published snapshot | Никто, не TryApplyChanges writer | Все memberships; следующая compilation видит новый text |
| Unknown graph/event/overflow | Нет доказанного content-only update | Write preflight запрещает stale/unknown | Evidence untrusted; production E1/E3 решает recovery |

CanApplyChange=true не является persistence contract. E0 отдельно проверяет,
что Adhoc.TryApplyChanges сам не изменяет файлы. Writer не выбирает произвольный
первый DocumentId при linked source, и не пишет .csproj даже при add/remove.

## 5. Independent oracle и счётчик DTB

Три независимых lifetime: A = ordinary capture, B = Adhoc hydrate из value DTO,
C = fresh MSBuild oracle. A закрывается до B; C не получает ни DTO, ни graph A/B,
ни тексты их snapshots. C открывает тот же стабильный fixture disk request с
теми же effective properties/toolset. Перед/после каждой стадии hashes всей
admitted input closure обязаны совпасть. Изменение дерева — invalid attempt,
а не допускаемая нормализация. Разные IDs нормализуются по portable instance
и membership keys. Пути нормализуются только для разрешённых temporary roots;
имена, aliases, options, encoding, graph edges и generated texts не маскируются.

Сравнение включает полный graph, full option vectors, all-memberships,
characters/encoding/write-back bytes, metadata/project references, diagnostics
(ID, severity, invariant message, normalized location), symbols/constant values,
полный generated document set и каждый text. `GetSourceGeneratedDocumentsAsync`
вызывается в обеих ветках; отсутствие обязательного output — failed/not-run.
Rejected generator проверяет admission и никогда не даёт passed equivalence.

DTB observability разделяет четыре величины: Open* invocation count, MSBuild
ProjectStarted count, CoreCompile target count и distinct design-time project
contexts. Последний — distinct (binlog ordinal, submission, node, project context,
project instance), где design-time flag подтверждён properties/request association.
TargetStarted не отождествляется с отдельным DTB; повтор target учитывается
отдельно. Restore/ordinary build/capture/oracle/hydrate имеют разные stage labels.
Counters получают события через public ILogger/BinaryLogger и
BinaryLogReplayEventSource; invocation count живёт в owned runner.

Если logging coverage оборвалось, нет флага/association, subprocess не покрыт
или replay failed, `DtbMeasurement.DesignTimeContexts=null`, `Complete=false`.
Нельзя записать 0 по отсутствующему логу. Для hydrate наблюдение ограничено
owned host: отсутствие любого Open*/process launch + structural analysis
transitive experiment calls + успешная semantic работа. Zero относится к этому
closed path, а не всему процессу. Отдельный synthetic negative witness, намеренно
вызывающий ordinary open, обязан обнаруживать nonzero. Source-text regex не
является structural proof; применять SourceSetAnalysis или execution hooks.

## 6. Reproducible sdk-project-v1 spike — owner task-02

На момент task-00 raw harness/binlogs заявленного в spec spike не были найдены.
Execution update 2026-10-03: [task-02](evidence/task-02-dependency-admission.md#независимая-приёмка)
принят как isolated research с воспроизводимыми raw artifacts и отрицательными
findings. Positive closure/new-session overlay остаются недоказанными, task-03
positive dispatch — hold. Это обновление статуса, не изменение контрактов ниже.
Task-02 создаёт isolated fixture roots под task-owned temp directory. Фиксирует
source HEAD/dirty, process executable/version, `dotnet --info`, `dotnet --list-sdks`,
MSBuild runtime path/version, Roslyn assembly versions, OS/path comparer, commands,
fixture bytes/hash manifest, exit code, stdout/stderr и неизменные raw binlogs.
SDK 10.0.300 pin + rollForward disable; отсутствие SDK означает not-run.
Package versions 5.9.0/18.6.3 не меняются в production ради spike.

Порядок: restore один раз вне DTB измерения; затем для каждого fixture обычный
build и design-time build с `DesignTimeBuild=true`, `SkipCompilerExecution=true`,
`ProvideCommandLineArgs=true`, каждый с `ProjectImports=None` и `Embed`.
Команда формы `dotnet msbuild <fixture.csproj> -t:Compile -p:DesignTimeBuild=true
-p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true
-bl:<absolute.binlog>;ProjectImports=None` передаёт каждый аргумент отдельно
через ProcessStartInfo.ArgumentList (shell semicolon не разбивает аргумент).
Ordinary control — `dotnet build <fixture.csproj> --no-restore -c Debug` с таким
же отдельным logger argument. Дополнительно ordinary MSBuildWorkspace Open*
с ILogger: отдельный evidence источник, CLI DTB ему не приписывается.
Для multi-target каждый inner TFM имеет отдельный command и exact mapping.

Матрица fixtures: two-project reference; absent Local.props/Exists → появление;
positive Directory.Build.props/targets; excluded `secret/**/*.cs`; пустой
`missing-dir/**/*.txt`; explicit obj generated compile/config; custom target
до CoreCompile, включая task/Exec с невидимым внешним input; linked external source;
explicit Analyzer вне TaskOutput; два inner TFM/defines; Razor/Web templates и
wwwroot/.well-known; NuGet static asset вне SDK/project; missing reference/root;
legally empty project. Для target detector выполнять XML inventory и compiler
input comparison, не только replay. Secrets/global environment не выгружать:
allowlist = DTB flags, Configuration, Platform, TargetFramework.

Два fresh runs должны проверить, что повторяемый ProjectInstanceId не portable
identity. Отдельный новый session создаёт overlay evidence только после независимого
SDK/pack re-resolution + disk hash DLL. Same-path replacement, unreadable DLL,
ambiguous instance, corrupt capture → no overlay. Вызов production gate здесь
не добавляется; эксперимент передаёт newly constructed snapshot существующему
gate только через existing test seams, либо честно пишет integration not-run.

Task-02 отдаёт `DependencyEvidenceRunner` contract ниже и отчёт
`evidence/task-02-dependency-admission.md`, inventory артефактов и hashes.
Raw binlogs хранятся как task evidence artifacts с точными locator/hash;
не коммитить несанифицированные binlogs с environment properties.
Перед task-03 author task-02 заполняет раздел Findings в packet-03: фактические
inner mappings, marker/output identities, admitted/rejected cases, decoding,
SDK prerequisites, evidence paths и failed/not-run. Expected contract выше
сохраняется; расхождение возвращается task-00 owner, не переписывается молча.

## 7. Общие proposed declarations и ownership

Namespace всех experiment types: `RoslynMcpServer.Tests.WorkspaceLoadCache`.
Root `RoslynMcpServer.Tests/WorkspaceLoadCache/`; data types в `Models/`,
logic рядом. Один тип/файл. Task-01 владеет shared declarations packet-01,
включая Models/AdmissionEvidence.cs. Task-02 владеет новым
DependencyEvidenceRunner.cs (никаких production edits):

```csharp
internal sealed class DependencyEvidenceRunner
{
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
}

internal sealed record AdmissionEvidence(
    string ProfileVersion,
    string Disposition,
    ImmutableArray<EvidenceEntry> Entries,
    ImmutableArray<RegionEntry> Regions,
    ImmutableArray<InstanceKey> ExpectedInstances,
    ImmutableArray<ProjectEdge> ExpectedEdges,
    ImmutableArray<string> ExpectedCompilerInputPaths,
    ImmutableArray<string> Reasons,
    bool AnalyzerInventoryComplete,
    bool OverlayReady);
```

`AdmissionEvidence`, `EvidenceEntry`, `InstanceKey`, `ProjectEdge`,
`ExperimentRequest` создаёт task-01.
Disposition строго `supported|unsupported|unknown`; unknown имеет Reasons.
Expected arrays не default, duplicates запрещены; отсутствие root/instance не
маскируется empty. OverlayReady не следует из supported.
AnalyzerInventoryComplete не выводится из DTO: supported требует независимо
покрытого compiler analyzer inventory каждого ExpectedInstance, включая явно
пустые списки и dependency closure. До empty-analyzer shortcut hydrate проверяет
точное равенство DTO analyzer/dependency inventories с evidence по packet-01;
удаление всех analyzer entries не превращает request в analyzer-free.
Snapshot не содержит
LoadSessionId. InspectAsync не загружает analyzer DLL и не hydrate. Он не пишет
source/project files; MSBuild subprocess может выполнять targets/tasks и менять
obj/generated files. Поэтому runner работает только с owned temp fixtures,
фиксирует эти side effects, затем устанавливает stable input manifest. Mutation
после этого manifest отвергает attempt. artifactDirectory принадлежит runner,
fixture root временно эксклюзивно предоставлен caller. Cancellation — OCE с тем же token;
I/O/toolset/evidence failure возвращает unknown с bounded reason, programming
contract errors — ArgumentException. Caller владеет artifact directory;
runner закрывает processes/loggers до возврата, shared lock не требуется.

RegionEntry/RegionMember, category allowlist и точный glob/manifest protocol
определены packet-01 и потребляются task-02 без изменения. RevalidateOverlayAsync
реализует delegate ExperimentAnalyzerAdmission из packet-01; никаких новых DTB,
Open* или процессов в этом методе нет. InspectAsync сохраняет в owned runner
проверенные portable provenance facts из raw capture; каждый факт связывает
consumer project path/inner TFM/DLL с TaskOutput и exact compiler inputs, SDK/pack
resolution и dependency hashes. Revalidate заново получает текущую resolution
из доступных public SDK/toolset resolution источников и disk evidence без build,
читает DLL bytes и rebinds consumer к новым ProjectId baseSolution. Невозможность
независимого resolution без DTB даёт null, не доверие сохранённому пути.

После полного совпадения создаётся новый AnalyzerProvenanceSnapshot из этих
revalidated facts с newSessionId, новыми bindings и текущим toolset; сохранённый
snapshot никогда не меняется через `with { LoadSessionId = ... }`. Constructor
и внутренние record types уже находятся в
Services/Analyzers/AnalyzerProvenanceCaptureService.cs; не переносить их в новый
production file. Empty/fabricated Complete snapshot запрещён. Все analyzer
compiler paths должны иметь доказанный TaskOutput identity; missing explicit
Analyzer, ambiguity, dependency gap, DLL mismatch/unreadable, SDK/pack mismatch
или OverlayReady=false возвращают null до загрузки DLL. Нельзя задавать Complete
только потому, что checksum совпал. Контракт метода: caller owns immutable inputs,
result immutable fresh evidence, no DLL execution/locks, OCE propagates token,
I/O/evidence failure => null; contract misuse => ArgumentException. Отчёт task-02
содержит причину отказа и evidence. Это experiment seam, не production publication.

## 8. Regression map и принятие результата

- V01: two-project graph/edges/navigation → packet-03 graph fixture и packet-04;
  current `SolutionManager.LoadAndPrepareAsync`, existing path resolution tests.
- V02: parse/nullable/defines/bytes → packet-03 encodings, packet-01 writer;
  `WorkspaceWriteEncodingTests`, `SourceTextEncodingTests`.
- V03: exact inner contexts → task-02 evidence, packet-03 MultiTarget,
  packet-04; current `SolutionProjectTargetResolverTests`.
- V04: additional/config/generator → packets-03/04;
  `NonCSharpDocumentTextSyncSpikeTests`, `WorkspaceNonCSharpDiskSyncTests`.
- V05: explicit obj compile/config hashes → task-02 + packets-03/04;
  `WorkspaceInputMapTests`, role classifier не доказывает closure.
- V06: linked all-memberships/external import → task-02 + packets-03/04;
  `WorkspaceSharedSourceSyncTests`, `WorkspaceInputFreshnessTests`.
- V23: new PID/RAM/reset/false→true/force distinction → future E1/E2 production
  lifecycle fixtures, current `SolutionManagerPassedLoadArgsTests` и
  AnalyzerLifecycle publication tests. E0 records capture/oracle/hydrate sessions
  отдельно и не заявляет реальный production cross-process disk hit.
- A-LOAD cancellation/failed candidate; A-WRITE stale mapping/partial;
  A-STICKY/A-ADMISSION banned/unavailable; watcher newer event retention;
  WPF callback и BOM own-write остаются required downstream regressions.

E0 outcomes по каждому scenario: `passed|failed|not-run`, category отдельно
`positive-equivalence|negative-admission|capability|metadata`. Unsupported никогда
не positive-equivalence. Reports включают commands, exit codes/test counts,
raw locator, versions, gaps. До acceptance code/test работы coordinator строит
Release solution, main `Category!=AnalyzerLifecycle`; lifecycle дополнительно
при изменении его paths/config с env `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`.
Zero tests/skips/timeouts не pass. Сам этот docs-only contract suite не запускает.

## 9. Принятые уточнения task-02.1 — 2026-10-04

Владелец принял [A021-01/02/03 и D021-04/05](design/task-02.1-decisions.md);
нормативные границы — [дополнение spec](spec.md#уточнение-профиля-и-provenance--решение-владельца-2026-10-04).
Они уточняют §§3.1/3.2/6/7: workload extension отделено от SDK directory,
SDK-evaluated-item — distinct provenance source; strict finite source-backed closure
package-free exact-pack subset обязателен. Старые TaskOutput-only observations
остаются historical; explicit non-provenance DLL по-прежнему отказ. Нельзя
создавать фиктивные CapturedAnalyzerProvenanceItem/task contexts для SDK XML item.

Shared host может потребовать изолированный adapter/model change по executable
packet: production gate остаётся unchanged и сам не доказывает closure. Independent
portable source facts, fresh consumer bindings и complete analyzer/dependency
inventory проверяются до загрузки. Existing OwnedAnalyzerLoader host resolution
должна быть проверена по actual binary references; production host catalog не
подменяет её. D021-04 exact graph/rebinding обязателен; D021-05 оставляет Razor/Web
negative. Task-04 positive stages2–7 и observation API remediation сюда не входят.

[Source audit/finite contracts](design/task-02.1-source-contracts.md) и replacement
implementation packet должны получить independent review до C# edits. Эти owner
choices не являются доказательством admission или разрешением production changes.

### Уточнение ordered workload selection — 2026-10-04

Принятая A021-03 запрещает NuGet/restore/reference fallback, downloads и overrides;
SDK выбирается exact pin с `rollForward:disable`. Штатный ordered выбор workload
manifests по feature-band входит в A021-01 только как доказанный version-specific
algorithm. Название upstream `FallbackForMissingManifest` не исключает эту ветвь
и не разрешает произвольный fallback. Для текущей установки отсутствие primary
`10.0.300`, выбор secondary `10.0.100`, installstate, workloadsets, installer/userlocal
markers и все более приоритетные положительные/отрицательные selectors входят
в independent no-build re-resolution, повторный probe и mutation matrix.
