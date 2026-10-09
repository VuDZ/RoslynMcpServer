# E0/task-07 — isolated restore benchmark

Дата: **2026-10-09**, Europe/Moscow. Прогон: 2026-10-08T21:37:25Z.
Статус задачи: **accepted, round 2, 2026-10-09** для bounded SamplePlugin benchmark runner и report.
Независимое [ревью / validation](task-07-review-round-2.md): T07-R1-001/002/003 закрыты,
3 исправления подтверждены, открытых must-fix findings нет; исходные SamplePlugin numbers не переписывались.
Production cache не включался.
Приёмка эпохи и production readiness **не объявляются**.

Числа median/min/max ниже скопированы из [summary.json](task-07/summary.json).
Отдельного пересчёта нет. Исполнитель: Grok 4.7, текущая сессия, без subagents.

Поправка round 1 подтверждена независимым recheck round 2. Раннер до замеров принимает только контроль SamplePlugin и сверяет `declaration` до медианы. Child success требует свежий run id, exit code 0 и TRX 1/1/0/0. Журнал исходного прогона скопирован в [run.txt](task-07/run.txt); summary не пересчитывался. Новый [положительный прогон](task-07-review-round-2/positive/summary.json) сохранён отдельно: ordinary 5/5, restore 5/5, partial=false, BTCPay отдельно unsupported.

Раннер: [roundtrip_benchmark.py](../../../../scripts/workspace-state-cache/roundtrip_benchmark.py).
Ordinary-нога использует [ordinary_baseline.py](../../../../scripts/workspace-state-cache/ordinary_baseline.py) без смены его протокола `e0-ordinary-baseline-1`.
Как устроены стадии: [README раннера](../../../../scripts/workspace-state-cache/README.md).

## Контроль и заранее названный ответ

Workspace: `samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj`.
Query: `find_symbol_definition`, `symbolName=SamplePlugin`.
Ожидаемое объявление, названное до прогона: `RoslynMcpPlugin.SamplePlugin` в `samples/RoslynMcpPlugin/SamplePlugin.cs`.

Ordinary-успех — это объявление в ответе MCP. Restore-успех в новом PID — тот же тип в hydrated compilation, файл `RoslynMcpPlugin\SamplePlugin.cs`, интерфейс `RoslynMcpServer.Plugins.IRoslynMcpPlugin` из сборки `RoslynMcpServer`, и отсутствие compiler errors. Промах в медиану не входил. Промаха не было.

`configuration`, `platform`, `targetFramework`, `buildArgs` и `shadowCopyInSolutionAnalyzers` не передавались.
Evaluated instance, снятый вне таймера: Configuration `Debug`, Platform `AnyCPU`, TargetFramework `net10.0`.
[evaluated-instance.json](task-07/evaluated-instance.json).

## Publish и restore корпуса вне тайминга

NuGet restore корпуса не запускался. Publish Release `win-x64` закончился до warmup.
[task-07-publish.json](task-07-publish.json): 2026-10-09T00:33:16+03:00 … 00:33:49+03:00, 32359.8 ms, код 0.
Exe: `bin\Release\net10.0\win-x64\publish\RoslynMcpServer.exe`, FileVersion `1.5.6.0`, product `1.5.6+09cb8ea2…`.
Эта длительность не входит в load, semantic, useful, capture, read, decode, hydrate и query.

## Один compare key

Идентификатор: `e6c127a0c8dad57d03998ab9caa2ba67e3037046c7b9c1ca8df030053f2aad58`.
Объект: [compare-key.json](task-07/compare-key.json).

Ключ общий для ordinary и isolated restore. Метка сценария внутри ключа — `e0-paired-ordinary-and-isolated-restore`, а не отдельный ключ на каждую ногу. Нога записана в attempt (`forced-ordinary-baseline` или `isolated-cross-process-restore`). Медиана каждой ноги берётся только из успешных measured-попыток с этим id.

| Поле | Значение |
|---|---|
| server commit / workspace revision | `09cb8ea2f8a82119894384774578cd5b11d3f0f2` |
| server file version | `1.5.6.0` (product `1.5.6+09cb8ea2…`) |
| source / assembly / file version | `1.5.6` / `1.5.6.0` / `1.5.6.0` |
| OS | `Windows-11-10.0.26200-SP0` |
| CPU | AMD Ryzen 7 9800X3D 8-Core Processor, `cpuCount` 16 |
| RAM | 100562268160 байт |
| content identity | `19f8d2de826006e5724da98503026ebb85599da395739c4646973738d2a5ff27` |
| scope identity | csproj и исходники project reference, 232 файла |
| SDK | `10.0.300` |
| MSBuild | host `18.6.3.23102`, package pin `18.6.3` |
| Roslyn package | `5.9.0` |
| raw requested | configuration, platform, targetFramework, buildArgs, shadowCopy: `absent` |
| loader globals before open | fresh process; inherited Loaded* `absent`; `RoslynMcp.jsonc` нет |
| evaluated instance | Configuration `Debug`, Platform `AnyCPU`, TargetFramework `net10.0` |
| overlay | off |
| query | `find_symbol_definition:SamplePlugin` |
| H-001 / H-002 | external XML excluded / strong-name variant B |
| cache hit | false |

Полная XML-doc эквивалентность и точный перенос strong-name state не заявляются.
Дерево на старте было грязным: `mutationsAtStart` в compare-key.json. Между попытками identity не менялся.

Это не cache hit и не RAM-hit. Файловый кэш ОС не сбрасывался: new-process/warm-files.

## Ordinary

Новый процесс `RoslynMcpServer.exe` на попытку. `loadMs` и `semanticMs` — от отправки `tools/call` до ответа. `usefulMs` = сумма. `startupMs` в useful не входит. Один warmup вне медианы. Итог: `partial=false`.

| # | Роль | PID | Исход | loadMs | semanticMs | usefulMs | В медиане |
|---|---|---|---|---|---|---|---|
| 0 | warmup | 33968 | success | 1425.3 | 916.2 | 2341.5 | нет |
| 1 | measured | 27172 | success | 1451.1 | 905.0 | 2356.1 | да |
| 2 | measured | 76600 | success | 1427.3 | 889.6 | 2316.9 | да |
| 3 | measured | 73572 | success | 1414.0 | 885.6 | 2299.6 | да |
| 4 | measured | 15688 | success | 1419.3 | 898.9 | 2318.2 | да |
| 5 | measured | 64548 | success | 1442.8 | 881.8 | 2324.6 | да |

Measured: successes 5, errors 0, timeouts 0.

| Метрика | n | median | min | max |
|---|---|---|---|---|
| load | 5 | 1427.3 ms (1.43 s) | 1414.0 ms (1.41 s) | 1451.1 ms (1.45 s) |
| semantic | 5 | 889.6 ms (0.89 s) | 881.8 ms (0.88 s) | 905.0 ms (0.91 s) |
| useful | 5 | 2318.2 ms (2.32 s) | 2299.6 ms (2.30 s) | 2356.1 ms (2.36 s) |

Peak working set ordinary — выборка примерно раз в 2 с у процесса exe, не `PeakWorkingSet64`. Median 246198272 байт (min 242552832, max 247042048). В useful не входит.

Стадии capture/read/decode/hydrate/write у ordinary MCP-вызова `not-measured`, не ноль: сервер их наружу не отдаёт.

## Isolated restore

На попытку два новых PID тестового хоста. Capture пишет envelope. Другой PID его читает. Capture в `usefulMs` restore не входит и из отчёта не убран: overhead стоит рядом.

`captureMs` — только `CaptureAsync`. Ordinary MSBuild load, который capture сам не открывает, — `ordinaryLoadMs`. Encode и запись файла — `writeMs`. `captureOverheadMs` = сумма трёх.
`hydrateMs` включает повторный encode/decode внутри принятого hydrator. Это не перенесено в отдельный decode, чтобы не замерять другую программу.
`queryMs` — поиск `RoslynMcpPlugin.SamplePlugin`, проверка файла, интерфейса и compiler errors. Первое реальное обращение к compilation сидит здесь.

| # | Роль | capture PID | restore PID | captureMs | readMs | decodeMs | hydrateMs | queryMs | usefulMs | В медиане restore |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | warmup | 63832 | 61004 | 1545.6 | 1.5 | 79.3 | 1144.4 | 193.1 | 1418.3 | нет |
| 1 | measured | 65072 | 39852 | 1546.7 | 1.4 | 78.0 | 1133.9 | 192.1 | 1405.4 | да |
| 2 | measured | 27344 | 30068 | 1550.4 | 8.4 | 79.0 | 1126.2 | 192.0 | 1405.6 | да |
| 3 | measured | 76012 | 58612 | 1544.6 | 8.1 | 81.4 | 1134.5 | 190.6 | 1414.6 | да |
| 4 | measured | 14504 | 78400 | 1531.3 | 1.5 | 78.1 | 1139.5 | 194.1 | 1413.2 | да |
| 5 | measured | 33756 | 43916 | 1552.5 | 1.4 | 77.7 | 1155.4 | 192.3 | 1426.8 | да |

Все PID ordinary, capture и restore различны. В каждой паре capture PID отличается от restore PID. Large PID 57840 тоже другой. Раннер не нашёл совпадений (`partial=false`).

Measured restore: successes 5, errors 0, timeouts 0. Capture successes на том же ключе: 5.

| Стадия | n | median | min | max |
|---|---|---|---|---|
| ordinary load внутри capture | 5 | 1416.3 ms (1.42 s) | 1406.9 ms (1.41 s) | 1435.5 ms (1.44 s) |
| capture | 5 | 1546.7 ms (1.55 s) | 1531.3 ms (1.53 s) | 1552.5 ms (1.55 s) |
| write | 5 | 77.7 ms (0.08 s) | 75.0 ms (0.07 s) | 79.2 ms (0.08 s) |
| capture overhead | 5 | 3035.0 ms (3.04 s) | 3022.6 ms (3.02 s) | 3062.4 ms (3.06 s) |
| read | 5 | 1.5 ms | 1.4 ms | 8.4 ms |
| decode | 5 | 78.1 ms (0.08 s) | 77.7 ms (0.08 s) | 81.4 ms (0.08 s) |
| hydrate | 5 | 1134.5 ms (1.13 s) | 1126.2 ms (1.13 s) | 1155.4 ms (1.16 s) |
| query | 5 | 192.1 ms (0.19 s) | 190.6 ms (0.19 s) | 194.1 ms (0.19 s) |
| useful restore | 5 | 1413.2 ms (1.41 s) | 1405.4 ms (1.41 s) | 1426.8 ms (1.43 s) |

Disk envelope на пяти успешных capture: 516268 байт, min = max.
Peak working set — `PeakWorkingSet64` процесса testhost, включая хост, не только стадию:

| Процесс | median | min | max |
|---|---|---|---|
| capture | 337096704 байт | 334577664 | 337149952 |
| restore | 280387584 байт | 278081536 | 280518656 |

p95 не считался и не обещается: n=5, порог протокола — 20.

Useful restore (1.41 s) меньше useful ordinary (2.32 s) на этом ключе. Capture overhead (3.04 s) в это useful не входит. Сложение двух медиан медианой не является и за ускорение load не выдаётся. Для E1 capture, который происходит до ответа load, остаётся внутри useful.

## Большой корпус, отдельно

Кандидат backlog, не контроль этой медианы: `E:\Devel\roslyn-mcp-bench\clones\btcpay-web\BTCPayServer\BTCPayServer.csproj`.
Один новый PID 57840. Исход: **unsupported**, причина `generator-dependent-scope-unsupported`.
Ordinary load 3627.1 ms, capture 7427.9 ms, envelope не сохранён.
[large-target.json](task-07/large-target.json). В медиану RoslynMcpPlugin не входил. Cache hit не объявлен. Профиль и csproj под этот корпус не подгонялись.

## Raw

Каталог: [task-07/](task-07/).

- [summary.json](task-07/summary.json) — счётчики, median/min/max, таблицы PID
- [compare-key.json](task-07/compare-key.json)
- [evaluated-instance.json](task-07/evaluated-instance.json)
- [content-identity.json](task-07/content-identity.json)
- [run.txt](task-07/run.txt)
- [task-07-publish.json](task-07-publish.json)
- ordinary: [warmup](task-07/ordinary/attempt-00-warmup.json), [1](task-07/ordinary/attempt-01.json), [2](task-07/ordinary/attempt-02.json), [3](task-07/ordinary/attempt-03.json), [4](task-07/ordinary/attempt-04.json), [5](task-07/ordinary/attempt-05.json)
- restore: [warmup](task-07/restore/attempt-00-warmup.json), [1](task-07/restore/attempt-01.json), [2](task-07/restore/attempt-02.json), [3](task-07/restore/attempt-03.json), [4](task-07/restore/attempt-04.json), [5](task-07/restore/attempt-05.json)
- стадии и PID лежат в каталоге попытки (`capture-stages.json`, `restore-stages.json`)
- large: [large-target.json](task-07/large-target.json), каталог [large/](task-07/large/)

## Пределы

Не измерялись, и это не нули: input enumeration, stat, text hashing, binary sample/read, prepare, publication, visited entries, bytes hashed, unique binaries, dirty/reloaded projects, Open/DTB observations.

H-001: внешний XML исключён. H-002: strong-name scope — вариант B.
Isolated restore — не production cache и не RAM-hit.
n=5, p95 нет.

## Предложение scope E1

Опора — рабочий real round-trip task-06 и этот прогон, не синтетический DTO.

Положительный disk-hit E1 имеет смысл начинать с того же контроля: `RoslynMcpPlugin.csproj`, Debug / net10.0, absent raw request и loader globals, overlay off, query `SamplePlugin`, профиль H-001/H-002. Сравнение E1: минимум пять успешных попыток, тот же compare key и тот же ожидаемый query; useful median unchanged hit меньше forced ordinary useful median. На этом прогоне ordinary useful median — 2318.2 ms. Число 1413.2 ms сюда подставлять нельзя: это restore при уже лежащем envelope, без capture.

Capture overhead этого контроля — 3035.0 ms median. Если E1 делает capture до ответа load, это время остаётся внутри useful. Отдельный isolated useful не доказывает hit.

BTCPay на этом профиле — `generator-dependent-scope-unsupported`. Для E1 это whole-request ordinary, не цель hit и не повод менять корпус. S-002 по-прежнему not-run; manifest task-00 без него не принимается. Store, host, preparation, watcher, write и fallback остаются задачами E1. Этот бенчмарк их не выполняет.

Default cache не включается. Приёмка E0 этим отчётом не закрывается:
исправления T07-R1-001/002/003 подтверждены [независимым recheck round 2](task-07-review-round-2.md),
task-07 принята в bounded scope, отдельная приёмка эпохи не объявлена.

## Validation

Production C# не менялся. Версия `RoslynMcpServer.csproj` осталась `1.5.6`.
Release-сборка тестового проекта после последней правки harness прошла.
Известные предупреждения без новых в harness: CS8603 в `NuGetFallbackAssemblyResolver`, CS8601 в `HostSession`, xUnit1031 в `WpfTemporaryProjectCallbackTests`.

Полный main suite `Category!=AnalyzerLifecycle`, Release, `--no-build` после этой сборки, как у CI после отдельного build:
**1359 passed, 0 failed, 0 skipped**. Passed = Total. `Assert-NoSkippedTests.ps1` подтвердил total=1359, executed=1359, notExecuted=0.
Повтор после поправки round 1, тем же фильтром и Release `--no-build`: снова **1359 passed, 0 failed**. Total = Passed.
Независимый recheck round 2: повторные Release build решения и main suite **1359/1359**, 0 failed, 0 skipped — [validation](task-07-review-round-2/validation.json).
Новый факт — `IsolatedRestoreBenchmarkTests.Attempt_requires_a_runner_directory`: без handoff он только проверяет отказ и не измеряет workspace. В прогоне бенчмарка тот же метод с handoff выполняет одну попытку.

AnalyzerLifecycle **not-run / not required**: production analyzer, shadow-copy, load, prepare, publication и lifecycle host не менялись.
SourceStructure входит в этот main suite. Exact structural limits не менялись.

Ранний smoke capture упал за 44 ms: `Microsoft.Build.Framework` грузился раньше `MsBuildBootstrapper.Register`, потому что регистрация стояла в том же методе, что и MSBuild-типы. Регистрация перенесена в вызывающий тест до компиляции capture. Повторный smoke и полный прогон после этого успешны. Тот ранний отказ в медиану не входил; каталог smoke удалён.
