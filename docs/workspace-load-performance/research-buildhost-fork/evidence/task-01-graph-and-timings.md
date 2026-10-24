# BuildHost/task-01 — тайминги запросов и фактический граф загрузки: отчёт

Дата: **2026-10-24**. Статус: **in progress** — инструмент готов и проверен на Linux; прогоны на Roslyn.slnx
(Windows) и в офисе — у владельца. Контракт: [task-01](../task-01-buildhost-graph-and-timings-hi.md).

## Как устроена загрузка 5.9.0

Прочитано в исходниках клиента `dotnet/roslyn@35d9211b` (пакет 5.9.0) и форка; это определяет метод.

- **Клиент загружает проекты строго по одному.** `MSBuildProjectLoader.Worker.LoadAsync` идёт по проектам
  решения в цикле с `await`. Для каждого: RPC `LoadProjectFileAsync` (в host — корневая evaluation),
  `GetDiagnosticLogItems`, RPC `GetProjectFileInfosAsync` (design-time build по каждому TFM и извлечение
  `ProjectFileInfo`), снова `GetDiagnosticLogItems`, затем на клиенте — разбор командной строки, документы и
  разрешение ссылок. Ссылка на ещё не загруженный проект загружает его внутри этого разрешения (в глубину).
  Поэтому в каждый момент выполняется не больше одного запроса, а загрузка — одна последовательная цепочка;
  `MaxNodeCount` BuildHost при одной отправке за раз ничего не распараллеливает.
- **Корневые evaluation в binlog не попадают.** `ProjectBuildManager` создаёт `ProjectCollection` без
  логгеров (обход dotnet/msbuild#11867), binlog получает только сборки и evaluation внутри них. Это и есть
  часть «промежутков» task-00a.
- **Host кеширует `Project` по одному пути, без свойств.** Повторный `LoadProjectAsync` того же пути отдаёт
  уже загруженный `Project`. Несколько TFM — один `Project`: host ставит глобальное `TargetFramework`,
  переоценивает, строит, а в конце возвращает исходное значение и переоценивает ещё раз (для двух TFM —
  три повторные evaluation).
- **Запрос output path** (`TryGetProjectOutputPathAsync`) клиент делает только для ссылки на проект, который
  workspace загрузить не может (например `.vcxproj` или `.proj`); он тоже проходит через этот кеш.

## Метод

Форк BuildHost получил трассировку (изменение форка, upstream-стиль, без смены протокола):
`ROSLYN_BUILDHOST_TRACE_DIRECTORY` — каталог, куда host пишет `buildhost-<pid>.jsonl`. Без переменной
трассировка не создаёт объектов и ничего не пишет. Spans (вложенные, по async-потоку):

- RPC: момент чтения строки запроса, начало обработки (разница — очередь пула потоков), вызов метода,
  сериализация ответа, отправка с размером ответа;
- `evaluate` (корневая, с числом imports и вычисленными Configuration/Platform/TFM), `reevaluate` (смена TFM
  и возврат), `load-project` (с признаком кеша), `output-path`, `create-build-manager`;
- `build-project` → `build-instance` (TFM) → `create-instance`, `msbuild-build` (цели, результат);
- `extract` (`ProjectFileInfo` по TFM: число документов, ProjectReference с `ReferenceOutputAssembly`).

Клиент не форкается. Публичный `IProgress<ProjectLoadProgress>` даёт операции Evaluate (обёртка
`LoadProjectFileAsync`), Build (`GetProjectFileInfosAsync`) и Resolve (разрешение ссылок) с длительностью;
`Report` вызывается синхронно сразу после остановки таймера, поэтому момент вызова — конец операции.
Spike пишет их в `client.json` вместе с окном самого `OpenSolutionAsync` (без MEF и создания workspace —
сервер делает это раз за процесс) и записями файла решения.

**Часы.** `Stopwatch` в Windows (QueryPerformanceCounter) и Linux (CLOCK_MONOTONIC) — общий для процессов
машины, поэтому spans host ставятся между отметками клиента без синхронизации. Это проверяется на каждой
загрузке: каждый запрос `LoadProjectFileAsync` и `GetProjectFileInfosAsync` должен лежать внутри своей
операции Evaluate или Build (допуск 1 мс: клиент может прочитать ответ раньше, чем host закроет span
отправки). Только время старта процесса host берётся из wall clock и помечено как оценка.

**Разбивка wall.** Окно `OpenSolutionAsync` делится по тому, что выполнялось: собственное время span host
(минус его дочерние spans), иначе самая внутренняя операция клиента без работы host под ней, иначе работа
клиента до первой, между и после операций. Клиент последователен, поэтому части складываются в wall и
образуют critical path; моменты с несколькими spans host одновременно считаются отдельно.

**Граф.** Узлы — проекты в эффективном контексте (путь, TFM сборки, вычисленные Configuration/Platform),
записи решения и проекты, известные только по ссылке. Рёбра с происхождением: ProjectReference
собранного проекта (с `ReferenceOutputAssembly`), загрузка проекта клиентом во время Resolve другого,
запрос output path во время Resolve. Не наблюдаются: ссылки, которые MSBuild разрешает внутри
design-time build (протокол ProjectReference вызывает задачу MSBuild); они есть только в binlog.

Инструменты: `spikes/BuildHostFork` — `observe` пишет трассу при заданной переменной, `trace-report`,
`trace-summary`; `Invoke-BuildHostForkBaseline.ps1 -Trace` трассирует только сторону форка, поэтому
сравнение task-00 проверяет, что трассировка ничего не меняет, а отношение загрузок показывает её цену.
Для решения вне репозитория отчёты называют проекты заглушками, карта заглушек остаётся на машине.
`spikes/LoadTimeline` (task-00a) получил таблицу вложенных сборок по запрошенным целям.

## Проверка на фикстуре, Linux

`spikes/BuildHostFork/Test-BuildHostTrace.ps1`: App → Lib (`net10.0;net8.0`, для net8.0 условный import
добавляет файл), App → Tool (`ReferenceOutputAssembly=false`, не в решении), App и Second → Native
(SDK-проект с расширением `.proj`: workspace его не загружает, клиент и на Windows отправляет его в
netcore-host, а `.vcxproj` NuGet считал бы нативным и не восстановил бы App),
Second → Lib с `SetTargetFramework`. **14 из 14 проверок**:

- все запросы внутри своих операций клиента;
- Lib: две сборки (по TFM), одна корневая evaluation и три повторные (два TFM и возврат), у net8.0 на один
  документ больше (условный import);
- App → Tool — ребро-элемент с `ReferenceOutputAssembly=false`; Tool и Lib загружены клиентом во время
  Resolve у App, Lib — раньше своей записи в решении;
- Second → Lib — ребро-элемент; его `SetTargetFramework` действует внутри design-time build, вне трассы;
- Native: два запроса output path (от App и от Second), второй — из кеша `ProjectCollection`; Native —
  узел без сборки, известный только по ссылкам.

## `RoslynMcpServer.sln`, Linux, 2026-10-24

2 vCPU, SDK 10.0.113; `Invoke-BuildHostForkBaseline.ps1 -Trace`, прогрев и 3 раунда.

- Трассированный форк против оригинала без трассировки: **identical**, natural 0, **equivalent**.
- Загрузка (весь `SolutionLoader`, медианы): оригинал 6 960 мс, форк с трассировкой 7 253 мс (×1,042);
  в первом прогоне того же дня ×1,019. На этой машине разброс раундов до 15 %, цена трассировки отдельно
  от него не выделяется; её оценит прогон на Windows.
- Часы: 40 из 40 запросов внутри своих операций, нарушений нет; минимальный зазор 0,08 мс до приёма,
  0,39 мс после отправки.

`OpenSolutionAsync`, медиана 3 раундов 6 033 мс:

| что выполнялось | мс | доля |
|---|---|---|
| host: design-time build | 1 956 | 32,4 % |
| host: корневая evaluation | 1 398 | 23,2 % |
| host: извлечение `ProjectFileInfo` | 1 025 | 17,0 % |
| клиент: между операциями | 344 | 5,7 % |
| клиент: до первой операции | 329 | 5,5 % |
| клиент: после последней операции | 171 | 2,8 % |
| host: старт процесса (оценка) | 149 | 2,5 % |
| host: старт `BuildManager` | 101 | 1,7 % |
| host: очередь RPC | 90 | 1,5 % |
| host: сериализация ответа | 86 | 1,4 % |
| клиент: Resolve без работы host | 85 | 1,4 % |
| host: прочее в RPC-методах | 83 | 1,4 % |
| клиент: ожидание Build / Evaluate без работы host | 84 | 1,4 % |
| host: остальное (instance, отправка, разбор запроса, чтение файла, ожидание подключения) | 119 | 2,0 % |

- Работа host — около 83 % окна, клиент — около 17 %. Транспорт и разбор ответа на клиенте (ожидание
  без работы host) — 1,4 %, очередь пула потоков в host — 1,5 %: RPC не узкое место.
- **Первый проект в свежем процессе дорог.** Корневая evaluation `RoslynMcpServer.csproj` — 1 119 мс против
  72–132 мс у остальных, извлечение — 592 мс против 58–140 мс. Это разогрев процесса (JIT, SDK resolver,
  кеши MSBuild), а не стоимость проекта. Каждая загрузка сервера стартует новый BuildHost и платит её снова.
- Повторов нет: каждый путь оценён один раз, каждая сборка (путь, TFM) — одна, запросов output path нет.
- Граф: 5 узлов, 6 рёбер-элементов (одно с `ReferenceOutputAssembly=false`), 3 загрузки во время Resolve.
- Вложенные сборки в binlog (`spikes/LoadTimeline`): `GetTargetFrameworks` ×6, `GetTargetPath` ×6,
  `InitializeSourceRootMappedPaths` ×4 — вызовы целей протокола ProjectReference, полных сборок нет.

Это маленькое решение на медленной машине: доли не переносятся на Roslyn.slnx и офис.

## Прогоны у владельца

Windows, Roslyn.slnx (клон `roslyn-deep`, тот же commit, что в task-00):

```powershell
pwsh spikes\BuildHostFork\Invoke-BuildHostForkBaseline.ps1 -Solution <путь>\Roslyn.slnx -Name CSharpCompilationOptions -Trace
pwsh spikes\BuildHostFork\Test-BuildHostTrace.ps1
```

Офис — тот же baseline с `-Trace` на офисном решении. В Git переносятся `summary.md`, `trace-summary.md`
и `trace\fork-round<N>\report.md`; для решения вне репозитория они уже с заглушками вместо имён.
`client.json`, `buildhost-*.jsonl`, `graph.json` офисного решения и `names-private.json` содержат пути или
структуру офисного решения и остаются у владельца. Для вложенных сборок офиса — `analyze` из
`spikes/LoadTimeline` на сохранённых binlog task-00a: таблица «nested builds by requested targets»
отвечает, из чего состоят 2 882 запуска проектов.

## Пределы

- Ссылки внутри design-time build (задача MSBuild) трасса не видит; граф — свидетель этого прогона, не
  полный граф проекта. Полноту графа по одному решению не объявлять.
- Host .NET Framework выполняет запросы в отдельном AppDomain, трассировка там не инициализируется: в офисе
  проекты старого формата идут через пакетный net472-host (R2-20), их время видно только как работа клиента
  и как ожидание без работы host.
- Время записи строки трассы дочернего span попадает в собственное время родителя.
- Старт процесса host — оценка по wall clock; остальные времена host — spans `Stopwatch`.
- Цена трассировки на Linux не отделена от разброса.

## Проверки

- Сборка форка Release: 0 ошибок, 0 предупреждений.
- `Test-BuildHostTrace.ps1`: 14 из 14 (Linux).
- Baseline с `-Trace` на `RoslynMcpServer.sln`: identical, equivalent.
- Отказные сценарии task-00 с изменённым форком (трассировка выключена), Linux: **all checks passed**; ранняя
  отмена — по одному host у обеих сторон (upstream, task-00b).
- Release-сборка решения — 0 ошибок; main-набор Linux-шарда — **1 390 passed, 0 failed, 0 skipped**.
  Production-код не менялся: форк и spikes в сборку сервера не входят, `AnalyzerLifecycle` не затронут.
