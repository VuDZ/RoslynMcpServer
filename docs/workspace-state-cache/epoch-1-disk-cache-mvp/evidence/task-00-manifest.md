# E1/task-00 — Manifest значимых входов

Дата: **2026-10-09**, Europe/Moscow. Исполнитель: Codex, сабагент
`manifest_implementer`; точный runtime model ID инструментами не предоставлен.
Статус отчёта: **blocked по результату S-002**; executable task-00 adapter не принят.
Основание: [task-00](../task-00-input-manifest-hi.md),
[spec](../spec.md), [правила исполнения](../../execution.md).
Task/README bookkeeping и окончательный review/validation принадлежат координатору.

## Проверенные исходные seams

Принятый [E0/task-07](../../epoch-0-snapshot-roundtrip/evidence/task-07-review-round-2.md)
предлагает real control `samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj`,
Debug / AnyCPU / net10.0, absent raw request и independently merged loader globals,
overlay off, query `RoslynMcpPlugin.SamplePlugin`. Его isolated roundtrip не является
manifest closure, production cache hit или приёмкой E1.

[WorkspaceInputMapBuilder](../../../../Services/Workspace/WorkspaceInputMapBuilder.cs)
содержит loaded source/additional/analyzer-config occurrences, metadata dependencies,
outputs, project paths и стандартные walk-up candidates. Shared path сохраняет
отдельные occurrences всех известных consumers. Walk-up names ограничены
Directory.Build.props/targets, Directory.Packages.props и global.json. Import/restore,
custom-task inputs и inclusion/removal rules на этом public surface отсутствуют;
regions имеют `CompletenessKnown=false`, required categories остаются Unknown.
Solution path сейчас получает только первого owner; будущий manifest adapter должен
сохранить всех потребителей solution evaluation, а не копировать это ограничение.

[OrdinaryWorkspaceCapture](../../../../Services/Workspace/StateCache/OrdinaryWorkspaceCapture.cs)
связывает public descriptors/live documents и portable
[SnapshotProjectKey](../../../../Services/Workspace/StateCache/Models/SnapshotProjectKey.cs).
Он заново оценивает проекты через ProjectCollection после ordinary load и сопоставляет
configuration/TFM с compilation attributes. Такое свидетельство не доказывает,
какие bytes imports были consumed исходным load. Его input map строится с
`loadGraphComplete=false`; walk-up/import/restore paths исключены из schema-v1
snapshot. Snapshot остаётся пригодным для принятого isolated E0 scope.

[AnalyzerProvenanceCaptureService](../../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs)
использует текущий BinaryLogger с caller-supplied `ProjectImports=None`, `Verbosity=Normal` и cleanup после replay. Effective remote verbosity не измерена.
Его analyzer bindings/toolset facts не являются полным input manifest.
Ни map generation Guid, ни session ID, ни стабильный post-load hash не доказывают P-004.

## Dependency blocker S-002

Фактический источник — [final S-002 report](task-11-raw-evidence.md),
[сводка](task-11/result.json) и сохранённые sanitized raw contexts.
Дата: 2026-10-09. Caller-supplied logger settings — None/Normal; effective remote
verbosity не измерена. Ordinary positive и required manifest positive различаются:

- Real SamplePlugin ordinary load и useful symbol успешны, но **228** raw import
  events относятся к referenced host, EvaluationId 4 / submission 0. Root plugin,
  EvaluationId 3, имеет ProjectStarted с пустыми Properties/GlobalProperties;
  root import inventory и ProjectEvaluationFinished отсутствуют.
- Controlled App + Library содержит **224** import events только Library evaluation
  4 / submission 0. Root App evaluation 3 не имеет required import/evaluation context.
  Nested/conditional/absent/shared/external imports видны в Library, не во всех consumers.
- Дополнительно actual dependent build context 13 / submission 1 имеет EvaluationId
  **9**, тогда как imports/evaluation-finished относятся к EvaluationId **4** /
  submission 0. Совпадение physical ProjectFile и числового ProjectInstanceId=3
  не связывает imported inventory с final dependent compilation instance.
  [Real raw](task-11/real/capture.binlog.json),
  [controlled raw](task-11/controlled/capture.binlog.json).
- Redirected **assets** действительно видны для обоих controlled consumers через
  actual TaskParameter TaskInput `ProjectAssetsFile`, bound к ProjectStarted contexts:
  App context 4 / instance 2 / submission 0 и Library context 13 / instance 3 /
  submission 1. Отдельная Library evaluation property не является единственным
  источником assets и не заменяет этот actual consumer binding.
- Library lock path виден только как `NuGetLockFilePath` в evaluation 4; property
  не доказывает consumed lock bytes и не покрывает root lock. Required NuGet config
  inventory отсутствует: `RestoreConfigFile` не виден даже у Library; preparation
  restore и существование NuGet.Config/locks не доказывают design-time closure.

Current channel **partial**, а для finite supported SamplePlugin manifest
**insufficient**. MissingImportConsumers — path-level negative evidence check,
показывающий root gap; это не all-instance admission и не proof dependent
EvaluationId 9 closure. Useful ordinary query подтверждает загрузку, но не
required category/path/all-instance coverage. Supported real manifest отсутствует;
incomplete → whole-request unsupported. All-unsupported не закрывает task-00/E1.

Actual [Roslyn logger seams](task-11/roslyn-logger-seams.json) объясняют boundary:
MSBuildProjectLoader передаёт распознанный BinaryLogger через BinLogPathProvider
удалённому BuildHost. BuildHost ProjectCollection создаётся без evaluation loggers;
BinaryLogger подключается к BuildParameters/BeginBuild позже root evaluation.
Именно этот observed boundary учитывается в verdict, не утверждение, что None
вообще не даёт raw paths. Custom ILogger lifecycle wrapper после observed gaps
проверен отдельно: callback lifecycle пуст, файлов нет; произвольный wrapper этим
remote маршрутом не поддерживается и не восстанавливает all-instance evidence.
Sequential overwrite при повторном Initialize не доказан и не служит rationale.

Task-00 **blocked** по явному Depends on; source channel correction не выбрана
автоматически. Второй log/Embed/source collection и production provenance change
не вводятся. Caller-supplied `generationBound=true`, current properties/snapshot
или host-only paths не устраняют этот blocker. [Независимое review round 1](task-11-review-round-1.md)
оценивает bounded partial S-002 и blocked task-00; [финальная validation](task-11-validation.md)
фиксирует проверку actual test-only artifact, не поддержанный manifest adapter.

## Ограниченный inventory будущего manifest

Это предложение формы данных для продолжения после достаточного S-002;
executable DTO/capture adapter и supported profile этим отчётом не поставлены.
Один physical path должен иметь все portable occurrences/owners с source evidence,
role, membership kind, presence и только доказанный producer. Existing Unknown
остаётся Unknown; EvaluationInput имеет более строгий приоритет при нескольких ролях.

Нужные facts в одном manifest: profile/schema policy; independently current raw request
и loader globals с absence; project instance keys; positive inputs; standard
known-absence candidates; membership regions с discovery boundary/owners;
metadata/analyzer/generator binary identities и declared binary validation policy;
SDK/MSBuild/Roslyn/version-resolution fingerprint; environment policy/digest;
portable load-health/coverage/eligibility и происхождение bounded diagnostics.
Runtime watcher state, Roslyn IDs, session generation, shadow paths, execution
provenance и source/generated text не становятся portable state.

Actual import/restore entries должны происходить из accepted instance-bound S-002
source. Assets, lock/NuGet configs и nuget.g.props/targets нельзя подменять csproj.
Explicit inputs из obj, output-pruned trees и external linked paths проверяются
точечно независимо от membership pruning. Unknown custom input/import/TFM, budget
или I/O failure отвергают весь request. Runtime-only config/json не включается
автоматически; actual compiler/additional/evaluation роль обязательна независимо
от расширения. Один shared input сохраняет всех consumers, не первого owner.

SDK 10.0.300 `Roslyn/Microsoft.Managed.Core.targets`, lines 139–146, вычисляет
ancestor directories всех Compile paths. При соответствующих discovery flags он
ищет **и .editorconfig, и .globalconfig** в этих directories. Будущий finite profile
должен покрыть presence/absence от project и linked sources до filesystem root
либо другой доказанной границы, а также explicit config paths и SDK selected
analysis configs. `.editorconfig` имеет per-source applicability/root rules;
`.globalconfig` имеет project-wide applicability, не такую же stopping policy.
Repository root не является общей границей. Новые и удалённые configs должны
обнаруживаться до первого hit. Existing document hashes discovery не заменяют.

Консервативный environment fingerprint может хешировать sorted environment
name/value pairs и current working directory без сериализации raw values/secrets.
Capture и lookup должны вычислять его одной policy независимо от saved defaults.
Unknown semantic dependencies → unsupported; fingerprint не доказывает closure
произвольных tasks, property functions или внешних сервисов.
Portable graph-health не повышает Unknown до Complete из-за пустых hydrate diagnostics.
H-001 external XML exclusion и H-002 signing-independent base-query scope сохраняются.

## Отдельная граница P-004

MSBuild 18.6.3 local API docs для ProjectImportedEventArgs описывают imported path,
unexpanded expression и ignored status; ProjectEvaluationFinished описывает final
properties/items/profiler. Это не consumed-byte hashes. Raw availability и graph
bytes binding остаются разными основаниями.

Bounded candidate для дальнейшей проверки: discovery load только определяет set;
existing required inputs удерживаются read handles без Write/Delete sharing **до**
нового ordinary reload; использовать только graph этого reload и требовать, чтобы
каждый actual consumed positive path из полного evidence уже был guarded.
Newly discovered dependency/root → bounded retry либо capture refusal. Stable
post-load hashes и повторная evaluation прежнего graph такой порядок не заменяют.
Generated obj inputs могут потребовать write во время DTB: sharing violation должна
оставить обычную загрузку успешной и запретить capture, а не разрешить unsupported hit.

Это пока **не доказанный producer**. Для membership/known absence нужен closed finite
profile: transient новый файл, повлиявший на supported graph, обязан оставить
unguarded actual import/document/config evidence и вызвать refusal. Hidden custom
Exists/file reads, directory-sensitive evaluation и SDK resolution без такой evidence
остаются unsupported. Pre/post equality не закрывает ABA. Альтернатива retained
pre-consumption watcher journal требует доказанного startup/error/overflow/drain
contract; existing post-load watcher session его не предоставляет. Ни lock/reload,
ни новый journal в этой задаче без достаточного S-002 не внедряются.

## Validation и продолжение

В scope task-00 production/test C# и конфигурация не изменены; версия не повышена,
cache не активирован, commits отсутствуют. Полный code style прочитан до любых
потенциальных C# edits; pre-existing WorkspaceLoadCache/tooling dirty scope не изменялся.

Координатор выполнил после последней S-002 C# правки Release build **решения** и полный
main suite `Category!=AnalyzerLifecycle`: **1360 total / 1360 passed / 0 failed**,
skips отсутствуют. SourceStructure входит в main suite, exact structural inventory
не изменялся. AnalyzerLifecycle **not-run / not required**: production
load/prepare/analyzer/publication, lifecycle host/config не менялись.
[Validation report](task-11-validation.md) содержит actual runner evidence.
Эти passes подтверждают поставленный bounded test-only experiment и регрессии;
они не создают отсутствующий executable task-00 adapter или positive capture.
[Review round 1](task-11-review-round-1.md): bounded partial S-002 принимается,
**task-00 BLOCKED**, без implementation acceptance и public activation.

Task-00 completion требует достаточного S-002 source, executable manifest DTO/capture
adapter с доказанным producer, минимум одного unchanged real supported request,
negative incomplete/unsupported controls и применимой финальной validation/review.
Текущий результат выполняет исследование boundary и фиксирует blocker; он **не**
заменяет эти обязательные результаты и не разрешает downstream reusable envelope.
Task/index status и ссылки на review/validation синхронизирует координатор.
