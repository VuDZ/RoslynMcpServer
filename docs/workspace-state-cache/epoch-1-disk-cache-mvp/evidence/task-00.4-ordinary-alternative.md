# E1/task-00.4 — ordinary load: bounded alternative research

Дата: **2026-10-09**, Europe/Moscow. Исполнитель: Codex implementer subagent,
унаследованный runtime; точный model ID и reasoning **not confirmed**. Recommended
GPT-6.1 Sol / medium не подставлены как фактические. Коммитов нет.

Scope: analysis существующих E0 raw measurements, текущего ordinary pipeline и
focused EventPipe profiling одного заранее выбранного реального request. Production
оптимизация, C# tests/harness, existing runner, версии и defaults не изменены.
Новый `profile-command.txt` — отдельный diagnostic driver в evidence, импортирующий
existing ordinary runner без изменения его файла; это measurement tooling, поэтому
итоговую применимую build/main validation выполняет координатор.

## Решение и границы

**No-go для выбора implementation оптимизации на имеющихся outer timings.**
Они показывают стоимость ordinary load и первого semantic вызова, но не выделяют
стоимость MSBuild open, replay, watcher/input map, publication, disk sync и first-use
compilation. Focused profiling ниже проверил этот пробел, но не доказал конкретный
critical-path hotspot. Возможная отсрочка disk restore остаётся предложением,
не принятой owner decision. Task-00 manifest остаётся blocked.

Обязательные входы прочитаны: [task](../task-00.4-ordinary-load-alternative-med.md),
[spec](../spec.md), [E1 index](../README.md), [series](../../README.md),
[execution](../../execution.md), [benchmark](../../benchmark.md),
[manifest task](../task-00-input-manifest-hi.md),
[E0/task-07](../../epoch-0-snapshot-roundtrip/task-07-roundtrip-benchmark-med.md),
[accepted task-07 report](../../epoch-0-snapshot-roundtrip/evidence/task-07-benchmark.md),
[round 2 / validation](../../epoch-0-snapshot-roundtrip/evidence/task-07-review-round-2.md),
[S-002 task](../task-11-raw-import-restore-evidence-hi.md),
[accepted S-002 validation](task-11-recheck-validation.md).
S-002 ordinary semantic positive не доказывает full all-instance imports/restore
или P-004 consumed-generation binding. Стоимость этих proofs данным profiling
не измерена и не используется как числовое обоснование отказа от cache.

## Что делает current ordinary pipeline

[SolutionManager](../../../../Services/Workspace/SolutionManager.cs),
`LoadAndPrepareAsync` / `LoadCoreAsync`: canonical path и нормализация аргументов;
один manager lock; current globals explicit → Loaded* → FileSettings. RAM matcher
сравнивает только passed fields, omitted сохраняет прежнюю семантику. Same-key
RAM reuse flushes dirty documents; это отдельный режим, не новый-PID benchmark.
При physical reopen manager закрывает watcher/session и прежний workspace,
сбрасывает runtime state, создаёт `MSBuildWorkspace` и ordinary load session.

[AnalyzerProvenanceCaptureService](../../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs)
содержит ordinary `OpenProjectAsync`/`OpenSolutionAsync`, actual design-time logging
(`ProjectImports=None`, caller Normal), перечисление binlogs, replay на Task.Run,
binding/snapshot и cleanup. Это existing analyzer provenance capture, отдельный
от E0 state-snapshot capture; его нельзя объявлять лишним из-за overlay-off.
Current published semantic solution остаётся governed execution/admission policy.
Обычный server logging в [Program](../../../../Program.cs) очищает providers;
E0 load response содержит health, но не stage timings. Runtime capture metric
ReplayDuration сама по себе не даёт открытый MCP stage report.

После open manager оценивает execution gate, публикует no-overlay snapshot,
сохраняет globals и diagnostics, создаёт input map и стартует watchers через
[builder](../../../../Services/Workspace/WorkspaceInputMapBuilder.cs) и
[starter](../../../../Services/Workspace/WorkspaceDiskWatcherStarter.cs).
Overlay-on отдельно проходит capture suitability, preparation и publication,
сохраняя sticky RAM behavior и реальные Banned/Unavailable. Для semantic call
`GetSanitizedPublishedSolutionAsync` flushes disk changes и возвращает admitted
solution. [Navigation tool](../../../../Tools/NavigationTools.cs) и
[SymbolDeclarationResolver](../../../../Services/Navigation/SymbolDeclarationResolver.cs)
вызывают `SymbolFinder.FindDeclarationsAsync` по projects; Roslyn first-use
compilation/generator work не вычитается из query. Sequential project traversal
виден в коде, но его наличие не является измерением hotspot.

## Пригодные E0 numbers: два ключа, две ноги

Первоначальный [summary](../../epoch-0-snapshot-roundtrip/evidence/task-07/summary.json)
и [key](../../epoch-0-snapshot-roundtrip/evidence/task-07/compare-key.json):
`e6c127a0c8dad57d03998ab9caa2ba67e3037046c7b9c1ca8df030053f2aad58`.
Ordinary n=5 successful measured, 0 errors, 0 timeouts, один исключённый warmup;
load median 1427.3 ms, semantic 889.6 ms, useful **2318.2 ms**.
Isolated restore n=5/5, 0 errors/timeouts: useful **1413.2 ms**,
capture overhead **3035.0 ms** отдельно. Failed early bootstrap smoke сохранён
описанием в historical report; его удалённый каталог не восстанавливался и не
выдаётся за доступный raw. Accepted evidence не переписано.

Исправленный runner, independent positive recheck:
[summary](../../epoch-0-snapshot-roundtrip/evidence/task-07-review-round-2/positive/summary.json),
[key](../../epoch-0-snapshot-roundtrip/evidence/task-07-review-round-2/positive/compare-key.json),
[ordinary raw](../../epoch-0-snapshot-roundtrip/evidence/task-07-review-round-2/positive/ordinary/),
[restore raw](../../epoch-0-snapshot-roundtrip/evidence/task-07-review-round-2/positive/restore/),
[run](../../epoch-0-snapshot-roundtrip/evidence/task-07-review-round-2/positive/run.txt).
Key `e91ceca993df22cf067315714b2a41a94e455914d80a475fe9a6125196cf09df`;
ordinary n=5/5, 0 errors/timeouts: load **1427.0 ms**, semantic **877.6 ms**,
useful **2294.3 ms**, useful min/max 2290.7/2310.6 ms.
Restore n=5/5, 0 errors/timeouts: read 1.4, decode 77.3, hydrate 1091.7,
query 189.7, useful **1364.7 ms**. Один warmup каждой ноги исключён.
Два прогона не объединены: dirty inventory keys различаются.
BTCPay отдельно unsupported `generator-dependent-scope-unsupported`, не positive
ordinary optimization control и не reason убрать references/analyzers.

Для обоих E0 keys: Release win-x64 server file/source 1.5.6(.0), binary product
commit 09cb8ea2f8a82119894384774578cd5b11d3f0f2; SDK 10.0.300, MSBuild
18.6.3.23102/package 18.6.3, Roslyn 5.9.0; Windows 11 10.0.26200, Ryzen
9800X3D/16 logical CPU, RAM 100562268160 bytes. Source identity 232 files,
`19f8d2de826006e5724da98503026ebb85599da395739c4646973738d2a5ff27`.
Control `samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj` открывает два projects.
Raw configuration/platform/TFM/buildArgs/shadowCopy absent, independently merged
fresh loader globals absent, FileSettings absent, evaluated Debug/AnyCPU/net10.0.
Explicit load, overlay off; binary policy/chunks not-applicable. Expected query
`find_symbol_definition:SamplePlugin` → `RoslynMcpPlugin.SamplePlugin`,
`samples/RoslynMcpPlugin/SamplePlugin.cs`, line 5 column 21. H-001 external XML
excluded / H-002 signing-independent B и excluded emit/signing относятся к finite
isolated comparison profile; ordinary semantics этим исключением не урезаны.

Time-to-first-useful — **load + первый успешный semantic response**. Startup
(initialize), publish и corpus restore отдельно, не входят в useful. Файловый
cache ОС не сбрасывался: new-process/warm-files, не cold I/O и не existing RAM
reuse. Isolated restore при уже готовом envelope не включает production probe,
fresh prepare, watcher/publication, capture-to-load-response и не является disk
hit. Разность 2294.3−1364.7 не является эффектом ordinary оптимизации.
Capture до load response в возможном E1 обязателен внутри его useful. Input
hashes для сравнимости сняты вне таймера; это не manifest/P-004 proof. p95 нет.

## Focused profiling

До запуска выбран ровно прежний неизменённый SamplePlugin request и ожидаемый
semantic result, без filePath/line narrowing, source/reference/analyzer mutations.
Diagnostic инструмент: установленный `dotnet-trace 10.0.745401`, профиль
`dotnet-sampled-thread-time`, attach после initialize, до load, parent server
PID only. BuildHost child PID не трассируется. Current source HEAD до запуска
`fc72dc80d330874ae5160950e972a8c8b4d67874`; фактический published binary fingerprint
берётся из FileVersion, не подменяется source HEAD. Publish и corpus restore
не выполнялись во время измерения; server уже опубликован Release win-x64.

Сохранены все неуспехи:

- [setup-failures](task-00.4/setup-failures.json): `py -3` не обнаружил Python;
  initial driver raw-level dictionary дал KeyError до server start. Это два
  not-run setup failures, не query misses и не performance samples.
- [failed-attach attempt](task-00.4/failed-attach/attempt-01.json),
  [trace output](task-00.4/failed-attach/trace-output.txt): PID 41684, initialize
  197.1 ms; expected readiness marker отсутствовал в установленной версии tool,
  load не отправлен; error, useful absent. Trace idle, hotspot не подтверждает.
- [sandbox load attempt](task-00.4/failed-sandbox-load/attempt-01.json),
  [load response](task-00.4/failed-sandbox-load/attempt-01-load.txt): PID 82360,
  startup 191.8 ms, raw load 532.5 ms, load failed, semantic skipped, useful absent.
  BuildHost был started, но named-pipe connection не установилась. Повтор вне
  sandbox после automatic approval дал actual unchanged positive, поэтому этот
  отказ — environment limitation и не regression/ordinary hotspot verdict.
- [initial positive](task-00.4/success-truncated-trace/attempt-01.json): PID 30636,
  startup 177.6 ms, raw load 1718.5, semantic 952.6, raw useful 2671.1 ms. Existing
  runner teardown kill_tree произошёл до rundown, [trace output](task-00.4/success-truncated-trace/trace-output.txt)
  помечает potentially broken trace; topN parser отказал `Read past end of stream`.
  Semantic success пригоден, trace атрибуция — нет. Raw load/useful здесь включают
  немеренный отдельно profiler attach wait; они не ordinary median/speedup.

Final driver ждёт duration/rundown до kill_tree. [Command](task-00.4/profile-command.txt)
импортирует existing runner, добавляет diagnostic callbacks только в его memory,
сохраняет request/response phase timestamps и stderr. Raw load/useful включает
profiler attach wait, потому что existing runner начинает timer до send_message;
`profilerAttachWaitMsIncludedInRawLoad` и `requestOnlyLoadMs`/`requestOnlyUsefulMs`
раскрывают этот overhead отдельно. Sampling overhead внутри request неизвестен,
не вычитается. После semantic response ожидание rundown не включено в useful,
но trace включает этот idle interval и его нельзя считать hotspot.

Bounded recheck завершён; final raw result и attribution приведены ниже.

Final positive: [attempt](task-00.4/attempt-01.json),
[semantic response](task-00.4/attempt-01-semantic.txt),
[load response](task-00.4/attempt-01-load.txt), [stderr](task-00.4/server-stderr.txt),
[trace output](task-00.4/trace-output.txt),
[top inclusive](task-00.4/top-inclusive.txt), [top exclusive](task-00.4/top-exclusive.txt),
[analysis](task-00.4/profile-analysis.json), [analysis command](task-00.4/analysis-command.txt), [content identity](task-00.4/content-identity.json).
Сырые `ordinary.nettrace`, `ordinary.nettrace.etlx` и `ordinary.speedscope.json` хранятся локально.
PID **79380**, profiler exit **0**, load/semantic success, expected type/file/line
confirmed; two projects. Startup **177.5 ms**, raw load **1615.4 ms**, semantic
**981.4 ms**, raw useful **2596.8 ms**; attach wait **79.8 ms** included in raw
load. Derived request-only load **1535.6 ms**, request-only useful **2517.0 ms**.
These are instrumented n=1 diagnostic numbers, with unknown sampling overhead;
no uninstrumented paired estimate or performance improvement is inferred.
No input differences before/after в measured 232-file content-identity scope;
это не proof неизменности всех memberships/generated/excluded paths. В final и
initial-positive semantic raw присутствует Note о saved `.cs`, который appeared
или disappeared outside loaded workspace snapshot, и о graph-stale reload route.
Expected SamplePlugin declaration найден, но watcher/graph-stale warning здесь
не расследован: его источник и affected path **not-measured**. Stable memberships,
unchanged full graph и parity с E0 не заявлены. Diagnostic n=1 не сравнивается
с E0 numbers как performance evidence; unchanged scoped hashes этот предел
не снимают.

Final compare key `0be0c4f3e2b50f8c0b3fd09827cf36300ed612c3d2f7187803b7b208f9e35f9a`
is in attempt JSON: current source HEAD fc72dc80…, actual published server commit
09cb8ea2…/version 1.5.6.0. This records the distinction, not a claim that the
publish equals all current source. Same control/query, 232-file content identity,
SDK/MSBuild/Roslyn, raw absent/merged absent/evaluated Debug/AnyCPU/net10.0,
overlay-off, OS/CPU/RAM and not-applicable binary policy as above; new dirty
inventory and instrumentation make it a separate key from E0. No warmup/median
admission: useful numbers are diagnostic only. Across this investigation:
2 setup not-runs; 1 failed attach before load; 3 actual load requests, 2 semantic
successes and 1 environment load failure; query misses 0, request timeouts 0.
Only **1** positive trace is usable for method-name inspection; the earlier
success with broken rundown remains separately recorded. Failed readiness wait
is an orchestration error, not a request timeout.

Final trace parser succeeds and contains 3058 frames across 22 thread profiles,
including ordinary load/capture, replay, input-map, symbol resolution, compilation
and generator frames. No broken-trace warning remains; `Missing Symbol` присутствует, full symbol resolution не заявлена. Whole-trace exclusive
thread-time is dominated by `GetQueuedCompletionStatus` 61.87%, IO poller 10.05%
and waits. This is concurrent sampled **thread-time**, with post-response idle;
it is neither wall-clock load nor CPU-only critical path. BuildHost work is
outside this process. Frame presence proves these paths were observed, not their
attributable stage durations. Removing waits or provenance on this evidence
would be unjustified. Open/DTB, stage wall durations, lock wait, input enumeration,
stat/hash/read, map construction, prepare/publication, provenance replay critical
path, first compilation/generator breakdown, visited entries and bytes remain
**not-measured** as separate costs. RAM reuse, overlay-on and lazy entry are
**not-run** in this diagnostic; capture/codec timings remain solely E0 evidence.

## Итог и один bounded следующий шаг

**Honest no-go: подтверждённый локальный hotspot для небольшой production
оптимизации не выбран.** Это отрицательный результат task-00.4 research,
не отказ от ordinary load и не приёмка E1. Outer load содержит около 1.4 s на E0
и query около 0.88 s; эти длительности нельзя превратить в обещание уменьшить
какой-либо конкретный helper. Попытка профилирования выполнена; после bounded
rundown fix method names пригодны, но critical-path attribution недостаточна.

Единственный предлагаемый следующий шаг, если владелец выбирает продолжение
ordinary исследования: **измерить ordinary provenance replay как отдельный
candidate cost на том же SamplePlugin request**, не менять алгоритм. Это один
bounded measurement, а не production optimization implementation proposal.
Seam — `OpenAndCaptureAsync` после MSBuild open, вокруг `ReplayAll` и
`BuildSnapshot`, с already available `Metrics.ReplayDuration` и отдельным wall
span полного `OpenAndCaptureAsync`; public load/query остаются прежними. Такой
measurement выполняется diagnostic/test-only способом без отключения logger,
без skip replay, без fork/change BuildHost, без переноса capture за load response.
Если существующего exposed seam недостаточно, отдельная measurement задача
должна заранее определить минимальное instrumentation и пройти применимую
validation. Никакого нового API/default/activation этот report не вводит.

Acceptance этого следующего measurement: один заранее выбранный неизменённый
real request; один исключённый warmup и минимум 5 successful comparable new-PID
attempts, все errors/timeouts/retries в raw; request-only load/query/useful и
startup отдельно; фактические replay/open/snapshot spans, counts/bytes и границы
перекрытия задокументированы. Сумма parallel/inclusive spans не выдаётся за wall
cost. Before/after оптимизации пока нет; effect **not-measured** и числовой target
ускорения **не назначен**. Решение разрешить маленькую optimization task возможно
лишь если конкретная avoidable work доказана на critical path и есть correctness
preserving способ её уменьшить. Если replay мал/не подтверждён — сохранить no-go,
не создавать серию других measurements автоматически.

Будущая optimization acceptance (если её отдельно выберут после такого evidence):
новый paired compare key с изменением server build как явной variant, прежние
корпус/modes/query/expected result и environment; минимум 5 successful attempts
на variant. Useful median должна уменьшиться при сохранённом semantic result;
startup и load/query scatter показаны, перенос работы в query/capture/startup
не скрыт. Failed/insufficient benchmark — performance not-accepted. Mode-scoped
улучшение не обобщается на прочие modes и не объявляется disk hit.

Correctness regressions обязательны для любого выбранного ordinary change:

- RAM omitted matcher, explicit/absent distinction, Any CPU normalization,
  effective explicit → Loaded* → FileSettings, changed workspace/inherited
  globals, buildArgs update, reset и graph-stale physical reopen.
- Sticky overlay-on/off/repeated same-key behavior; unchanged analyzer/generator
  references, actual provenance/session binding, capture incomplete/corrupt,
  real Banned/Unavailable и restart-required; no fabricated execution permission.
- Workspace diagnostics/health/coverage и shared/linked/all-owner input map;
  watcher startup failures/races; source/additional/config/disk-sync semantics;
  не повышать Unknown до Complete по пустому списку diagnostics.
- Cancellation до/после lock/open/replay/prepare, failed candidate, lazy text и
  metadata lifetime, disposable workspace/binlog/temp resource ownership и
  postfailure usability в рамках current contract; не менять cleanup ради цифры.
- [Write preflight](../../../../Services/Workspace/WorkspaceWriteBoundary.cs),
  exact analyzer inverse, session/base/publication freshness, single production
  TryApplyChanges site, shared-path persistence, отказ до недопустимых side effects.

Validation выбранного будущего production change: focused affected regressions,
весь SourceStructure, Release solution build + полный Category!=AnalyzerLifecycle
main; AnalyzerLifecycle с ROSLYN_MCP_ANALYZER_LIFECYCLE=1, если изменены
analyzer/shadow-copy/load/prepare/publication или lifecycle harness/config.
Exact structural limit inventory сохраняется. Это scope требуемых проверок,
не заявление, что настоящая оптимизация или эти cases уже реализованы.

## Что может сохраняться без manifest и что теряется при отсрочке

Все E0 DTO/codec/ordinary capture/isolated hydrate/real cross-process roundtrip,
semantic/edit controls и raw benchmark artifacts сохраняются вместе с их
accepted bounded scope/H-001/H-002. Их не удалять и не превращать в production
cache proof. Existing ordinary loading, manager-owned RAM reuse, current watchers,
disk sync, non-CSharp layer, analyzer admission и writes доступны без E1 manifest.
Ordinary measurement/доказанная будущая локальная оптимизация не требует reusable
disk envelope, если не обходит эти инварианты.

[E1/task-04 host seam](../task-04-manager-host-ownership-hi.md) формально зависит
от accepted E0/task-07, а не manifest, и может обсуждаться независимо как
ownership refactor. Он **не показан ускорением** данным profiling и не выбран
следующей implementation задачей; его production activation/validation не
возникает автоматически из этой альтернативы.

Если владелец отдельно отсрочит disk restore: E1 reusable manifest/probe/store/
lookup/capture/hydrated prepare/watch/publication/write/restart acceptance и
production unchanged disk-hit benchmark недоступны. Task-00 остаётся blocked
из-за all-instance evidence/P-004 до нового applicable решения и реальной
приёмки, а ordinary success не выполняет manifest. E2 restart content refresh
без DTB (existing source/additional/config/eligible metadata), refreshed envelope
и edit/build→restart benchmark зависят от accepted E1/task-10 и недоступны.
E3 narrow selective reopen/dirty dependency mapping/whole candidate publication
и selective benchmark зависят от accepted E2/task-06; недоступны как результат
этой серии. Existing live watcher/source sync не засчитывается E2 restart,
ordinary whole reload не засчитывается E3 selective refresh.

После owner decision потребуется записать дату, accepted decision scope/reason
и evidence в E1/E2/E3 README/execution summaries и series index; downstream tasks
получат accurate deferred/blocked-by-owner-decision status по фактическому
решению. Для E1 task-01/02/03/05–10 и E2/E3 dependency chain потребуется
явная ссылка на отсрочку; E1/task-04 требует собственного решения, не blanket
completed/deferred по умолчанию. E0 accepted task statuses и S-002 partial result
не переписываются. Task-00.4 можно принять только как research/no-go, без выбора
ordinary route. Ни один из этих conditional status edits данным implementer
не выполнен; coordinator обновляет лишь status завершённого исследования после
independent review/validation.

## Validation этого scope

Production C#, test C#, existing harness scripts и configuration не менялись.
Measurement driver отличается от C# harness. Новых оптимизаций, versions, cache
entry/defaults нет. Самостоятельный build/test pass implementer не заявляет:
final applicable validation и status bookkeeping принадлежат coordinator.
Local links/dependencies/diff/dirty preservation проверяются по final artifacts;
итоговые независимое review и acceptance evidence будут записаны coordinator.
