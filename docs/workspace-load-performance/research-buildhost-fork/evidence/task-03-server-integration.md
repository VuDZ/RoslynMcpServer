# BuildHost/task-03 — форк BuildHost с A + B в сервере: отчёт

Дата: **2026-10-26**. Статус: **accepted, 2026-10-26** — Linux-проверки дополнены домашним Windows-прогоном:
Release build и publish успешны, main-набор 1467/0/0, `AnalyzerLifecycle` 13/0/0; Roslyn.slnx загружается с тем же
ответом на 17,4 % быстрее 1.5.16 ([приёмка](#приёмка-2026-10-26)). Контракт: [task-03](../task-03-buildhost-fork-server-integration-hi.md).

## Что сделано

- **Сборка.** `forks/roslyn-buildhost/build/BuildHostFork.targets`, импорт в `RoslynMcpServer.csproj`:
  - `ProjectReference` на проект форка (`ReferenceOutputAssembly=false`, `OutputItemType`) — restore и сборка форка
    вместе с сервером; командная строка сервера до форка не доходит (`-o`, `-r`, `--self-contained` убраны через
    `GlobalPropertiesToRemove`), `ValidateExecutableReferencesMatchSelfContained=false` (NETSDK1150 не относится к
    отдельному процессу BuildHost);
  - цель перед `AssignTargetPaths` убирает элементы `Content` пакета с `TargetPath` `BuildHost-netcore/…` и кладёт
    вместо них все файлы вывода форка плюс `License.txt` и `THIRD-PARTY-NOTICES.txt` Roslyn (`Always`, чтобы старые
    файлы пакета в выводе не пережили сборку);
  - ошибка сборки, если элементов пакета нет, если версия пакета не 5.9.0 или если форк ничего не собрал;
  - ссылка, цель и сам импорт не действуют при `DesignTimeBuild=true`: иначе `MSBuildWorkspace`, загружая сервер
    (свой репозиторий или плагин-пример), видел бы проекты форка как ссылки сервера.
- **Включение — свойствами workspace.** `BuildHostOptimizations.ForWorkspace` добавляет к глобальным свойствам
  `MSBuildWorkspace.Create` `RoslynMcpReferencesFirst` (1/0), `RoslynMcpEvaluateAhead` (потоки) и, для `.sln`,
  `.slnx`, `.slnf`, `RoslynMcpSolutionPath`. Форк (`InitializeReferencesFirst`, `InitializeEvaluateAhead`) забирает
  их до создания `ProjectCollection`; свойства побеждают переменные окружения форка, которые остаются для спайков.
  Запрошенные свойства для снимка происхождения анализаторов и кэша загрузки не меняются.
- **Настройки.** `buildhost-optimizations` / `ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS` (по умолчанию вкл.),
  `buildhost-evaluate-ahead-workers` / `ROSLYN_MCP_BUILDHOST_EVALUATE_AHEAD_WORKERS` (по умолчанию 4, 0 — без B);
  переменная побеждает файл. `get_mcp_server_info` показывает ключи и итог. README (оба языка), CHANGELOG,
  `RoslynMcp.jsonc.sample`.

## Первая версия включения и почему её заменили

Первая версия ставила переменные форка `ROSLYN_BUILDHOST_REFERENCES_FIRST` и `ROSLYN_BUILDHOST_EVALUATE_AHEAD` в
окружении процесса сервера перед каждой загрузкой: клиент 5.9.0 запускает BuildHost с окружением процесса. Main-набор
показал цену: окружение общее, и `RawImportEvidenceTests`, который сам создаёт `MSBuildWorkspace` и изучает binlog
host из пакета, получал A от загрузок `SolutionManager` в соседних тестах. С A в binlog загрузки нет ни одного
события evaluation: у upstream их даёт только повторная evaluation ссылки внутри сборки проекта (корневые evaluation
идут в коллекции без логгеров), а A эту evaluation и убирает. Это ожидаемое поведение A, а не ошибка; ошибкой было
включать его для чужих workspace. Свойства workspace действуют только на свой BuildHost.

Из production-кода события evaluation binlog читает только отчёт таймингов `DesignTimeBuildTimings`: с A в нём
меньше строк evaluation. Связывание анализаторов (`AnalyzerProvenanceCaptureService`) опирается на `ProjectStarted`
и элементы задач — проверяется набором `AnalyzerLifecycle`.

## Linux, 2 vCPU, 3,8 ГБ

| проверка | результат |
|---|---|
| build, `build -o`, publish `linux-x64` self-contained и framework-dependent, pack dotnet tool (все 7 пакетов) | в `BuildHost-netcore` — файлы форка (BuildHost.dll 135 680 байт против 112 464 у пакета, `5.9.0-fork+…`), лицензия Roslyn; `deps.json` сервера и корневой `Contracts.dll` — как без форка |
| загрузка `RoslynMcpServer.sln` опубликованным сервером через MCP (`load_workspace`), трасса BuildHost | 5 проектов; по умолчанию — `evaluate-ahead-start` с 4 потоками, решение взято (5 записей), 4 evaluation наперёд, 2 сборки ссылок заранее; с `ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS=0` — ни одного span A и B |
| тесты настроек, `ForWorkspace`, форк в выводе тестов, бывшие падения (`RealControlCapability`, `OrdinaryWorkspaceCapture`, `SnapshotSchema`, `CrossProcessRoundtrip`, `RawImportEvidence`) | 57 passed |

## История main-набора на Linux: OOM машины

- Первый запуск (первая версия, переменные окружения): 1 405 passed, 5 failed — 4 теста на число проектов
  (проекты форка в workspace, исправлено условием `DesignTimeBuild`) и `RawImportEvidenceTests` (A у чужого
  workspace, исправлено переходом на свойства). Все пять проходят после исправлений (57 passed выше).
- Полный набор после исправлений три раза убит системным OOM-killer: на машине 3,8 ГБ, процессы вне тестов держат
  около 2,2 ГБ, swap заполнен. С `ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS=0` — тоже OOM (пик 1,7 ГБ против 1,5 ГБ с
  оптимизациями), так что причина — память машины, а не A + B. Результата полного набора нет.
- Решение владельца (2026-10-26): закоммитить, полный main-набор и `AnalyzerLifecycle` владелец прогоняет на
  домашнем компьютере. На момент Linux-прогона — pending; закрыто Windows-проверкой ниже.

## Windows, домашний компьютер, 2026-10-26

Стенд: Windows 11 Pro 10.0.26200, Ryzen 7 9800X3D (8 ядер, 16 логических), 93,7 ГиБ RAM, около 67 ГиБ свободно.
SDK сборки/тестов — 10.0.402. Проверяемый commit — `de61f922bda0e54efcfd6ecd2b34d76fe1ddcd83`.
Он уже находился в рабочем каталоге, повторный `git pull` не выполнялся. Изменённых tracked-файлов перед проверкой
не было; существующие untracked C#-фикстуры и тесты вошли в сборку, поэтому числа относятся к этому рабочему дереву.
Production-код и тесты при проверке не редактировались. Переменные переопределения A/B и `DOTNET_gcServer` не заданы,
файла `RoslynMcp.jsonc` рядом с проверяемыми серверами и в каталоге решения нет: A + B включены по умолчанию, B — 4 потока.

### Сборка и тесты

Выполнены запрошенные команды; к тестам добавлены только TRX logger и каталог результатов:

```powershell
dotnet build RoslynMcpServer.sln -c Release
dotnet test RoslynMcpServer.Tests -c Release --no-build --filter "Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk"
$env:ROSLYN_MCP_ANALYZER_LIFECYCLE = '1'
dotnet test RoslynMcpServer.Tests -c Release --no-build --filter "Category=AnalyzerLifecycle"
```

- **Release build — succeeded**, через `run_dotnet_build` с `noIncremental=false`, как у запрошенной команды.
  Предупреждения: CS8603 (`NuGetFallbackAssemblyResolver.cs:72`), CS8601 (`HostSession.cs:1177`), xUnit1031
  (`WpfTemporaryProjectCallbackTests.cs:195`). Ошибок нет.
- **Main — 1467 passed, 0 failed, 0 skipped**, 1 мин 34 с. Внутри него **все 123 SourceStructure passed**;
  отдельного повторного запуска не требовалось.
- **AnalyzerLifecycle — 13 passed, 0 failed, 0 skipped**, 48 с, с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`.
  Проверены реальные загрузки, provenance/admission, shadow-copy, публикация, смена версии анализатора,
  перезапуск, watcher и отказы replay/capture при включённых по умолчанию A + B.
- `.github/scripts/Assert-NoSkippedTests.ps1` проверил оба итоговых TRX: **1480 discovered/executed/passed,
  0 failed, 0 notExecuted**.
- Первый запуск main в песочнице закончился **aborted до выполнения тестов**: VSTest не соединился с testhost
  за 90 с. Его TRX сохранён отдельно и не считается успешным. Оба итоговых набора выполнены вне песочницы;
  OOM и таймаутов тестов не было.

Дополнительно выполнен `dotnet publish RoslynMcpServer.csproj -c Release -r win-x64 --self-contained true -o <current-server>`:
успех, один CS8603. В опубликованном `BuildHost-netcore` DLL форка — 136192 байта,
`5.9.0-fork+35d9211b841e7613c1d2f8f5af6d628ace696c4c`, SHA-256
`BE05CE298768C76B361732ADC300CAFA3555FC97E2EC91B69049F88E16E0AABA`; `License.txt` и
`THIRD-PARTY-NOTICES.txt` присутствуют. В выводе тестов также проверена версия `5.9.0-fork+…`.
Pack повторно не выполнялся: его успешная Linux-проверка сохранена выше.

### Загрузка Roslyn.slnx против 1.5.16

Решение — `roslyn-deep/Roslyn.slnx`, commit `013d3a758df6c137497ff37a93f0d4bed103853a`, 434 проекта;
workspace использует SDK 10.0.301 по `global.json`. Эталон — локальный архив `artifacts/RoslynMCP-1.5.16.zip`,
сервер `1.5.16+3b8fd820c5327d84e6565d3b6f8893c324c2fa48`, SHA-256 архива
`D0EAC39B093D3209C5CB9A243FAAE70CB33D70214AF2C4166022BC75C7BC0D60`.
Обе стороны — self-contained win-x64 на runtime 10.0.13 с server GC.

Скрипт `scripts/Measure-WorkspaceLoad.ps1`, `-Runs 4 -SkipRamHit -FullOutput`, без запроса поиска:
на каждой стороне прогрев и три измеряемых загрузки в новых процессах. Сначала весь baseline, затем текущий
сервер; тесты и publish к этому времени завершены. Affinity не ограничивалась, антивирус не отключался.

- **1.5.16:** прогрев 56507 мс; измерения в порядке запуска — 53416, 54129, 54351 мс; **медиана 54128,653 мс**.
- **Task-03, A + B:** прогрев 44616 мс; измерения — 44728, 44937, 44470 мс; **медиана 44727,871 мс**.
- Отношение **×0,8263**, время загрузки меньше на **9401 мс / 17,37 %** на этом стенде.
- **Все восемь полных ответов `load_workspace` после штатного редактирования путей скриптом побайтово одинаковы**:
  SHA-256 `46FB6DC040D49719876F17FB3FDA5DC56EFF144D20067F4B2B813CB0731ADFA0`.
  Совпадают 434 проекта и диагностики в том же порядке. Это сравнение ответа инструмента;
  полный внутренний snapshot workspace в этом прогоне отдельно не сравнивался.
- Во всех восьми загрузках provenance capture — **Complete**, `ProjectContexts=10943`, `AnalyzerItems=17`,
  `ConfirmedBindings=3`, `FailureCodes` пуст. Binlog replay с A + B сохранил эти показатели.
- На обеих сторонах остаются **44 MSBuild Failure и 43 Warning** в серверном логе (в том числе неподдерживаемые
  `.shproj` и диагностики design-time build). Это прежние диагностики стенда; регрессии ответа нет.
- Ошибочных прогонов и оставшихся дочерних процессов — **0**.

Это локальное сравнение с прогретым файловым кешем и последовательным порядком сторон, без контроля холодного
кеша. Оно не задаёт универсального ускорения для других решений. Офисное решение и первый семантический запрос
в этой проверке не запускались; `RequiresPinnedSdk` исключён по запрошенному CI-фильтру.

### Файлы проверки

Полные данные лежат локально в ignored
`artifacts/buildhost-fork/task-03-home-2026-10-26/`:

- `environment.json`, `comparison.json` — стенд, рабочее дерево и проверка совпадения ответов;
- `main-unsandboxed.log`, `main-unsandboxed.trx`, `lifecycle.log`, `lifecycle.trx`, `final-trx/` — успешные прогоны;
- `main.log`, `main.trx` — первоначальный aborted-запуск в песочнице;
- `publish-win-x64.log`, `current-server/`, `baseline-1.5.16/` — публикация и обе стороны;
- `load-baseline/` и `load-current/` — `summary.md`, `runs.json`, ответы и выдержки логов всех запусков.

## Приёмка 2026-10-26

**Task-03 accepted:** интеграция production BuildHost с A + B по умолчанию и четырьмя потоками B проверена
Release build, полным main CI, SourceStructure, AnalyzerLifecycle, Windows publish и реальной загрузкой
Roslyn.slnx без регрессии ответа. Linux build/publish/pack из первой части отчёта дополняют эти результаты.
Pending домашней проверки закрыт. Настройка числа потоков, перенос в `main` и релиз остаются вне этой приёмки.
