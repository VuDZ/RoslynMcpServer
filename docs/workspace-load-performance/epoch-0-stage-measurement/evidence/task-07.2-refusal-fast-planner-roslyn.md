# Task-07.2 — Отказ до загрузки, быстрый поиск и Roslyn

Дата: **2026-10-10**. Scope: standalone `spikes/ParallelLoad` и evidence.
Frozen runtime прошёл независимое review round 2 ACCEPT и coordinator checks;
**свежая серия завершена: 36 matrix runs и шесть дополнительных load processes**.
Вердикт эксперимента — **no-go**: Roslyn parallel attempts имеют `plan-violation`.
Итоговое независимое data review round 2 — ACCEPT для отрицательного standalone эксперимента; формальная code/tests acceptance pending зелёного
main CI (1362 passed / 1 прежний SDK-specific failed / 0 skipped). Task-07/task-07.1,
production, version, CHANGELOG, solution и CI не изменяются. Координатор отвечает
за freeze, последовательную матрицу, финальную validation и task/index statuses.

## Предрегистрация до свежей серии

Политика ниже записана до первого нового baseline и не меняется по его результатам.
Она включается только `--refusal-policy true`. Без этого флага старые режимы
сохраняют прежний выбор partition и Debug|x64.

Приоритет причин: `balance-infeasible`, `planning-budget`, `no-expected-gain`,
`memory-budget`. JSON сохраняет главную причину, входы модели, прогноз и все
обойдённые force-флагом причины. `--force-parallel` обходит **только**
`no-expected-gain`/`memory-budget`; поиск допустимого плана обязателен и при force.

- `balance-infeasible`: законченный полный перебор назначений корней с учётом
  симметрии workers доказал отсутствие admissible plan при ε=0,15. Перебор
  применяется при ≤10 корнях. Неоконченный поиск доказательством не является.
- `planning-budget`: bounded search не нашёл допустимого плана до deadline.
- `no-expected-gain`: прогноз wall ≥0,9×оценки ordinary wall.
- `memory-budget`: оценка пика BuildHost >4 000 000 000 bytes, десятичные GB.

Доступность модели проверяется отдельно от баланса. При max=mean=0 balance ratio
равен 1, как у прежнего BuildPlan; all-core план может быть допустимым. Если у
admissible плана нет положительной profile cost вне ядра, `PredictedParallelMs=null`
и `ModelUnavailableReason` сохраняются отдельно. Это **не пятая допустимая причина
отказа** и не `planning-budget`: после ordinary fallback итог остаётся
`Status=model-unavailable`, `GateEligible=false`, exit 2; force не исправляет
недоступную модель. Четыре заданных условия успешного отказа не расширяются.

Search budget — **500 мс**, включая построение индексов/замыканий и seed assignment,
но исключая чтение XML, snapshot inventory и profile. Все эти чтения входят в
`PlanningMs` и итоговый wall. Дополнительные детерминированные bounds: 20 rounds,
40 000 evaluated candidates. Порядок roots ordinal, moves по root/worker, swaps
по affinity с ordinal tie-break, bundles по стоимости/path; лучший admissible
план сохраняется отдельно от текущего локального состояния. При deadline он
остаётся допустимым результатом; отсутствие лучшего плана — явный отказ.
Реальный `SearchMs`, candidate count и deadline flag остаются evidence, даже если
машина/GC/JIT не позволят соблюсти wall bound. Измеряемая серия не использует
предвычисленный offline план: каждый fresh процесс заново выполняет поиск.

После review round 1 проверка deadline выполняется внутри preparation/closure/
affinity, seed, evaluation, swap/bundle ranking и materialization. Поиск использует
cached fully materialized best plan и не вызывает повторный BuildPlan после
deadline. Candidate, завершённый после deadline, отбрасывается. Из общего бюджета
500 мс 5 мс резервируются для выхода/записи metadata; substantive work прекращается
при 495 мс. `InterruptedStage` указывает место остановки. Общий wall и реальные
планировочные затраты остаются измеряемыми; external preemption/GC не скрываются.

Frozen модель берётся из `matrix.model` в
[task-07.1 summary](task-07.1-core-first-equivalence-and-partition/summary.json).
Её параметры в миллисекундах:

- h1 = **592,9752**;
- residual = **4947,748723477786**;
- c = **1,1756123129598963**;
- b = **1,1359945429941485**;
- A = **5,5108**, M = **120,3719**.

Параметры h1/residual/c/b/A/M не перекалибровываются. b не заменяется фактическим
balance ratio нового плана. Как и в прежней модели, profile нормируется к ordinary
service time. Для workers N, core share f и same-series baseline T:

`W = max(0, T − h1)`;
`prediction = actualPlanningMs + A + h1 + f×W + c×(residual + b×(1−f)×W/N) + M`.

Это conditional extrapolation глубины 1; residual включает cold loader, aggregator,
ordering/context differences и не является причинной ценой spawn. `actualPlanningMs`
для прогноза измеряется перед решением; полная финальная `PlanningMs` дополнительно
включает чтение baseline estimate/решение. Разница сохраняется явно.

Ordinary estimate: **медиана wall measured baseline 1/2/3 этой новой серии**.
Memory input: **максимальный sampled summed BuildHost working-set peak** этих же
трёх runs. Скрипт `estimate` требует успешные stable snapshots, одинаковые globals,
registration и здоровую telemetry; пишет hashes каждого исходного JSON. XML graph
использует inventory baseline 1 той же серии. Cost profile — обычный binlog этой
серии через неизменённый reader task-05.1; старые profiles используются только
для development offline checks ниже.

Предзаданная memory formula:
`predictedPeak = baselinePeak × (1 + N/4)`.
Это heuristic: один retained core плюс N условных четвертей ordinary footprint.
Она **не является upper bound** и не учитывает неизвестные изменения размера
групп/TFM/GC. Поэтому actual memory gate каждого non-force parallel run сохраняется.
Формула записана до свежей серии, а её ошибки не исправляются новым fit.

При pre-load отказе BuildHost параллельных групп не создаётся; выполняется обычная
`LoadSolutionInfoAsync`, `WallMs = PlanningMs + OrdinaryFallbackMs`, обычный snapshot
сравнивается с baseline тем же byte-unchanged comparator.
При post-load failure/timeout/plan-violation выполняется ordinary fallback с новым
самостоятельным timeout budget; итоговый Status остаётся статусом неудачной попытки,
`GateEligible=false`, `WastedParallelMs` и полный `AttemptedParallel` сохраняются.
Последний содержит partial observation, group statuses/errors, violations,
diagnostics, progress и telemetry. Ordinary result сохраняется отдельно; его успех
не превращает попытку в успешную строку gate. Wall включает планирование, неудачную
параллель и ordinary. Внешний hard process timeout сохраняется координатором как
неуспешная попытка и не объявляется successful fallback.

## Roslyn: входы и SDK

Корпус — полный `Roslyn.slnx`, ожидаемый HEAD
`013d3a758df6c137497ff37a93f0d4bed103853a`. XML содержит C# и VB; spike добавляет
VisualBasic.Workspaces 5.9.0 и `.vbproj` в граф ownership. Исходные `.shproj` и
настоящие unloadable entries сохраняются в requested inventory фазы 1.
Literal XML edges не превращаются в evaluated graph; imported/property-generated
edges могут вызвать violation, который остаётся failed row с fallback.

Точный local SDK: `<roslyn-clone>/.dotnet/sdk/10.0.301`; `--local-sdk` проверяет
global.json pin, SDK folder и dotnet host, регистрирует этот MSBuildPath. В child
environment устанавливаются local DOTNET_ROOT/X64, DOTNET_HOST_PATH, resolver dirs,
MSBuildSDKsPath; local dotnet root добавляется в начало PATH, поскольку публичный
Roslyn loader запускает `dotnet.exe` по имени. Никакой глобальной установки и
правки global.json. Snapshot записывает parent Registration/LocalSdk/DotnetHost;
binlog replay извлекает фактические `NETCoreSdkVersion`, `MSBuildToolsPath`,
`MSBuildSDKsPath`, `MSBuildVersion`. Если child evidence укажет другой SDK либо его
невозможно установить, серия останавливается с причиной.

`--global-properties server-default` означает заданный task-07.2 вход
`Configuration=Debug`, без Platform и TargetFramework. Источник: optional значения
load_workspace проходят через `Tools/WorkspaceTools.cs`, `SolutionManager` и
`MsBuildWorkspaceProperties.Create`; helper добавляет только непустые значения.
При fresh FileSettings.Empty конфигурация может оставаться null, а SDK/project
default — Debug. Поэтому отчёт не утверждает, что сервер безусловно передаёт Debug.
PowerToys/Files сохраняют `debug-x64`. CLI `--timeout-minutes 20` задаёт Roslyn
предварительную/обычную загрузку; внешний runner должен иметь больший timeout.

Restore/environment records, exit codes, interim retries и итоговый результат
сохраняет координатор в ignored task-07.2 artifacts. Антивирусы и cache policy
не меняются.

## Development validation без новых загрузок

MCP Release build spike — PASS; прежние `check-core-first`, `check-profile`,
`check-merge` — PASS; новый `check-refusal` — PASS. Development fixes включали
дублирующее имя local pin, корректный synthetic deadline fixture и числовое
чтение JsonNode при compose; после исправлений проверки повторены.
В одном Release build были прежние MSB3061 file-lock warnings от работающего MCP;
следующие build succeeded. Финальные solution build/main CI выполнены координатором
и приведены в разделе validation ниже.
Production lifecycle не менялся, AnalyzerLifecycle не требуется.

Self-checks проверяют четыре причины отказа, force scope, zero-cost model
unavailability, completed proof, incomplete budget, сохранение admissible seed на
deadline, повторяемость ownership/candidate counts, post-load failure/timeout/
violation после ordinary success и полный wasted wall. Synthetic interval check
разделяет nested target → CallTarget task → child target → task: exclusive
20+20+20+40=100 мс; параллельный unrelated node не вычитается.
`python spikes/ParallelLoad/check_refusal_analysis.py` — PASS: gate, failure
fallback, individual memory outlier, refusal equivalence, missing rows, median
baseline estimate и diagnostic shares. Python printed its existing launcher
location warning, но regression завершилась exit 0.

Offline planner на immutable прошлых inputs, **не загрузочный замер**:

- PowerToys XML cluster N=4: оба отдельных CLI процесса дали 12 core projects,
  core cost 3998,7807/38532,1145 = 10,37778%, balance 1,1092723286391768,
  12 589 candidates; SearchMs **250,7547 / 245,5341** мс. Все plan поля совпали
  после исключения только SearchMs. Это прежнее ядро task-07.1 (12/10,38%) при
  поиске существенно меньше прежних 6,608 с. Новая серия проверит это снова.
- Files XML cluster N=2: **9** evaluated candidates: seed плюс **8**
  symmetry-distinct leaf assignments (seed может повторяться среди leaves),
  `InfeasibilityProven=true`, `balance-infeasible`, SearchMs **17,9326** мс,
  exit 2 ожидаем. Параллельная загрузка не выполнялась.
- Replay прежнего ordinary binlog task-05.1: `ok`, 267 project timing entries,
  SDK properties 10.0.401 из evaluation events; это проверка reader, а не Roslyn
  SDK301 evidence новой серии. EvaluationProfilerAvailable=false.

Raw development outputs: `artifacts/parallel-load/task-07.2-evidence/offline-*.json`.
Их компактный hash inventory будет сохранён с freeze/validation.

Review fix round 1, 2026-10-10, до fresh baseline: все четыре must-fix исправлены
тем же implementer. Повторный MCP Release build, check-refusal, check-core-first
и Python regression — PASS. Новые injected-clock checks останавливаются в closure,
affinity, seed, evaluation, materialization completion и swap/bundle ranking;
late seed не принимается, ранее завершённый seed на ranking deadline сохраняется.
Regression all-core/shared directory подтверждает balance ratio=1 и отсутствие
ложного infeasibility proof. Model-unavailable fallback остаётся failed gate.

Первый промежуточный offline запуск исправления достиг deadline в evaluation:
500,4997 мс, 12 084 candidates, cached admissible core 12/10,38%; это development
evidence до резерва времени/снижения частоты проверок внутри ограниченных loops.
Финальный код дважды закончил полный поиск: **388,8396 / 395,6800 мс**, 12 589
candidates, core 12/10,38%, balance 1,1092723286391768, без deadline; планы byte-wise
равны после исключения SearchMs. Files: **10,4439 мс**, seed+8 leaves,
`balance-infeasible`. Outputs `offline-*-fix1-plan*.json` сохраняются отдельно от
исходных development runs и новых измерительных rows.

## Диагностика прогрева: метод и доступность

Каждый новый run сохраняет публичные ProjectLoadProgress FilePath/Operation/
ElapsedTime; это длительность operation, а не timestamp spawn. Отдельные binlog
runs ordinary и parallel N=4 на PowerToys/Roslyn помечаются DiagnosticRun и
исключаются из медиа́н. Параллельный run пишет phase1 и отдельный binlog каждой
группы. Каталоги уникальны для каждого запуска; force допустим только для двух
экономических причин.

`binlog-timings` сопоставляет Start/Finish intervals; recoverable errors и
непарные события дают `incomplete`, не выдуманный нулевой результат. Evaluation
считается union; targets/tasks — exclusive с вычитанием дочерних intervals по
context lineage, включая nested CallTarget. Inclusive родители не суммируются с
tasks. Конкурирующие nodes исключаются из lineage. Финальные evaluation properties
служат SDK evidence. Неполный profiler не заменяется причинной гипотезой.

Сравнение `binlogs` использует **одни и те же project paths**, первые три по первому
появлению managed matched project и остальные отдельно; synthetic aggregator и
unmatched projects показываются отдельно. Top increments: evaluation, exclusive
targets, exclusive tasks. Доли имеют denominator **сумма положительных attributed
increments этих трёх слоёв на matched projects выбранной части**, а не весь wall
или прежний residual. Отрицательные дельты и net delta сохраняются. Неатрибутированный
wall/context residual остаётся отдельным неизвестным, без присвоения его IPC,
антивирусу или SDK resolver.

SDK resolver diagnostic messages не содержат гарантированной paired duration.

Parallel diagnostic gate требует successful actual N4 `AttemptedParallel`,
отсутствие fallback, completed phase1 и named phase1 binlog, отдельный binlog для
каждой nonempty группы и полный group outcome inventory. Ordinary fallback не
удовлетворяет этой обязательной диагностике. Экономический отказ требует нового
forced diagnostic run; balance/planning refusal этим флагом не обходятся. JSON
сохраняет `BinlogFiles`; synthetic analysis checks отвергают отсутствующие phase1/
group files и fallback, а forced economic case проверяют отдельно.

`SdkResolverTiming=null` и явное limitation остаются в JSON; evaluation включает
его неизвестную цену. Profiler availability показывается отдельно. Exact SDK
resolver durations и полная причинная декомпозиция residual могут оказаться
недоступны публичному BinaryLogger; отчёт обязан сохранить это ограничение.
Кандидаты против прогрева обсуждаются только после новых чисел; средства не
реализуются в этой задаче.

В PowerToys обычный loader пишет два streams; каждая группа также
может иметь несколько streams. Python analysis объединяет **все** ordinary и
group streams, сохраняет исходные filename/format/errors/profiler/SDK fields,
JSON hashes и каждое появление повторного project path. Внутри одного stream
используются union/exclusive intervals; между разными hosts складывается service
work, что не объявляется wall union. Aggregation по path не различает отдельные
TFM contexts, поэтому результат показывает именно summed path service.

Frozen C# timings не содержат абсолютного first-project timestamp для merge
порядка двух streams. `--group-load-report` даёт явный proxy: первый public progress
callback каждого matched path внутри группы. Это порядок завершения callbacks,
не exact cross-stream start order. Без этого источника firstThree unavailable,
показывается только allMatched; chronological first-three не выдумывается.
Unmatched paths/aggregator, ordering gaps и profiler gaps сохраняются отдельно.

## Команды координатору

Все CLI arguments ниже передаются MCP `run_dotnet_run` для
`spikes/ParallelLoad/ParallelLoad.csproj`; каждый вызов — свежий процесс.
MCP build spike: Release; self-checks: `check-refusal`, `check-core-first`,
`check-profile`, `check-merge`. Измерительные процессы выполняются последовательно.
`B` ниже — абсолютный `artifacts/parallel-load/task-07.2-evidence`, `S` — solution.

```text
baseline --solution S --output B/<name>-baseline-0.json [--global-properties server-default --local-sdk <roslyn>/.dotnet/sdk/10.0.301 --timeout-minutes 20]
# Repeat baseline 1/2/3 after the warmup; Roslyn prerequisite must finish within 20 minutes first.
python spikes/ParallelLoad/summarize_refusal.py estimate --inputs B/<name>-baseline-1.json B/<name>-baseline-2.json B/<name>-baseline-3.json --output B/<name>-baseline-estimate.json
profile --solution S --output B/<name>-profile.json [Roslyn settings above]
# profile is the separate ordinary binlog diagnostic. Its .load.json is copied under diagnostic-<name>-baseline.json.
core-plan --solution S --output B/<name>-forecast-N4.json --workers 4 --graph xml --snapshot B/<name>-baseline-1.json --profile B/<name>-profile.json --partition cluster --refusal-policy true --baseline-estimate B/<name>-baseline-estimate.json [Roslyn settings]
core-first --solution S --output B/<name>-cluster-N4-0.json --workers 4 --graph xml --snapshot B/<name>-baseline-1.json --profile B/<name>-profile.json --partition cluster --refusal-policy true --baseline-estimate B/<name>-baseline-estimate.json [Roslyn settings]
# Repeat measured 1/2/3; Files uses cluster-N2/workers2; Roslyn also cluster-N8/workers8 and greedy-N4/partitiongreedy.
# Each refused Roslyn combination also repeats warmup+1/2/3 with --force-parallel and filename ...-force-<index>.json.
core-first --solution S --output B/diagnostic-<name>-cluster-N4.json --workers 4 --graph xml --snapshot B/<name>-baseline-1.json --profile B/<name>-profile.json --partition cluster --refusal-policy true --baseline-estimate B/<name>-baseline-estimate.json --binlog-dir B/<name>-parallel-binlogs [Roslyn settings; --force-parallel if economically refused]
binlog-timings --solution-root <clone> --input <single.binlog> --output B/<name>-timings-<phase-or-group>.json
python spikes/ParallelLoad/summarize_refusal.py binlogs --baseline B/<name>-ordinary1-timings.json B/<name>-ordinary2-timings.json --group-streams B/<name>-group0-1-timings.json B/<name>-group0-2-timings.json --group-streams B/<name>-group1-1-timings.json B/<name>-group1-2-timings.json --group-streams B/<name>-group2-1-timings.json B/<name>-group2-2-timings.json --group-streams B/<name>-group3-1-timings.json B/<name>-group3-2-timings.json --group-load-report B/diagnostic-<name>-cluster-N4.json --output B/<name>-warmup-comparison.json
python spikes/ParallelLoad/summarize_refusal.py matrix --artifacts B --output <docs-evidence>/summary.json --details B/comparisons.json
python spikes/ParallelLoad/check_refusal_analysis.py
```

Для offline поиска без loader/экономического решения: `core-plan` с теми же
graph/snapshot/profile arguments, `--fast-planner true` и без `--refusal-policy`/
baseline estimate. Печать exit2 для Files означает сохранённый отказ, не crash.

До первого Roslyn measured parallel координатор выполняет **оба** core-plan N=4/N=8,
сохраняет JSON/hash и UTC timestamp записи прогноза в этом отчёте. Эта запись
выполнена; исходный блок ниже сохраняется без изменения чисел/timestamp/hashes.
Матрица Roslyn не запускается, если prerequisite baseline failed/timeout/>20 min.
Все planned нулевые/failed/timed-out попытки сохраняются с причиной.

## Прогноз Roslyn до первого параллельного замера

Записан координатором **2026-10-10T18:16:49Z**, до первого measured parallel run Roslyn.
Исходные JSON прогнозов сохранены без перезаписи; runtime — frozen 50 C#/csproj
files. Модель/пороги и формула памяти не перекалиброваны.

| Workers | Core count / cost share | Predicted wall, с | Решение | Estimated peak, GB |
|---:|---|---:|---|---:|
| 4 | 85 / 43,7311% | 34,211303 | parallel allowed | 2,837619 |
| 8 | 102 / 48,9427% | 32,032233 | memory-budget; требуется force diagnostic | 4,256428 |

Same-series ordinary median — **43,771112 с**, peak input — **1 418 809 344 bytes**.
Search обоих forecasts остановился по deadline с допустимым сохранённым планом:
495,5811 / 495,5029 мс; полное planning 681,8239 / 683,9998 мс включает XML/profile/
inventory чтение. Два дополнительных fresh offline N4 checks дали то же ownership,
core cost, group costs и candidate count; поисковое время отдельно не приравнивается.

SHA-256 N4: `530dd519b8f87a470e865466f687aec1c0aaff36642a17bf1f00eafd82e2cf2d`.
SHA-256 N8: `4d536952fff58af1a18c383254808ed23cf60977575294d2b128534351725e17`.
Полная запись с UTC, inputs и decisions —
ignored `roslyn-heldout-prediction-before-parallel.json`.
Сравнение с измеренными медианами и проверка решения об отказе приведены ниже;
исходные forecast numbers, timestamp и hashes сохранены без перезаписи.

## Результат и принятие

Финальные median rows, baseline/refusal comparisons, Roslyn forecast error,
force и диагностика прогрева сохранены в [summary.json](task-07.2-refusal-fast-planner-roslyn/summary.json)
и [warmup.json](task-07.2-refusal-fast-planner-roslyn/warmup.json). Gate остаётся
предзаданным: correctness во всех rows, PowerToys cluster N4 ≤75% baseline,
каждый pre-load refusal median ≤105%, actual non-force BuildHost peak ≤4 GB.
Post-load failures не являются допустимыми отказами. Roslyn N4 уже провалил
correctness: ordinary fallback не спасает gate. E1 не активируется; standalone
development checks и принятый runtime не являются production acceptance.

## Свежая серия: завершённая базовая матрица

Предварительный полный Roslyn baseline — `ok`, **50,054 с**, меньше 20 минут.
Same-series profile — `ok`, **425 variants: 376 C# + 49 VB**. Snapshot registration
и child binlog согласованы: NETCoreSdkVersion **10.0.301**, MSBuildVersion **18.6.4**,
MSBuildToolsPath и MSBuildSDKsPath — local clone `.dotnet/sdk/10.0.301`. SDK resolver
duration остаётся null, EvaluationProfilerAvailable=false.

Measured n=3 baseline medians: PowerToys **41,536039 с**, Files **10,9995943 с**,
Roslyn **43,7711123 с**. Завершены базовые комбинации, forced N8 и диагностика;
исходные artifacts сохранены, missing required matrix rows — **0**. Из 36 matrix
processes — 9 warmup и 27 measured. Дополнительно выполнены Roslyn prerequisite,
три ordinary profile runs (Files — дополнительный профиль) и две parallel N4
binlog diagnostics. Все вызовы измерительной серии выполнялись последовательно.

- PowerToys cluster N4: median wall **23,3059 с**, **56,11% baseline**; speed gate
  ≤75% проходит. Полное wall включает новый поиск/чтения и load.
- Files cluster N2: отказ до загрузки, обычный fallback; median wall **11,15987 с**,
  **101,46% baseline**, ниже заданных 105%. Причина `balance-infeasible`.
- Roslyn cluster N4 и greedy N4: measured attempts `plan-violation`; ordinary
  fallback `ok`, outer rows остаются failed, GateEligible=false. Их скорости не
  объявляются допустимым ускорением.
- Roslyn cluster N8: `memory-budget`, pre-load обычный fallback. Оценка 4,256428 GB
  проверена отдельной forced серией; actual peak **2,592756 GB**, но все три
  measured forced rows имеют `plan-violation`, поэтому не подтверждают безопасный
  параллельный результат и не проходят correctness.

Четыре первоначальных Roslyn CLI calls имели coordinator quoting error в --output
и завершились до workspace/load/BuildHost. Они сохранены отдельно как
`launcher-errors-before-load.json` (exit1, loadStarted=false, не timeout), после
них baseline 0/1/2/3 выполнены исправленной командой. Это launch failures, не
удалённые измерительные строки. PowerToys warmup0 имеет valid raw JSON; его MCP
receipt не сохранён после прерывания orchestration cell, что явно отмечается.

## Roslyn: механизмы расхождения graph/load

N4 cluster warmup0 и measured1 имеют одинаковые **12 violations**: шесть non-core
projects загружены несколькими группами, плюс шесть directory ownership violations.
Все шесть paths находятся в исходном solution и planned группе0.

- csc-arm64, vbc-arm64, VBCSCompiler-arm64 и
  `Compilers/Core/MSBuildTask/MSBuild/Microsoft.Build.Tasks.CodeAnalysis.csproj`
  реально возвращаются группами0/2/3. Дополнительные ProjectReferences находятся
  в `src/Setup/DevDivVsix/CompilersPackage/CompilersPackage.targets:7/9/11/13`.
  setup x64/x86/arm64 projects импортируют его на line20; x64 root запланирован
  группе2, x86/arm64 roots — группе3. `eng/Versions.props:139` задаёт
  UsingToolVSSDK=true на Windows. Literal XML graph читает top-level project XML,
  пропуская references импортированного targets.
- Razor Workspaces и Remote.Razor реально возвращаются группами0/3.
  Группа3 запрашивает Remote.Razor.CoreComponents.arm64; её project line7
  импортирует `CoreComponents.props`, line34 которого добавляет Remote.Razor
  с ReferenceOutputAssembly=false. Remote.Razor.csproj:33 ссылается на Workspaces.
  Imported load edge отсутствует в literal graph, хотя ROA=false требуется loader.

Отдельный механизм overapproximation literal conditional references: integration
tests project line32 содержит reference на RazorDeployment с условием
BuildDependencyVsix=true. Planner учитывает его без evaluation и исключает
RazorDeployment из roots. Deployment project lines16/19 ссылается на
RazorExtension.Dependencies и CoreComponents.x64. Все три planned группе0,
не запрошены roots и отсутствуют в attempted snapshot; ordinary запрашивает
все solution entries. Это согласуется с неактивным conditional edge; фактическое
evaluated значение BuildDependencyVsix не установлено, поэтому не объявляется
отдельно доказанной причинной настройкой.

Attempted measured1 содержит **422 variants вместо 425**, unresolved references
**12 вместо 0**, **66 unexplained differences**: 3 ProjectKeys +3 ProjectReferences
+60 Diagnostics, case-only=0. Новые 60 diagnostics — missing GetTargetPath, например
Microsoft.CodeAnalysis.CSharp.CodeStyle.UnitTests; причина этой дополнительной
context difference не атрибутирована. Ordinary fallback snapshot оценивается
отдельно и не подменяет comparison неудачной попытки.

В phase1 дополнительно обнаружены CodeLens (import
CoreComponents.Shared.targets:23) и Razor.Utilities.Shared (`$(SharedSourceRoot)`
reference Razor.Compiler.csproj:20). Это ещё два примера неполноты literal graph.
Frozen inputs не расширяются задним числом до evaluated graph; после-load
projects, properties и diagnostics не исправляются. Причина no-go сохраняется.

## Итоговые медианы и отказ

n=3, planning входит в wall; память — максимум отдельного measured run, десятичные
GB. Core — planned inventory/cost; у pre-load refusal он не загружался. Phase1 и
critical group ниже относятся к **параллельной попытке**, даже если после неё был
ordinary fallback. Ratio failed rows — описательное отношение затрат, не speedup
допустимого результата. Все baseline snapshots полностью совпали внутри серии.

| Решение / вариант | Решение planner / итог | Wall, с / ×baseline | Core / cost | Phase1 / critical group, с | Planning, с | Peak max, GB | Equivalence |
|---|---|---:|---|---|---:|---:|---|
| PowerToys baseline | ordinary / ok ×3 | 41,536 / 1,000 | — | — / 41,294 ordinary | — | 1,138 | identical |
| PowerToys cluster N4 | parallel / ok ×3 | 23,306 / 0,561 | 12 / 8,314% | 4,419 / 17,901 | 0,528 | 1,908 | identical, violations 0 |
| Files baseline | ordinary / ok ×3 | 11,000 / 1,000 | — | — / 10,882 ordinary | — | 0,213 | identical |
| Files cluster N2 | balance-infeasible / ordinary ok ×3 | 11,160 / 1,015 | rejected seed 4 / 56,361% | не запускалась | 0,063 | 0,206 | ordinary fallback identical |
| Roslyn baseline | ordinary / ok ×3 | 43,771 / 1,000 | — | — / 43,050 ordinary | — | 1,419 | identical |
| Roslyn cluster N4 | parallel / plan-violation ×3 | 82,266 / 1,879 | 85 / 43,731% | 19,923 / 18,161 | 0,623 | 1,961 | attempt unexplained 66/run; fallback identical |
| Roslyn cluster N8 | memory-budget / ordinary ok ×3 | 42,795 / 0,978 | 102→98 / 48,943→48,198% | не запускалась | 0,682 | 1,429 | ordinary fallback identical |
| Roslyn greedy N4 | parallel / plan-violation ×3 | 84,340 / 1,927 | 108 / 51,190% | 23,763 / 16,650 | 0,210 | 1,941 | attempt unexplained 66/run; fallback identical |
| Roslyn cluster N8 force | bypass memory / plan-violation ×3 | 83,984 / 1,919 | 102→98 / 48,943→48,198% | 22,901 / 17,016 | 0,685 | 2,593 | attempt unexplained 66/run; fallback identical |

Roslyn cluster N4 violations — 12/run, greedy N4 — 6/run, forced N8 — 8/run.
Все группы вернули Status=ok (12/12/24 measured group outcomes соответственно),
но ownership и snapshots не совпали. Group exception/timeout отсутствуют; статус
failed строки нельзя снять из-за успешного завершения group API.
Ordinary fallbacks совпадают с baseline во всех отказах и post-load failures.
Attempted 66 unexplained/run сохраняются отдельно от этого совпадения.

Busy cores / физических BuildHosts (median; failed rows включают fallback):
PowerToys baseline 1,424/2; cluster N4 4,126/10; Files baseline 1,537/1,
refusal 1,525/1; Roslyn baseline 1,461/1, cluster N4 2,138/6,
memory refusal 1,442/1, greedy N4 2,051/6, forced N8 2,690/18.
Sampler errors — 0, maximum measured sample gap **213,4041 мс**, меньше 1 с.
Force rows исключены из memory gate; их actual peak показан, а не скрыт.

PowerToys measured SearchMs **398,3442 / 396,8935 / 386,3244** мс, без deadline,
12 core projects; full planning median 527,7613 мс включает XML/profile/inventory/
estimate reads. Прежний cluster task-07.1: 12 projects, 10,38%, planning 6,608 с.
На прежнем immutable profile offline search воспроизвёл именно 12/10,38%; новая
серия имеет другой профиль и 8,314%, поэтому разницу cost share не следует
объявлять сравнением одинаковых весов. Все свежие PowerToys ownership совпали.
Files завершил exhaustive proof; search measured около 10 мс, pre-load ordinary
fallback добавил к медиане baseline **1,457%**, меньше допустимых 5%.

Roslyn N4 held-out forecast **34,211303 с**, failed attempted parallel плюс planning
median **39,425761 с**, ошибка **−13,226%**; весь wall с fallback **82,265509 с**,
ошибка **−58,414%**. N8 forecast **32,032233 с**, forced attempted+planning
**41,542969 с**, ошибка **−22,894%**; весь wall **83,983894 с**, ошибка **−61,859%**.
Знак — (prediction−observed)/observed. Это сравнение с неэквивалентными попытками:
успешная held-out validation модели не получена; fallback cost модель не предсказывала.

N8 memory heuristic оценил **4,256428 GB**, observed forced peak **2,592756 GB**,
завысив его примерно на **64,17%**. Memory refusal был консервативен в этой серии;
force показал меньшую память, но snapshot неправильный, а attempted+planning
**94,91% baseline** — выше speed boundary 90%. Не наблюдалось ни одной причины
no-expected-gain, которую корректная force загрузка могла бы опровергнуть.
Параметры и формула не перекалиброваны.

**Предел детерминизма:** фиксированный порядок обхода candidates воспроизводится,
но wall deadline обрывает его на разном месте. На completed PowerToys searches
планы совпали; ownership N4 стабилен в проверенных процессах. Universal identical
partition determinism **не установлен**, требование детерминизма не достигнуто
полностью: Roslyn N8 measured1/2/3 как без force, так и с force возвращал core
**102/98/98**, cost shares **48,942669%/48,197652%/48,197652%**. Все эти N8 searches
остановлены по deadline; число завершённых candidates без force —
**20 771/26 558/26 907**, с force — **20 849/26 594/26 943**. Cached best admissible
plan зависит от достигнутого места остановки. Это дополнительное ограничение
отрицательного эксперимента; fixed traversal не выдаётся за identical partition.
Forecast N8 до замера остаётся исходным **102-project** планом с неизменёнными
timestamp/hash/numbers; он не пересчитан по более поздним 98-project plans.

## Прогрев BuildHost: полученные данные и пределы вывода

Ordinary binlog diagnostics: PowerToys **42,088154 с**, Roslyn **44,281068 с**,
оба equivalent. Parallel PowerToys N4 — **22,062908 с**, equivalent, binlog count10;
Roslyn N4 — **84,748895 с** с failed parallel **39,959715 с** и последующим
ordinary fallback, plan-violation12. Последний diagnostic gate не проходит:
ordinary fallback не подменяет требуемую успешную parallel diagnostic.
Все diagnostic runs исключены из медиа́н и speed gate.

У Roslyn diagnostic `summary.json.diagnostics.diagnostic-roslyn-cluster-N4.unexplainedCount=0`
описывает **outer snapshot обычного fallback** (425 variants), а не успешную
эквивалентность попытки. `AttemptedParallel` и идентичный ему `.attempted.json`
содержат **422 variants**, `Status=plan-violation`, violations **12** и **66**
unexplained differences: 3 ProjectKeys +3 ProjectReferences +60 Diagnostics.
OrdinaryFallback/outer имеют 425 variants и unexplained0; outer Status всё равно
plan-violation, diagnostic equivalent=false. Эти сравнения не взаимозаменяемы.

Все 18 новых timing replays и прежний Roslyn ordinary replay — ok, read errors0.
PowerToys primary streams имеют format26 и project timing records; secondary
streams имеют format27 и **0 project events** (ordinary, phase1 и четыре группы).
Физический count2 не позволяет назвать их WPF workload или приписать им время
разборки проектов. Roslyn ordinary/phase1/группы/ordinary fallback — format25,
все project-bearing streams подтверждают local SDK301/MSBuild18.6.4.
SDK resolver duration недоступно; profiler false во всех streams.

Положительные attributed service increments по matched paths, sum четырёх групп:

- PowerToys **13,041138 с**: evaluation **5,797371 с (44,45%)**, exclusive targets
  **2,022344 с (15,51%)**, exclusive tasks **5,221422 с (40,04%)**. First-three
  callback proxy — **1,394302 с (10,69%)**, остальные — **11,646836 с (89,31%)**.
- Roslyn **26,028218 с**: evaluation **10,413772 с (40,01%)**, exclusive targets
  **3,245957 с (12,47%)**, exclusive tasks **12,368488 с (47,52%)**. First-three
  proxy — **2,029013 с (7,80%)**, остальные — **23,999205 с (92,20%)**.

Denominator этих долей — положительный прирост измеренных evaluation/exclusive
target/task service layers на matched paths; это **не** сумма wall residual по
группам. Inclusive parents/tasks не суммируются; отрицательные и net дельты тоже
сохранены в warmup.json. Например PowerToys group0 rest: net evaluation +1,341 с,
targets +0,297 с, tasks +0,540 с; Roslyn group2 rest: evaluation +2,782 с,
targets −1,162 с, tasks +1,015 с. Значительные отрицательные дельты показывают
несовпадающий context, а не чистую фиксированную надбавку.

Самые заметные дополнительные task sources: PowerToys CsWin32CodeGeneratorTask
**+199,737 мс** (group0 rest), MSBuild query **+118,817 мс** (group2 rest), Exec
**+103,746 мс** (group2 rest); targets — WindowsAppSDK payload **+55,011 мс**
(group1 rest). Roslyn — MSBuild query **+638,207 мс** (group1 first-three),
**+421,582 мс** (group2 rest); evaluation одного path **+428,165 мс** (group2 rest),
SplitResourcesByCulture target **+250,627 мс** (group1 rest). Exact paths, ordinary/
group values и top increments каждой части сохранены в compact warmup.json.

На каждый group stream один unmatched path — synthetic aggregator. Остальные
matched timing-only paths без public callback: PowerToys **17/20/23/17**, Roslyn
**50/42/77/72**; они включают nested queries и не объявляются впервые загруженными
проектами. Первые три — explicit callback completion proxy, не точный порядок
начала процесса. Phase1 streams и отдельный Roslyn ordinary fallback stream
сохранены в inventory и не включены в group increment comparison.

Наблюдаемая надбавка не сосредоточена в первых трёх matched projects, но **равномерная
надбавка каждому project не доказана**: есть крупные одиночные evaluation/query
дельты, отрицательные дельты, aggregator/IPC/GC/query gaps и отсутствие resolver
таймера. Полное причинное разложение прежнего residual около5 с не получено;
неатрибутированный остаток остаётся неизвестным. На основе чисел можно исследовать
reuse BuildHost/import caches, сокращение MSBuild reference queries и повторной
evaluation, а для PowerToys отдельно генератор CsWin32; прежде нужен полный
evaluated load graph и проверка correctness. Эти средства не реализованы,
антивирусы не перенастраивались.

## Gate, validation и целостность

Final experiment **no-go**: correctness FAIL; speed PASS (56,11%≤75%), refusal
overhead PASS (Files101,46%, RoslynN8 97,77%≤105%), memory PASS (maximum non-force
parallel1,960673 GB≤4). Missing required matrix rows0, loader/group timeout0,
неожиданные post-load violations сохранены. Неуспешная Roslyn diagnostic тоже
явно проваливает требуемую эквивалентность. Это отрицательный результат полного
эксперимента, не разрешение public/production/E1 activation.

Финальная validation координатора: Release spike и solution builds PASS;
четыре C# self-checks PASS; три Python regressions PASS. Full Release main
Category!=AnalyzerLifecycle реально выполнен: **1362 passed / 1 failed / 0 skipped**,
total1363, около97 с. В нём весь SourceStructure **123 passed**. Единственный
прежний SDK10.0.401 failure — Framework_selection_matches_installed_pack_files_without_loading_analyzers:
expected framework-task-input-closure-unproven, actual дополнительно
framework-selection-profile-unproven. Тест не изменён/не исключён; AnalyzerLifecycle
не требуется. Формальная code/tests acceptance pending main CI, commit не создан.
Подробный [review/validation](task-07.2-refusal-fast-planner-roslyn/validation.md)
ведёт координатор; финальное независимое data review round 2 — ACCEPT, 2026-10-10. Task/index statuses согласованы; scope — standalone реализация/evidence отрицательного эксперимента с указанными ограничениями.

[Runtime SHA inventory](task-07.2-refusal-fast-planner-roslyn/measured-source-sha256.json)
содержит **50** frozen C#/csproj; [послесерийная проверка](task-07.2-refusal-fast-planner-roslyn/post-series-source-integrity.json)
подтверждает 0 изменений, comparator/SolutionObservation byte-unchanged и все
**19** прежних evidence files неизменными. [Artifact hashes](task-07.2-refusal-fast-planner-roslyn/artifact-sha256.json)
связывают compact числа с **143** raw/report/timing/binlog/restore artifacts;
[analysis source hashes](task-07.2-refusal-fast-planner-roslyn/analysis-source-sha256.json)
отдельно фиксируют Python. Сам manifest не хэшируется своим содержимым; final
validation/status changes имеют отдельный coordinator inventory.
