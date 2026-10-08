# E0/task-06 — real cross-process round-trip и scoped .cs edit

Дата: **2026-10-08**, Europe/Moscow. Статус: **accepted, round 1** — принятый scope:
real new-PID ordinary capture/codec/hydrate и scoped linked .cs edit в H-001/H-002 границе.
Независимое [review round 1](task-06-review-round-1.md), Grok 4.7: **ACCEPT**, 0 must-fix,
0 исправлений после ревью. Владелец подтвердил приёмку и запросил commit. Исполнитель:
Codex (GPT-6; точный runtime model ID недоступен), текущая сессия, без subagents.
Исходный HEAD: `5678c2b1ce36f9abbc7ff93a541e299b00a1e5c5`; SDK `10.0.300`.

## Actual artifact и маршрут

Четыре новых test-only файла в `RoslynMcpServer.Tests/WorkspaceStateCache/`:
`CrossProcessRoundtripTests.cs`, `RoundtripProcess.cs`, `RoundtripObservation.cs`,
`RoundtripFixture.cs`. Production code/config/version не менялись. Используются
принятые ordinary capture, schema-v1 codec и isolated hydrator task-03/04/05;
cache/runtime activation и production write boundary не добавляются.

Parent через public `MSBuildProjectLoader.LoadProjectInfoAsync` получает ordinary
pre-overlay descriptors и помещает их в `AdhocWorkspace`. Actual
`OrdinaryWorkspaceCapture.CaptureAsync` создаёт DTO из этого workspace. Parent
сохраняет binary envelope actual codec и отдельное public ordinary observation,
затем **dispose ordinary workspace до запуска child**. `dotnet vstest` запускает
ровно один из этих же tests в новом testhost PID. Child читает envelope с диска;
ordinary load/capture в child-ветке не вызываются. Bootstrap registration не
является MSBuild workspace load. Hydrate path остаётся принятым task-05.

Handoff передаётся только через environment дочернего процесса. Parent проверяет
exit code, TRX **total=1 / passed=1 / notExecuted=0**, receipt PID отличается от
собственного. Timeout убивает process tree и дожидается завершения. Нет no-op worker
test, zero-test run не может дать pass. Повторный запуск не использует старые receipts:
каждый evidence directory имеет новый GUID.

## Реальный неизменённый контроль

`samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj`, включая загруженный
`RoslynMcpServer.csproj` и actual metadata/analyzer references. Expected semantic
facts заданы независимо от DTO: definition `RoslynMcpPlugin.SamplePlugin` в
`SamplePlugin.cs`, interface `RoslynMcpServer.Plugins.IRoslynMcpPlugin` из assembly
`RoslynMcpServer`, metadata type `Newtonsoft.Json.Linq.JToken`, отсутствие compiler
errors, `NullableContextOptions.Enable` и `OptimizationLevel.Debug`.

Ordinary/hydrated public observations сравниваются целиком: project identity/paths/
outputs/default namespace/CompilationOutputInfo, source/additional/analyzer-config
inventory, folders, source kinds, current text hashes, encoding/preamble/checksum,
parse и compilation option facts, directed edges, reference kind/aliases/
EmbedInteropTypes и analyzer paths. OptionFacts выполняет public Equals witness
с только тремя уже принятыми host-owned identity normalizations; external XML и
точный hidden strong-name state не выдаются за сохранённые. Все ProjectIds и
DocumentIds, включая additional/config, свежие. Host CurrentSolution пуст.

Raw requested и pre-open globals остаются **null**, evaluated Configuration/TFM
имеют actual **Debug/net10.0**, unknown-properties пусты. Child проверяет сохранённые
request/globals; every occurrence получает новый actual owner/document/producer ID,
сохраняя role, RoleSource и ProducerSource. Unknown не переклассифицируется.
Presence проверяется по current disk, не по capture stamp.

H-001 отдельно: source comment `Portable graph facts only` сохраняется;
`JToken.GetDocumentationCommentXml` пуст в hydrated metadata. Operation gate
отказывает metadata-documentation и documentation-analyzer до callback.
H-002 отдельно: emit/sign и unknown-write отказаны до callback. Это существующий
isolated gate, **не production fallback/log integration**.

## Scoped edit ordinary-loaded fixture

Два SDK проекта App → Library построены ordinary Debug, затем capture выполнен
из public loader descriptors. Один уже существующий `Shared.cs` имеет **два linked
source memberships и один additional membership**. Не создаётся expected synthetic
DTO. Три независимых child-процесса покрывают UTF-8 без BOM, UTF-8 BOM и UTF-16 LE BOM.

Child сначала подтверждает whole ordinary observation parity, затем выполняет
`SourceText.WithChanges` / public `Solution.WithDocumentText` для `Value = 7 → 19`
в обоих source memberships. Updated base compilation каждого owner повторно
проверяется `SnapshotSigningProfile` **до persistence**. Записывается один physical
`.cs` с loaded encoding/preamble. Exact bytes, Unicode comment `Grüße 世界`,
linked/additional memberships, roles и CheckOverflow=true сохраняются. Повторный
hydrate **из исходного envelope** читает edited bytes; обе base compilations дают
независимо ожидаемый constant **19** без errors. Это scoped isolated persistence,
не право писать через production session.

В каждом edit child current source затем отдельно меняется на AssemblyKeyFile
и unresolved assembly attribute. Hydrator отказывает signing-dependent и
signing-state-unknown до session return с stage/project/Debug/net10.0/route=refusal.
Workspace disposal notification получена; allocated assembly images реально
недоступны после disposal (`ObjectDisposedException`). Accepted edited bytes
возвращаются в finally. Ordinary negative controls дополнительно подтверждают
отказ capture для этих source states.

Generator control использует actual SDK JsonSerializerContext generator; ordinary
capture возвращает `generator-dependent-scope-unsupported` без snapshot. Multi-TFM
`net10.0;net10.0-windows` загружает несколько instances одного пути и получает
`ambiguous-project-instance` без snapshot. References/options/csproj не меняются
для прохождения позитивного real control.

## Validation и воспроизведение

Roslyn MCP Release solution build (`noIncremental=true`) **passed**. Остались
existing CS8603 в NuGetFallbackAssemblyResolver, CS8601 в HostSession и xUnit1031
в WpfTemporaryProjectCallbackTests; новых warnings в этих четырёх файлах нет.
Final focused `CrossProcessRoundtripTests`: **6/6 passed, 0 failed**, четыре child
TRX в каждом run имеют **1/1 passed, 0 notExecuted**.
Complete main CI suite `Category!=AnalyzerLifecycle`, Release, noBuild/noRestore=true,
timeoutSeconds=600: **1358/1358 passed, 0 failed / 0 skipped**. Passed=Total;
zero-test/timeouts/not-run не выдаются за pass. [Runner outputs](task-06-validation.json),
[четыре actual PID/codec/inventory observations](task-06-processes.json).

После independent review координатор перед acceptance/commit повторил Release
solution build (`noIncremental=true`) и complete main suite на текущем final code:
**build passed, main 1358/1358 passed, 0 failed / 0 skipped**. Код после ревью не менялся.
Повторные raw runner outputs сохранены в `postReviewBuild` / `postReviewMain`
[validation JSON](task-06-validation.json). Reviewer focused run также дал **6/6**.

AnalyzerLifecycle **not-run / not required**: production analyzer/shadow-copy/
load/prepare/publication, lifecycle host и shared test/build configuration не менялись.
Production C# не менялся; SourceStructure входит в обязательный complete main suite.
Exact structural limits task-05 не менялись.

Ранние outcomes сохранены честно: pre-test build not-started из-за неверных имён
DTO properties и tuple cast; первый executable run **0/4** из-за слишком поздней
MSBuild registration (исправлено constructor registration); затем **3/4**, когда
TFM oracle ожидал более поздний refusal, хотя actual ordinary loader дал ambiguous
instances; после уточнения assertion и additions — **6/6**, после decomposition
и final Release build — повторно **6/6**. Эти ранние failures не являются final pass.

Воспроизведение: Release build solution; `run_specific_test` с className
`CrossProcessRoundtripTests`, Release, noBuild/noRestore=true, timeoutSeconds=600;
затем `run_test_by_filter` с `Category!=AnalyzerLifecycle` на main test project.
Transient artifacts: `TestResults/task-06/<control>-<guid>/snapshot.bin`,
`ordinary.json`, `hydrated.json` (real), `edited.json` и `refusals.json` (edit),
`ordinary.ids`, parent/child PID, child stdout/stderr и `child.trx`.
Temp edit fixtures удаляются после завершения child, retained observations остаются.
Observation sidecars — test oracle/evidence, не portable DTO/store contract.

Benchmark timings/speedup/peak memory/Open/DTB counters **not-measured** (task-07).
Whole-request ordinary routing, production cache/write integration **not-run** (E1).
Task-06 принят после independent review и повторной coordinator validation;
task-07 и вся E0 пока не приняты.
Посторонние dirty WorkspaceLoadCache/tooling файлы не входят в scope; main suite
выполняется по текущему общему рабочему дереву.
