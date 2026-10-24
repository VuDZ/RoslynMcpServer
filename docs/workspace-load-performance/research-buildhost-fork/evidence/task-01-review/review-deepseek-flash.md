# Независимое ревью: BuildHost/task-01 — тайминги запросов и фактический load graph

- Модель: **deepseek-flash**
- Дата: **2026-10-24**
- Проверенный commit: **`a58486a`** (HEAD ветки `research/workspace-load-performance`; этим коммитом добавлен сам запрос).
  Код задачи — `ac92063` (трассировка и spike) и `a35f682` (прогоны владельца); запрос написан при `a35f682`.
- Стенд ревью: Windows 11 10.0.26200, PowerShell 7.6.8, .NET SDK 10.0.402. Что именно запускал — в разделе 3.

## 1. Вердикт

**Блокеров нет. Записанные результаты task-01 можно принять как evidence, но задачу нельзя закрывать как «инструмент
готов» без исправления M-1.** Остальное — minor/nit; часть из них нужно поправить до task-02, часть — это правки
отчёта и индексов.

Почему результаты можно принимать:

- сам клиент 5.9.0 не форкается; `IProgress<ProjectLoadProgress>.Report` действительно вызывается синхронно сразу
  после остановки собственного таймера операции, а `ElapsedTime` — это время `doFunc`, поэтому окно операции
  восстанавливается корректно (проверено по исходникам `dotnet/roslyn@35d9211b`, см. раздел 3);
- клиент последователен: `Worker.LoadAsync` идёт по проектам `foreach` + `await`, `ResolveReferencesAsync` — тоже;
  в каждый момент выполняется не более одного запроса, поэтому разбивка окна на непересекающиеся интервалы —
  законный critical-path профиль, а не сумма inclusive-времён;
- трассировка выключена — `BuildHostTrace.Start` возвращает `null`, протокол и построение `Response` не меняются;
- фикстура пройдена независимо: 14/14, сборка форка и spike — 0 предупреждений/0 ошибок (Windows, SDK 10.0.402);
- граф в отчёте назван свидетелем одного прогона, а не полным графом; это соответствует контракту task-01;
- production-код не менялся: `git diff --name-only ac92063~1 a35f682` даёт только `docs/`, `forks/`, `spikes/`,
  а `RoslynMcpServer.csproj` исключает оба каталога из сборки сервера.

Что мешает приёмке в полном объёме:

- **M-1 (major):** ошибка открытия/записи трассы не изолирована. При включённой переменной отказ `Initialize`
  роняет host до создания pipe, а отказ `WriteLine` во время span может превратить успешный RPC в ответ с
  исключением или уронить задачу запроса после уже отправленного ответа. Это ровно то свойство, которое проверяет
  пункт 1 запроса («ошибка записи трассы … не ломает host и не меняет исход запроса»), и оно не выполнено.
  Записанные успешные прогоны от этого не пострадали, но инструмент для task-02 в таком виде небезопасен.

## 2. Находки

Порядок — по убыванию серьёзности. `file:line` даны по проверенному commit; для upstream — ссылка на
`dotnet/roslyn@35d9211b841e7613c1d2f8f5af6d628ace696c4c`.

### M-1 (major) — ошибка трассы меняет поведение host и исход RPC

**Где.** [BuildHostTrace.cs:38-52](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/Tracing/BuildHostTrace.cs#L38-L52)
(`Initialize`; `Directory.CreateDirectory(directory);` — строка 45, `new StreamWriter(...)` — 46–50, всё без
`try/catch`), [BuildHostTrace.cs:79-93](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/Tracing/BuildHostTrace.cs#L79-L93)
(`WriteLine`; `writer.WriteLine(line);` — строка 92, без `try/catch`),
[RpcServer.cs:159-187](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/Rpc/RpcServer.cs#L159-L187)
(`rpc-invoke`/`rpc-serialize` внутри `try`, `catch` на 175–181, `send` на 185),
[RpcServer.cs:193-197](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/Rpc/RpcServer.cs#L193-L197)
(`WriteLineAsync`/`FlushAsync`),
[ProjectBuildManager.cs:112,145,204,227,248-253,259-271,288,294,325](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/Build/ProjectBuildManager.cs#L112),
[ProjectFile.cs:50-57](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/MSBuild/ProjectFile/ProjectFile.cs#L50-L57),
[Program.cs:20,43](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/Program.cs#L20).

**Что не так.** Трассировка не является fail-safe:

- `Initialize` зовёт `Directory.CreateDirectory` и создаёт `StreamWriter` без обработки ошибок; вызов стоит в
  `Program.Main` **до** проверки аргументов и до `NamedPipeUtil.CreateServer`. Если `ROSLYN_BUILDHOST_TRACE_DIRECTORY`
  указывает на существующий файл, недоступный/read-only каталог, диск заполнен или путь невалиден, host падает при
  старте. Клиентский `BuildHostProcessManager` в этом случае не получает pipe и загрузка завершается ошибкой; это
  уже не «трасса не записалась», а изменение исхода загрузки.
- `WriteLine` (вызывается из `Span.Dispose`) не ловит `IOException`/`ObjectDisposedException`/ошибку кодирования.
  Внутренние span (`load-project`, `evaluate`, `reevaluate`, `build-instance`, `msbuild-build`, `extract`,
  `rpc-invoke`, `rpc-serialize`) закрываются **внутри** `try` метода `ProcessRequestAsync`; исключение из
  `Dispose` попадает в `catch (Exception e)` на 175–181 и превращает **успешно выполненный** метод в
  `Response.ExceptionMessage`. Клиент видит workspace failure/ошибку проекта, которого на самом деле нет.
- `send` и внешний `rpc` span закрываются **после** `try`, уже после записи ответа. Их `Dispose` при ошибке
  трассы роняет задачу запроса; `RunAsync` затем получает исключение в `Task.WhenAll(remainingTasks)`
  ([RpcServer.cs:111](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/Rpc/RpcServer.cs#L111)),
  и host завершается аварийно. Если же `Dispose` бросает во время разворачивания стека (например, `build-instance`
  закрывается из-за ошибки сборки), исключение трассы **подменяет исходное** — меняется текст диагностики.
- `Span.Dispose` восстанавливает `s_current` до записи строки, поэтому вложенность при этом не ломается — это
  хорошо; проблема только в том, что исключение уходит наружу.

**Сценарий.** Диск, куда пишется `buildhost-<pid>.jsonl`, заполняется на 30-й секунде загрузки. Следующий
`reevaluate`/`msbuild-build` закрывается, `WriteLine` бросает `IOException`, `ProcessRequestAsync` ловит его и
возвращает клиенту ошибку метода, хотя MSBuild-вызов завершился. Или: каталог трассы создан на read-only сетевой
шаре — host не стартует, и вся загрузка падает. Или: файл трассы открыт эксклюзивно другим процессом — падение
`Initialize`. `-Trace` позиционируется как «сравнение проверяет, что трассировка ничего не меняет», а при отказе
I/O она меняет и ответ, и жизнь процесса.

**Доказательство.** Цитаты выше; в отчёте «Проверки» ([task-01-graph-and-timings.md:376-388](../task-01-graph-and-timings.md#L376-L388))
нет ни одного отказа трассы, а `Test-BuildHostTrace.ps1` всегда создаёт новый writable-каталог. То есть свойство
не проверено ни тестом, ни прогоном; по коду оно не выполняется.

**Предлагаемое исправление.** Сделать трассировку best-effort и полностью отделить её ошибки от RPC:

1. `Initialize`: обернуть создание каталога/писателя в `try/catch`; при ошибке записать одну строку в stderr
   (если есть) и оставить `s_writer = null` — host работает без трассы.
2. `WriteLine` и `Span.Dispose`: никогда не бросать наружу. При первой ошибке выставить флаг «trace failed»
   (и, при желании, `s_writer = null`, чтобы не пытаться снова); `Start` после этого возвращает `null`; уже
   начатые span’ы просто не пишут. Ошибку трассы можно записать в stderr один раз.
3. Добавить в `Test-BuildHostTrace.ps1` отказной сценарий: каталог трассы указывает на файл/read-only каталог
   (и, если возможно, имитацию `IOException` при записи) и проверить, что ответ `observe`/workspace не изменился,
   а host не упал. Это тот тест, которого сейчас нет.

**Уверенность:** высокая (по коду); вживую отказ не воспроизводил — см. раздел 4.

### m-1 (minor) — в офисе net472-host — это исходный пакетный бинарь, а не форк; объяснение в «Пределах» относится не к этому прогону

**Где.** [BuildHostForkCommon.ps1:19-24](../../../../../spikes/BuildHostFork/BuildHostForkCommon.ps1#L19-L24)
(заменяется только `BuildHost-netcore`); [task-01-graph-and-timings.md:365-367](../task-01-graph-and-timings.md#L365-L367);
[BuildHostTrace.cs:21-25](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/Tracing/BuildHostTrace.cs#L21-L25);
[NetFrameworkBuildHost.cs:31-54](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/NetFrameworkBuildHost.cs#L31-L54);
[Program.cs:43](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/Program.cs#L43).

**Что не так.** `Initialize-ForkCopy` копирует выход spike’а и заменяет **только** `BuildHost-netcore`; каталог
`BuildHost-net472` остаётся из пакета 5.9.0. Проверка версий это подтверждает:

```text
bin-fork\BuildHost-net472\...BuildHost.exe  -> 5.9.0-1.26357.3+35d9211b...
bin-fork\BuildHost-netcore\...BuildHost.dll -> 5.9.0-fork+35d9211b...
```

Поэтому в офисе 5 проектов старого формата обслуживал **неинструментированный исходный** host, и «трассировка там
не инициализируется» — не причина, а следствие того, что это не форк. Кроме того, даже собранный форк для net472
не дал бы «только process-level spans»: `NetFrameworkBuildHost.Create` создаёт `RpcServer` в **default AppDomain**
и передаёт его в дочерний AppDomain, поэтому `rpc`, `rpc-invoke`, `rpc-serialize`, `rpc-send` писались бы, а
`load-project`/`evaluate`/`build` — нет (они в дочернем AppDomain, где статики `BuildHostTrace` не инициализированы).
Формулировка XML-doc «only the process-level spans appear» как минимум неполна: RPC-span’ы — это тоже
process-level (default AppDomain), и они были бы записаны; отсутствовали бы только span’ы дочернего AppDomain.
Для офисного прогона важнее другое: там **вообще** не форк, поэтому «трассировка там не инициализируется» — не
причина, а следствие.

**Сценарий.** В task-02 старые проекты офиса (5 из 142, 5,0 с) снова окажутся без трассы; если решат собрать
форк для net472, трасса будет частичной (RPC есть, evaluation/build нет), и `WallBreakdown`/`ClockCheck` увидят
host-запросы без соответствующих span — как в M-1/m-11, но уже на валидных данных.

**Доказательство.** Команда выше; `Initialize-ForkCopy`; код `NetFrameworkBuildHost.Create`:
`new RpcServer(pipeStream, factory.CreateMethodInvoker())` — default AppDomain; `ProjectBuildManager`/`ProjectFile`
создаются в `Factory`/`NetFrameworkBuildHost` — дочерний AppDomain.

**Предлагаемое исправление.** В «Пределах» написать прямо: net472-host — исходный пакетный, форк для net472 не
собирался и не проверялся; 5,0 с измерены по `client.json` и остаются валидными, но трассы этих запросов нет.
Поправить XML-doc `BuildHostTrace` (упомянуть, что RPC-часть живёт в default AppDomain), либо собирать и тестировать
форк net472 отдельно, если старые проекты входят в цель task-02.

**Уверенность:** высокая.

### m-2 (minor) — сравнение «на проект» 61 / 44 / 82 смешивает разные знаменатели

**Где.** [task-01-graph-and-timings.md:309-310](../task-01-graph-and-timings.md#L309-L310).

**Что не так.** Офисное решение содержит 142 записи, но host-трасса есть только у 137 netcore-проектов (5 net472
не трассируются). Числа 10 381/137 = 75,8 мс, 10 080/137 = 73,6, 13 740/137 = 100,3 — это «на **трассированный**
проект», а не на все 142 (на все 142 было бы 73,1 / 71,0 / 96,8); это ещё одна неоговорённая база. Для Roslyn.slnx
на офисном компьютере в отчёте стоит «61, 44 и 82 мс»; при этом:

- 19 343/318 = 60,8 мс — **на путь** (318 корневых evaluation), а на 434 проекта — 44,6;
- 19 172/318 = 60,3 мс на путь, а на 434 проекта — 44,2;
- 35 493/434 = 81,8 мс — на сборку/проект.

То есть 61 — на путь, 44 — на проект, 82 — на сборку. Одного знаменателя нет. Корректное сравнение «на проект»:
**45 / 44 / 82**; корректное «на путь»: **61 / 60 / 82** (но тогда build надо считать на путь: 111,6).

**Сценарий.** Читатель сравнивает 76/74/100 офисного решения с 61/44/82 и делает вывод, что корневая evaluation
на Roslyn дешевле всего на 25 %, а извлечение — на 68 %; на самом деле в пересчёте на проект evaluation дешевле
на 68 % (45 против 76), как и извлечение. Это меняет интерпретацию «на проект» в отчёте (основной вывод
«evaluation/извлечение медленнее сборки» получен из отношений итогов к основному компьютеру, 3,0–3,3 против 1,9,
и от этой опечатки не зависит).

**Доказательство.** Числа из [trace-summary.md](../task-01-roslyn-office-pc/trace-summary.md#L4-L15):
19 343 / 19 172 / 35 493; 318 путей / 434 проекта; офисное решение — 10 381 / 10 080 / 13 740 на 137.

**Предлагаемое исправление.** Заменить «61, 44 и 82» на «45, 44 и 82 мс на проект» (и при желании рядом —
«61 и 60 мс на путь»), либо явно указать знаменатель для каждого числа.

**Уверенность:** высокая.

### m-3 (minor) — родительские README говорят «Roslyn.slnx и офис pending», хотя прогоны уже есть

**Где.** [docs/workspace-load-performance/README.md:24](../../../README.md#L24) и
[docs/README.md:18](../../../../README.md#L18): «task-01 — граф/тайминги, in progress (…; Roslyn.slnx и офис pending)»
и «инструмент проверен на Linux, Roslyn.slnx и офис pending». При этом
[task-01-buildhost-graph-and-timings-hi.md:4-13](../../task-01-buildhost-graph-and-timings-hi.md#L4-L13) и
[отчёт:3-6](../task-01-graph-and-timings.md#L3-L6) фиксируют Windows-прогоны, Roslyn.slnx на двух компьютерах и
офисное решение; pending — только ревью и приёмка. Статус task-01 в [research README:32](../../README.md#L32)
тоже говорит «Pending: независимое ревью, приёмка».

**Что не так.** Нарушена согласованность статусов, которую требует AGENTS.md («Keep task, index, report, and packet
execution-status summaries consistent»). Канонический индекс темы — первое место, куда смотрит читатель; он
сообщает, что ключевых прогонов нет. Это не техническая ошибка, но это именно то, что проверяет пункт 7 запроса.

**Сценарий.** Ревьюер/владелец по `docs/README.md` решает, что Windows/офисные прогоны ещё не сделаны, и
перезапускает их или откладывает приёмку; в отчёте task-02 ссылаются на «pending» вместо результатов.

**Предлагаемое исправление.** Заменить «Roslyn.slnx и офис pending» на «прогоны выполнены, pending ревью/приёмка»
в обоих README; ссылку на отчёт сохранить. Статус «in progress» при этом остаётся правильным до приёмки.

**Уверенность:** высокая.

### m-4 (minor) — ClockCheck считает операции, а не запросы; unmatched не отличает пропуск запроса от net472

**Где.** [ClockCheck.cs:21-43](../../../../../spikes/BuildHostFork/Trace/ClockCheck.cs#L21-L43);
[HostRequests.cs:29](../../../../../spikes/BuildHostFork/Trace/HostRequests.cs#L29);
[TraceReport.cs:34](../../../../../spikes/BuildHostFork/Trace/TraceReport.cs#L34);
[Program.cs:55](../../../../../spikes/BuildHostFork/Program.cs#L55);
[trace-summary.md:5](../task-01-office/trace-summary.md#L5).

**Что не так.** `Run` идёт по `client.Operations` (Evaluate/Build) и для каждой ищет запрос; `matched + unmatched`
— это число **операций**, а не запросов. Обратной проверки нет: запрос, который существует, но не попал в окно
операции (сдвиг часов, потерянная строка трассы, другой порядок), просто не учитывается; операция при этом
попадает в `unmatched`. Поэтому 40 unmatched в офисе выглядят одинаково и как «5 проектов × 2 операции × 4 раунда
ушли в нетрассируемый net472-host», и как «одна строка запроса потерялась/сдвинулась». Отчёт объясняет 40 через
`client.json` и типы проектов (не-SDK → net472 на Windows), но **инструмент** этого не доказывает и не печатает,
какие операции не совпали. `trace-report` при этом возвращает код 1 при любом unmatched
([Program.cs:55](../../../../../spikes/BuildHostFork/Program.cs#L55)), то есть валидная офисная трасса формально
«не проходит» проверку.

**Сценарий.** Одна строка `rpc` для netcore-запроса потеряна (например, host убит в конце загрузки, m-11); его
Evaluate становится unmatched, счётчик 41 вместо 40; отчёт либо заметит расхождение, либо (если проект попал в
другой раунд) отнесёт его к net472. При параллельном клиенте `MinBy` может выбрать не тот запрос.

**Доказательство.** Код `ClockCheck.Run`; `HostRequests.Read` (`Sent` = конец `rpc-send`, `Received` = `received`);
в `trace-summary.md` есть только счётчики, без путей/методов. Сырой `client.json` офиса в Git отсутствует.

**Предлагаемое исправление.** Печатать unmatched-операции (путь, метод, окно) и число запросов, не попавших ни
в одну операцию; сравнивать `matched` с числом Evaluate/Build-запросов за вычетом известных нетрассируемых хостов;
добавить флаг `--allow-unmatched` или отдельный код возврата для объяснённого unmatched; в отчёте явно писать,
что 40 — это операции, а не запросы.

**Уверенность:** высокая по коду; влияние на вывод — низкое, потому что 40 = 5 × 2 × 4 и типы проектов
проверяемы независимо.

### m-5 (minor) — узел графа не включает остальные effective global properties; «один путь с разными properties» покрыт только TFM

**Где.** [LoadGraphBuilder.cs:39-57](../../../../../spikes/BuildHostFork/Trace/LoadGraphBuilder.cs#L39-L57);
[GraphNode.cs](../../../../../spikes/BuildHostFork/Trace/Models/GraphNode.cs);
[ProjectBuildManager.cs:72-85](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/Build/ProjectBuildManager.cs#L72-L85);
[task-01-graph-and-timings.md:59-63](../task-01-graph-and-timings.md#L59-L63) и `Пределы` 361–374.

**Что не так.** Идентичность узла — `(путь, TFM)`; `Configuration`/`Platform` берутся из **первого** корневого
`evaluate` этого пути и являются подписями, а не частью идентичности. Остальные effective global properties
(`DesignTimeBuild`, `BuildProjectReferences`, `NonExistentFile`, `SolutionDir` и клиентские `Properties`) в узле
отсутствуют. В 5.9.0 `MSBuildProjectLoader` передаёт **один** словарь `Properties` на загрузку плюс `SolutionDir`;
`ProjectBuildManager` добавляет к нему `s_defaultGlobalProperties`, поэтому в этих прогонах другие свойства не
варьируются, и граф не врёт. Но контракт task-01 требует «как минимум Configuration/Platform/TFM и влияющие global
properties», а пункт «один путь с разными properties» фактически проверен только переключением TFM. Если загрузчик
когда-нибудь начнёт передавать per-project Configuration/Platform (или второй host — другие свойства), два контекста
одного пути сольются в один узел, и `First()` молча выберет один из них. В «Пределах» этого ограничения нет.

**Сценарий.** `MSBuildProjectLoader` пробрасывает `Configuration=Release` для одного проекта и `Debug` для другого
с тем же путём (или два host’а с разными global properties) — граф даст один узел и один label, но два разных
эффективных контекста; task-02, кеширующий по узлу, переиспользует неверный результат.

**Доказательство.** `LoadGraphBuilder.Nodes`: `contexts` — `GroupBy(path).First()`, `built` — `Distinct()` по
`(Path, Tfm)`; `GraphNode` не имеет поля global properties. `ProjectBuildManager` ctor: `allProperties` из
`_globalMSBuildProperties` + `s_defaultGlobalProperties`; `ProjectCollection` один на host.

**Предлагаемое исправление.** Записывать в узел effective global properties (или их хеш/список) и включать их в
ключ; как минимум документировать, что единственное переменное свойство верхнего уровня в 5.9.0 — TFM, а
Configuration/Platform — подписи. Для проверки «один путь с разными properties» добавить в фикстуру случай, где
свойство меняется на уровне загрузки (или явно записать, что SetTargetFramework виден только в binlog).

**Уверенность:** высокая по коду; практическое влияние на эти прогоны — низкое.

### m-6 (minor) — «повторов нет» — это повторы, видимые трассой; вложенные MSBuild-сборки в неё не попадают

**Где.** [ProjectCosts.cs:17-35](../../../../../spikes/BuildHostFork/Trace/ProjectCosts.cs#L17-L35);
[ProjectFile.cs:39-60](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/MSBuild/ProjectFile/ProjectFile.cs#L39-L60);
[task-01-graph-and-timings.md:195-196](../task-01-graph-and-timings.md#L195-L196) (Roslyn.slnx),
[311-315](../task-01-graph-and-timings.md#L311-L315) (офис), `Пределы` 361–364 и 372.

**Что не так.** `RepeatInventory` считает только span’ы `evaluate`, `build-instance`, `output-path` и
`load-project (cached)`. Вложенные сборки, которые MSBuild запускает задачей `MSBuild` внутри design-time build,
не являются span’ами `BuildHostTrace`; поэтому повторная полная сборка, запущенная целью проекта, в inventory не
попадёт. Для **офисного** решения это частично закрыто анализом binlog task-00a: 2 740 вложенных запусков — только
`GetTargetPath`/`GetTargetFrameworks`, полных нет. Для **Roslyn.slnx** binlog-анализа в task-01 нет; отчёт сам
пишет (строка 372), что порядок загрузки и состав сборки не установлены. Значит «Повторов нет … кешировать
повторную работу в Roslyn.slnx нечего» (195–196) сильнее данных: это «нет повторов, видимых трассой». Ограничение
«ссылки внутри design-time build трасса не видит» в «Пределах» есть, но оно относится к графу, а не к выводу о
повторах.

**Сценарий.** Цель проекта (или SDK) внутри design-time build запускает `MSBuild` на два проекта дважды с
default-целями — трасса покажет один `msbuild-build` верхнего уровня и ноль повторов; task-02 решит, что кешировать
нечего, и пропустит реальный повтор. Офисный прогон этого не показывает, но Roslyn.slnx не проверен.

**Доказательство.** `RepeatInventory` kinds; отсутствие span’ов на nested MSBuild; отчёт 363–364, 372.

**Предлагаемое исправление.** Квалифицировать вывод: «повторов, видимых трассой, нет»; для Roslyn.slnx либо
добавить анализ binlog по целям (как для офиса), либо записать в «Пределы» явно, что repeat inventory не видит
nested MSBuild и это не проверено.

**Уверенность:** высокая по ограничению; сам вывод «полных повторов нет» для офиса подтверждён.

### m-7 (minor) — фикстура не проверяет часть того, что заявляет, и не покрывает отказы

**Где.** [Test-BuildHostTrace.ps1:86-103](../../../../../spikes/BuildHostFork/Test-BuildHostTrace.ps1#L86-L103).

**Что не так.** По каждой проверке:

- **строка 89** «Lib is re-evaluated for each target framework and once to restore» проверяет только `Count -eq 3`.
  Три `reevaluate` могут быть тремя переключениями без возврата; атрибут `restoresTargetFramework` (на котором
  держится вывод «возврат TFM 3,5 %») не проверяется. В сырой трассе фикстуры третий span действительно имеет
  `restoresTargetFramework:true` — но тест это не ловит.
- **строки 99–101** «Native.proj gets one output path query from App and one from Second» проверяют только
  `OutputPathQueries -eq 2`, `Cached -eq 1` и ребро **Second → Native**. Ребро **App → Native** не проверяется.
  Если обе операции припишутся Second, `OutputPathQueries` останется 2, `Distinct()` ребра даст 1, проверка 12
  пройдёт — а заявление «от App и от Second» будет ложным.
- **строка 86** `Clock.Matched -gt 0` — хорошо; но нет проверки, что трасса **не создаётся** при выключенной
  переменной (заявление офф-пути), и нет отказного сценария на ошибку открытия/записи трассы (M-1).
- Нет проверки `CachedProjectLoads == 1`, `PathsEvaluatedMoreThanOnce == 0`, `RepeatedBuildsOfSamePathAndTfm == 0`
  — то есть счётчики повторов и кеша, на которых стоит вывод «повторов нет», могут быть сломаны и тест этого не
  заметит.
- Нет записи решения, которую workspace не может загрузить (`.vcxproj`/`.shproj`): `SolutionEntriesNotLoaded == 0`,
  ветка «узел без сборки из solution entry» и `SolutionEntryKind` не покрыты. `Native.proj` — это ссылка, а не
  запись решения.

**Сценарий.** Регрессия в `BuildHostTrace`/`ProjectBuildManager`, которая помечает возврат TFM как обычное
переключение, проходит фикстуру; регрессия в `LoadGraphBuilder`/`ResolveEdges`, которая приписывает оба output-path
запроса Second, проходит; регрессия, которая создаёт файл трассы при выключенной переменной, проходит; ошибка
`Initialize`/`WriteLine` вообще не тестируется.

**Доказательство.** Проверки и их сообщения в `Test-BuildHostTrace.ps1`; `Repeats` в `report.json` фикстуры —
1 cached, 2 output path, 0/0 повторов, но ни одно из этих значений не сравнивается; `SolutionEntriesNotLoaded`
в фикстуре 0 (в solution только три загружаемых проекта).

**Предлагаемое исправление.** Добавить:

- `@(reevaluate Lib | Where restoresTargetFramework).Count -eq 1` и `targetFramework` отсутствует у возврата;
- ребро `App → Native` с provenance `output path query during Resolve`;
- `Repeats.CachedProjectLoads -eq 1`, `PathsEvaluatedMoreThanOnce -eq 0`, `RepeatedBuildsOfSamePathAndTfm -eq 0`;
- отдельный `observe` без `ROSLYN_BUILDHOST_TRACE_DIRECTORY`, проверить отсутствие `buildhost-*.jsonl`;
- запись `.vcxproj` (или заведомо неподдерживаемый проект) в solution и проверку `SolutionEntriesNotLoaded`;
- отказной сценарий трассы (M-1).

**Уверенность:** высокая.

### m-8 (minor) — «1 435 GetTargetPath = 1 435 рёбер, один вызов на ссылку» — совпадение двух прогонов, а не доказательство 1:1

**Где.** [task-01-graph-and-timings.md:330-334](../task-01-graph-and-timings.md#L330-L334) и `Пределы` 373–374;
[Extensions.cs:43-46](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/MSBuild/ProjectFile/Extensions.cs#L43-L46);
[ProjectInstanceReader.cs:138](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/MSBuild/ProjectFile/ProjectInstanceReader.cs#L138);
[TimelineReport.cs:118-137](../../../../../spikes/LoadTimeline/TimelineReport.cs#L118-L137).

**Что не так.** Рёбра-элементы трассы — это **все** `ProjectReference` items `ProjectInstance` после design-time
build’а (с `ReferenceOutputAssembly`); трасса не записывает `BuildReference`/`SkipGetTargetFrameworkProperties`
и другие метаданные, которые могли бы исключить item из reference build. Число 1 435 в binlog — это фактические
вызовы `GetTargetPath` в **другом прогоне** (task-00a, сервер 1.5.14, другая ОС), для 137 верхнеуровневых сборок.
Совпадение итогов само по себе не доказывает, что каждому ребру соответствует ровно один вызов: item, исключённый
из reference build (`BuildReference=false`) или пропускающий `GetTargetFrameworks`
(`SkipGetTargetFrameworkProperties`), мог бы дать ребро без соответствующего вызова; дубликат item’а/вызова мог бы
сместить счётчик; `GetTargetFrameworks` на 174 вызова меньше — уже пример того, что протокол не единообразен.
Отчёт честно пишет «сверка двух прогонов одного решения» (373–374), но фраза «столько же … один вызов на ссылку»
(333) звучит как доказанный 1:1. Счётчик нужен для вывода «2 740 — только протокол»; этот вывод от 1:1 не зависит.

**Сценарий.** В другом решении item с `BuildReference=false` (или два item’а на один путь) даст расхождение
1 435 ≠ 1 435; если читатель уже принял «один вызов на ссылку», он неверно интерпретирует разницу как потерю
рёбер/вызовов.

**Предлагаемое исправление.** Писать «итоги совпали (1 435 = 1 435) в двух прогонах»; для 1:1 либо записывать
`BuildReference`/`SkipGetTargetFrameworkProperties` в trace и сравнивать множества, либо оставить как
корреляцию; разобрать 174 (`SetTargetFramework`/`SkipGetTargetFrameworkProperties`/не-SDK references) или явно
не приписывать этому значению смысл.

**Уверенность:** высокая.

### m-9 (minor) — цена трассировки не отделена: сравнивается fork с трассой против исходного без трассы, а MEF/workspace общие для обеих сторон

**Где.** [Invoke-BuildHostForkBaseline.ps1:80-96](../../../../../spikes/BuildHostFork/Invoke-BuildHostForkBaseline.ps1#L80-L96);
[BuildHostForkCommon.ps1:43-45](../../../../../spikes/BuildHostFork/BuildHostForkCommon.ps1#L43-L45);
[Program.cs:84-90](../../../../../spikes/BuildHostFork/Program.cs#L84-L90);
[SolutionLoader.cs:54-60](../../../../../spikes/ParallelSearch/SolutionLoader.cs#L54-L60);
[task-01-graph-and-timings.md:370-371](../task-01-graph-and-timings.md#L370-L371).

**Что не так.** `-Trace` включает трассу только на стороне fork; исходная сторона остаётся без трассы. Отношение
`fork(traced) / original(untraced)` включает: (а) собственную разницу форка (в task-00 на Roslyn.slnx ×0,992),
(б) шум раундов, (в) цену трассы. Кроме того, `loadMs` в `observe` — это внешний `watch` вокруг
`SolutionLoader.LoadAsync`, то есть **включая** MEF и создание workspace (на офисе ~0,6 с, на Windows ~0,35 с);
`OpenSolutionAsync`-окно трассы эти ~0,6 с не включает. Эта часть общая для обеих сторон и сама по себе отношение
почти не искажает (она лишь немного уменьшает его), но она показывает, что сравниваются разные метрики: «цена» в
отчёте — это цена всей загрузки `SolutionLoader`, а разбивка ниже — только окна. Главные искажения отношения —
собственная разница форка (в task-00 без трассы на Roslyn.slnx было ×0,992) и разброс раундов; поэтому `×1,011`
нельзя читать как изолированную цену трассы. Отчёт пишет «не отделена от разброса» (370–371) — это честно, но
контракт task-01 требовал «измерен overhead логирования»; здесь получена верхняя оценка отношения загрузок, а не
изолированная цена.

**Сценарий.** Для решения о task-02 важно, платит ли трасса 1 % или 2–3 % при 43 % design-time build; текущее
отношение не позволяет это утверждать. При переносе на другую машину/диск разница может быть больше, а
«×1,000–1,04» будет читаться как цена трассы, хотя часть — шум и собственная разница форка.

**Доказательство.** `Invoke-ObserveRounds`: env ставится только для fork; `observe` `WallMs` = внешний `watch`;
`SolutionLoader` `window` = только `OpenSolutionAsync`; отчёт 370–371.

**Предлагаемое исправление.** Добавить контрольный набор: тот же fork **без** трассы в том же сеансе, чередуя
раунды; сравнить fork-traced / fork-untraced; при желании — по `OpenSolutionAsync`-окну, без MEF/workspace.
Тогда цена трассы будет изолирована, а не смешана с форком и разбросом.

**Уверенность:** высокая по методу; влияние на вывод — среднее (цена трассы мала по сравнению с 43 %).

### m-10 (minor) — каталог трассы не очищается: повторный `-OutDirectory` смешивает прогоны

**Где.** [Invoke-BuildHostForkBaseline.ps1:80-89](../../../../../spikes/BuildHostFork/Invoke-BuildHostForkBaseline.ps1#L80-L89)
(`New-Item` без очистки, `$traceRoot`); [BuildHostForkCommon.ps1:43-45](../../../../../spikes/BuildHostFork/BuildHostForkCommon.ps1#L43-L45);
[BuildHostTrace.cs:45-46](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/Tracing/BuildHostTrace.cs#L45-L46);
[ClientTraceFile.cs:49-50](../../../../../spikes/BuildHostFork/Trace/ClientTraceFile.cs#L49-L50);
[HostTraceReader.cs:9-13](../../../../../spikes/BuildHostFork/Trace/HostTraceReader.cs#L9-L13).

**Что не так.** Если передан существующий `-OutDirectory`, `trace/fork-round<N>` уже может содержать
`buildhost-<oldpid>.jsonl` (и `client.json`) от предыдущего запуска. Новый host создаёт `buildhost-<newpid>.jsonl`
с `append:false`; старый файл остаётся. `HostTraceReader.ReadAll` читает **все** `buildhost-*.jsonl`;
`TraceReport` увидит 2 host’а, сложит их span’ы и прогонит `ClockCheck` по старым запросам, которые могут
совпасть с новыми операциями по пути/методу. `client.json` перезаписывается, но если fork-запуск упал до записи
`client.json`, останется старый, и `trace-summary` молча посчитает его. Скрипт не проверяет «в раунде ровно один
host-файл».

**Сценарий.** Владелец перезапускает `-Trace` в тот же `results/baseline-…`, чтобы повторить сравнение; в
`trace-summary` «2 traced host(s)», неверные matched/unmatched и доли; график/отчёт выглядят валидными. Либо
упавший fork-раунд оставляет старый `client.json`, и `trace-summary` смешивает старый client с новыми host-файлами.

**Доказательство.** Код; `BuildHostTrace.Initialize` `append: false`; `ClientTraceFile.Write` `File.WriteAllText`;
`HostTraceReader.ReadAll` `Directory.GetFiles(...).Select(Read)`.

**Предлагаемое исправление.** Удалять/требовать пустой `$traceRoot` (или `trace/fork-round*`) перед раундами;
в `TraceReport.Run` проверять `hosts.Count == 1` (или ожидаемое число) и наличие `client.json` этого запуска
(например, метка run id); в `trace-summary` требовать одинаковое число host’ов в раундах.

**Уверенность:** высокая.

### m-11 (minor) — незакрытые/потерянные span’ы не обнаруживаются; разбивка всё равно складывается в окно

**Где.** [HostTraceReader.cs:16-52](../../../../../spikes/BuildHostFork/Trace/HostTraceReader.cs#L16-L52)
(битая последняя строка пропускается);
[WallBreakdown.cs:110-131](../../../../../spikes/BuildHostFork/Trace/WallBreakdown.cs#L110-L131)
(`children = host.Spans.ToLookup(span => span.Parent)`, без проверки, что родитель существует);
`TraceReport`/`TraceRounds` не проверяют полноту; `Пределы` 361–374.

**Что не так.** Host, убитый в середине запроса (ранняя отмена task-00, `kill-host`), успевает записать строки
дочерних span’ов, но не строку родителя: `Span.Dispose` ребёнка пишет `parent = <id>`, строки с этим `id` нет.
`HostTraceReader` просто пропускает битую последнюю строку; `LoadGraphBuilder`/`ProjectCosts`/`WallBreakdown`
используют `Parent` как ссылку, не проверяя её существования. В результате ребёнок становится «корнем» с
собственным self-time, а время отсутствующего родителя (включая его self time) не относится ни к одной категории;
`sweep` всё равно разбивает окно на отрезки и суммы сходятся, поэтому отчёт выглядит полным. «Пределы» этого не
называют, хотя пункт 3 запроса прямо спрашивает про span без закрытия.

**Сценарий.** `-Trace` с ранней отменой/падением host’а: клиентская загрузка завершилась, `client.json` есть,
host-файл обрезан; `trace-report` выдаёт разбивку и граф без предупреждения, host-работа занижена на время
родительских span’ов. Если такой прогон попадёт в `trace-summary`, медианы категорий сместятся.

**Доказательство.** `Span.Dispose` пишет строку после `end`; при убийстве процесса строка может не успеть;
`HostTraceReader` `catch (JsonException) continue`; `WallBreakdown` не проверяет `byId.ContainsKey(parent)`.

**Предлагаемое исправление.** При чтении считать span’ы, чей `parent != 0` отсутствует в файле, и span’ы без
парной строки (по возможности); если есть — помечать трассу `incomplete` и не включать её в `trace-summary`/
не выдавать «полную» разбивку; в `trace-report` печатать предупреждение. Добавить тест с обрезанным файлом.

**Уверенность:** высокая.

### m-12 (minor) — redaction привязан к пути решения, а не к каждому пути; in-repo solution с внешним проектом утечёт полный путь

**Где.** [Invoke-BuildHostForkBaseline.ps1:92-95](../../../../../spikes/BuildHostFork/Invoke-BuildHostForkBaseline.ps1#L92-L95);
[TraceReport.cs:42](../../../../../spikes/BuildHostFork/Trace/TraceReport.cs#L42);
[PathNames.cs:18-22](../../../../../spikes/BuildHostFork/Trace/PathNames.cs#L18-L22); `Named` (65–68).

**Что не так.** `$external = -not $Solution.StartsWith($repo + …)`: `redact` включается, только если само решение
лежит вне репозитория. `PathNames.Name` при `redact=false` возвращает `Path.GetRelativePath(root, fullPath)`, а
если путь **выше** каталога решения (начинается с `..`), возвращает **полный** путь. Для решения внутри репозитория
с `ProjectReference` на приватный проект вне каталога решения `report.md` (таблица top-15 и `graph.json`) будет
содержать полный внешний путь. В текущем Git-наборе этого нет (офисное решение и Roslyn.slnx — внешние, поэтому
redact; фикстура без внешних проектов), но инструмент к этому не защищён.

**Сценарий.** `-Trace` на решении внутри репозитория, один проект ссылается на `D:\private\…`; `report.md`
попадает в Git с полным приватным путём — нарушение правила «пути редактировать перед переносом».

**Доказательство.** `PathNames.Name`; `TraceReport.Run` передаёт `redact` из `-Trace`; `Invoke-BuildHostForkBaseline`
считает `$external` только по решению.

**Предлагаемое исправление.** Считать пустым/красным любой путь вне разрешённого корня (репозиторий или каталог
решения), а не только решение; либо добавить `-Redact` и включать его по умолчанию для `report.md`/`graph.json`,
когда есть хотя бы один внешний проект.

**Уверенность:** высокая; текущие опубликованные файлы чистые.

### n-1 (nit) — при выключенной трассировке всё равно читаются `Timestamp`/`line.Length` и растёт замыкание

**Где.** [RpcServer.cs:75-76,89](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/Rpc/RpcServer.cs#L75-L76).

**Что не так.** `receivedTimestamp` и `requestLength` вычисляются на каждом запросе всегда, даже когда
`s_writer == null`; `Task.Run(() => ProcessRequestAsync(request, receivedTimestamp, requestLength))` захватывает
на две переменные больше, чем раньше. Это не создаёт объектов трассы (Span’ы не аллоцируются), `Stopwatch.GetTimestamp`
дёшев, и на поведение это не влияет; но формулировка README/отчёта «без переменной не создаёт объектов и ничего не
пишет» буквально неверна: работа (и чуть больший closure) есть. Уровень — nit.

**Сценарий.** Никакого измеримого; важно только для точности формулировки.

**Предлагаемое исправление.** Либо перенести захват в ветку `if (BuildHostTrace.Enabled)`, либо написать «не создаёт
Span-объектов и не пишет файл». Если оставить — указать в отчёте, что два значения читаются всегда.

**Уверенность:** высокая; влияние пренебрежимо.

### n-2 (nit) — в статусе задачи и README «разогрев первого проекта ~2,2 с» без оговорки, что он не отделён от стоимости проекта

**Где.** [task-01-buildhost-graph-and-timings-hi.md:11](../../task-01-buildhost-graph-and-timings-hi.md#L11);
[research README:32](../../README.md#L32); [отчёт:305-310](../task-01-graph-and-timings.md#L305-L310).

**Что не так.** Отчёт говорит: «вместе — около 2,2 с (~5 %); отделить разогрев от стоимости самого проекта по
этим прогонам нельзя» (307–308). Статус и README пересказывают это как «разогрев первого проекта ~2,2 с» без
оговорки; кроме того, 531–536 мс + 1 759–1 964 мс = 2,29–2,50 с, то есть «~2,2» — нижняя граница. Читатель может
вычесть 2,2 с как измеренный warmup.

**Сценарий.** В task-02 бюджет загрузки уменьшают на «warmup 2,2 с», хотя это верхняя оценка первого проекта,
включающая его собственную стоимость.

**Предлагаемое исправление.** В статусе/README: «первый проект ~2,3 с (включая старт/JIT; отделить разогрев от
стоимости проекта нельзя)»; в отчёте оставить как есть.

**Уверенность:** высокая; это редакционная правка.

### n-3 (nit) — мелкие расхождения в представлении: Linux-медианы, `measured[0]`, неполный `HostMs`, заглушки по раундам

**Где.** [task-01-graph-and-timings.md:101-121](../task-01-graph-and-timings.md#L101-L121) (Linux-таблица);
[TraceRounds.cs:32](../../../../../spikes/BuildHostFork/Trace/TraceRounds.cs#L32);
[ProjectCost.cs:23](../../../../../spikes/BuildHostFork/Trace/Models/ProjectCost.cs#L23);
[PathNames.cs:25-31](../../../../../spikes/BuildHostFork/Trace/PathNames.cs#L25-L31).

**Что не так.** По отдельности это nit’ы:

- Linux: сумма медиан 6 020 мс против медианы окна 6 033 мс; для Windows (42 549/42 394) и офиса (43 495/43 379)
  отчёт оговаривает, что медианы категорий не обязаны складываться, для Linux — нет. Разница 13 мс (0,2 %) и
  возникает из медиан, а не из утечки частей; достаточно одной фразы.
- `TraceRounds.cs:32` берёт `Projects`/`Documents`/`WorkspaceFailures`/`Hosts` из `measured[0]`, а категории —
  из всех раундов; если раунды различаются составом, summary этого не покажет.
- `ProjectCost.HostMs` не включает `load-project`, `output-path` и RPC-время; таблица «Projects by host time»
  (top-15) — это не весь host time проекта, а сумма выбранных категорий. На ранжирование это почти не влияет,
  но подпись стоит уточнить.
- Заглушки `project-NNN` назначаются в порядке обхода (`PathNames.Name`), а порядок top-15 по host time между
  раундами может меняться; `report.md` каждого раунда имеет свою карту, поэтому `project-001` в разных раундах —
  не обязательно один проект. В evidence это не сказано (карта у владельца), а сравнение по строкам может
  обмануть.

**Сценарий.** Читатель складывает медианы Linux-таблицы и получает 6 020 вместо 6 033; или сравнивает
`project-001` из `fork-round0` и `fork-round1` как один проект.

**Предлагаемое исправление.** Добавить оговорку про Linux; печатать состав по каждому раунду или проверять
равенство; переименовать колонку/поле в «selected host time»; в шапке отчёта написать, что заглушки локальны
для раунда.

**Уверенность:** высокая; влияние — только на чтение.

## 3. Проверено и в порядке

Ниже — что именно проверено и как. «Проверено» означает, что я читал код/данные или запускал; «по отчёту» —
что факт взят из отчёта/сводки и не пересчитан из сырых данных (см. раздел 4).

### Клиент 5.9.0

- **`IProgress<ProjectLoadProgress>.Report` синхронен и вызывается сразу после остановки таймера.** Проверено по
  исходнику `dotnet/roslyn@35d9211b841e7613c1d2f8f5af6d628ace696c4c`:
  [`ProjectLoadProgressExtensions.cs`](https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Workspaces/MSBuild/Core/MSBuild/ProjectLoadProgressExtensions.cs)
  — `Stopwatch.StartNew()` (только при `progress != null`), `await doFunc()`, в `finally` `watch.Stop()` и
  `progress.Report(new ProjectLoadProgress(..., watch.Elapsed))`. Никакого `Progress<T>`/SynchronizationContext
  между загрузчиком и `ProgressRecorder` нет: `BuildHostProjectFileInfoProvider` передаёт `progress` напрямую,
  `SolutionLoader` передаёт `ProgressRecorder` в `OpenSolutionAsync`. Значит `end` в `ProgressRecorder.Report` —
  момент конца операции (с точностью до вызова `Report`), а `start = end - ElapsedTime` — её начало. Ошибка
  сдвига — время создания `ProjectLoadProgress` и вызова, доли микросекунды.
- **Операции Evaluate/Build/Resolve.** [`BuildHostProjectFileInfoProvider.cs`](https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Workspaces/MSBuild/Core/MSBuild/BuildHostProjectFileInfoProvider.cs)
  оборачивает `LoadProjectFileAsync` в Evaluate, `GetProjectFileInfosAsync` — в Build;
  [`MSBuildProjectLoader.Worker.cs`](https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Workspaces/MSBuild/Core/MSBuild/MSBuildProjectLoader.Worker.cs)
  оборачивает `ResolveReferencesAsync` в Resolve. `GetDiagnosticLogItemsAsync` идёт **вне** Evaluate/Build — его
  ожидание попадает в «между операциями», а host-часть — в RPC-категории; это согласуется с разбивкой, но в
  отчёте отдельно не названо.
- **Клиент последователен.** `Worker.LoadAsync` — `foreach (var projectPath in _requestedProjectPaths) { await
  LoadProjectInfosFromPathAsync(...) }`; [`Worker_ResolveReferences.cs`](https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Workspaces/MSBuild/Core/MSBuild/MSBuildProjectLoader.Worker_ResolveReferences.cs)
  — `foreach (var projectFileReference in projectFileInfo.ProjectReferences) { await ... }`. В каждый момент один
  запрос; `MaxNodeCount` BuildHost не создаёт параллельных RPC. Это подтверждает и `clock check` (2 544/1 096
  matched, 0 нарушений) и категория «several host spans at once» 0–6 мс.
- **Корневые evaluation не попадают в binlog.** `ProjectBuildManager` создаёт
  `ProjectCollection(allProperties, loggers: [], ...)` — см.
  [ProjectBuildManager.cs:85](../../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/Build/ProjectBuildManager.cs#L85);
  binlog получает только сборки и evaluation внутри них. Утверждение отчёта верно.
- **Global properties 5.9.0.** [`MSBuildProjectLoader.cs`](https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Workspaces/MSBuild/Core/MSBuild/MSBuildProjectLoader.cs)
  передаёт один `Properties` + `SolutionDir` в `BuildHostProcessManager`, а тот — в `ConfigureGlobalState`;
  per-project Configuration/Platform в этой версии нет. Это ограничивает m-5 фактически, но не отменяет.

### Форк и трасса

- **Офф-путь.** `Start` возвращает `null` до чтения `s_current` и без аллокации Span;
  `using (BuildHostTrace.Start(...))`/`using var` — no-op; аргументы `span?.Set(...)` не вычисляются; протокол
  и построение `Response` не изменены. Единственная дополнительная работа — `Timestamp`/`line.Length` и чуть
  больший closure (n-1).
- **Протокол RPC.** `Request`/`Response`, `TargetObject`, `Id`, сериализация, `#if DEBUG`-проверка и запись
  `WriteLineAsync`/`FlushAsync` — те же; новые `using`-блоки не меняют порядок `InvokeMethod` → `await` →
  `Response`. Ответы не меняются, кроме M-1.
- **Потокобезопасность JSONL.** `WriteLine` строит JSON в локальном `MemoryStream`, затем `lock (s_gate)`
  вокруг `writer.WriteLine`; `AutoFlush = true`. Параллельные span’ы не перемешают строки. `Interlocked.Increment`
  даёт уникальные id.
- **Вложенность AsyncLocal.** `s_current` — `AsyncLocal<Span?>`; `Start` ставит родителя из текущего async-flow,
  `Dispose` восстанавливает. `Task.Run` в `RunAsync` захватывает контекст read-loop, где `s_current == null`,
  поэтому каждый `rpc` — корень. На фикстуре: 126 span’ов, 21 `rpc`, у всех `parent = 0`; нет span’ов, чей
  корень — не `rpc`/`connect`; у каждого `rpc` есть `rpc-send`. Кросс-запросного родителя нет. При параллельных
  запросах JSONL и вложенность останутся корректными, но анализ (`ClockCheck`, `ResolveEdges`) предполагает
  последовательность; категория «several host spans at once» фиксирует перекрытие, но не разбирает его по
  запросам. Для текущего клиента это не проблема.
- **Смена/возврат TFM.** `reevaluate` создаётся вокруг `SetGlobalProperty` + `ReevaluateIfNecessary` (цикл),
  `restore` — после цикла, вокруг `Remove/SetGlobalProperty` + `ReevaluateIfNecessary`; порядок upstream не
  изменён. В фикстуре у Lib ровно три `reevaluate`, у третьего `restoresTargetFramework: true` (проверено в
  `buildhost-*.jsonl`). Span возврата включает также `Remove/SetGlobalProperty` и `results.ToArray()` — не только
  `ReevaluateIfNecessary`, но это доли микросекунды; заявление отчёта «покрывают ровно вызов» — практически верно.
- **Ошибки записи.** Не изолированы — M-1.

### Разбивка окна и стоимости

- **Разбивка на раунд.** `WallBreakdown` строит self-time host-span’ов (span минус дочерние), очередь RPC
  (`received → rpc.start`), оценку старта (`ProcessStart → connect.start`), клиентские операции и края окна; sweep
  выбирает host > innermost client > «между». На фикстуре сумма категорий ровно 1 111 мс = окно; утечки нет.
  Медианы категорий по раундам не обязаны складываться в медиану окна: Windows 42 549 против 42 394, офис
  43 495 против 43 379 — отчёт это оговаривает; Linux 6 020 против 6 033 — нет (n-3).
- **Перекрытия/Unclosed.** Перекрытие host-span’ов — отдельная категория; на прогонах 0–6 мс. Работа вне окна
  обрезается; старт процесса — оценка по wall clock. Незакрытые span’ы не детектируются (m-11).
- **Запись строки ребёнка.** `end` ребёнка фиксируется до `WriteLine`, поэтому запись попадает в self-time
  родителя; отчёт это называет (строка 368). Величина мала: затронуты в основном мелкие категории (RPC dispatch,
  build bookkeeping, project file read).
- **Проектные стоимости.** `ProjectCosts.Cost` суммирует inclusive `evaluate`/`reevaluate`/`msbuild-build`/
  `create-instance`/`extract` и response serialize+send; `Client` — операции. `HostMs` не включает
  `load-project`/`output-path`/RPC (n-3). Top-15 и totals согласуются с категориями разбивки для соответствующих
  kinds: в фикстуре 210/43/339/69 мс; response 8 мс — это только `GetProjectFileInfosAsync`, а категории
  serialize+send 24 мс включают все RPC.
- **Повторы.** `Repeats` корректно считает повторные `evaluate`, `build-instance` (путь+TFM), `output-path` и
  `load-project (cached)`; в фикстуре 1 cached, 2 output path (1 cached), 0 повторов; в реальных прогонах 0.
  Ограничение по nested MSBuild — m-6.

### Часы и привязка к операциям

- `Stopwatch` на Windows — QPC, на Linux — `CLOCK_MONOTONIC`; оба системные для процессов одной машины. `Frequency`
  проверяется (`TraceReport.Run`: `hosts.Any(host => host.Frequency != client.Frequency)` → throw). `ClockCheck`
  подтверждает на каждой загрузке: 2 544 (Roslyn.slnx) и 1 096 (офис) matched, 0 violations, минимальные lead/tail
  в пределах ±0,204 мс. Для постоянного сдвига/дрейфа проверка lead/tail корректна: положительный сдвиг даёт
  отрицательный tail, отрицательный — отрицательный lead; >1 мс ловится.
- 40 unmatched в офисе = 5 проектов × 2 операции × 4 раунда; интерпретация «net472-host» правдоподобна и
  подтверждается типами проектов (не-SDK → net472 на Windows), но инструмент её не доказывает — m-4.
- `ProcessStart` — оценка по wall clock; остальные времена host — `Stopwatch`; отчёт помечает.

### Граф

- Узлы: `(path, TFM)` с Configuration/Platform из первого корневого `evaluate`; solution entries без сборки —
  `Built=false`; цели рёбер без узла добавляются. Рёбра: item из `extract` собранного проекта (с ROA), клиентская
  загрузка во время innermost Resolve, output path во время Resolve. В фикстуре 6 узлов, 5 built, 5 item-рёбер,
  2 client load, 2 output path, 1 ROA=false; все ребра соответствуют ожидаемым; App→Tool ROA=false и App→Lib
  загрузка во время Resolve подтверждены. Ошибок приписывания в фикстуре не нашёл.
- Граф не полон: ссылки внутри design-time build (MSBuild task) не видны; `ProjectGraph`/список ProjectReferences
  заранее полным не считается; отчёт это говорит. Global properties — m-5.

### Повторы и binlog

- Офис: 2 882 = 142 top-level + 2 740 nested; nested — только `GetTargetPath` (1 435 + 24) и
  `GetTargetFrameworks` (1 261 + 20); default targets нет; сумма inclusive 512 мс. Это подтверждает, что 2 882
  не полные повторные сборки. 1 435 = 1 435 рёбер — совпадение двух прогонов, не 1:1 (m-8).
- Roslyn.slnx: nested-binlog-анализа нет; «повторов нет» — trace-visible (m-6).

### Фикстура

- Запустил [Test-BuildHostTrace.ps1](../../../../../spikes/BuildHostFork/Test-BuildHostTrace.ps1): сборка форка и
  spike Release — 0 warnings/0 errors; фикстура — 5 проектов, 16 документов, 2 workspace failure; **14/14**;
  `clock matched 8, unmatched 0, violations 0`; `graph 6 nodes/5 built`; `repeats: 1 cached, 2 output path (1 cached)`;
  у Lib один root evaluate, 3 reevaluate (третий `restoresTargetFramework`), 2 build-instance (net10/net8), net8
  extracts на 1 документ больше. Все проверки не могут пройти на пустых данных (условия требуют ненулевых
  значений/точных множеств). Пробелы покрытия — m-7.

### Числа и согласованность

- Пересчитаны отношения/суммы: Roslyn.slnx Windows 18 374/8 302/6 037/5 777/930/585/849/1 405/290 → 42 549 при
  окне 42 394; 43,3/19,6/14,2/13,6/2,2/1,4/2,0/3,3/0,7 %; 100,9 с host ≈ 94 %, 2,5 с client ≈ 6 %.
  Офисный компьютер: 35 493/24 593/19 343/19 172 → 43,9 с evaluation (40,8 %) против 35,5 с build (32,9 %),
  отношения 1,93/2,96/3,20/3,32; окно 107 737, host 100,9 с. Офисное решение: 13 740/10 381/10 080 → 35 394 host
  (81,6 %), client 8 101 (18,7 %), окно 43 379; 5 044 net472 = 11,6 %; 2,2 с ≈ 5 %. 434−318=116; 245−129=116;
  2 882−2 740=142; 232−137−90=5; 475−434=41; 2 740=1 435+1 261+24+20; 512 мс ≈ 0,5 с. Все сходятся, кроме
  m-2 (61/44/82) и n-2 (2,2 с как нижняя граница).
- Статусы: task-файл и research README — «in progress, pending review/acceptance», что соответствует; родительские
  README — устарели (m-3).

### Публикация, стиль, production

- В tracked-файлах task-01 нет имени офисного решения, его путей и имён проектов: `summary.md`/`trace-summary.md` —
  только счётчики; `fork-round*-report.md` — `solution: (not shown)` и `project-NNN`; отчёт — «офисное решение»
  без имени; `Roslyn.VisualStudio.Setup.csproj` — публичный Roslyn. `client.json`, `buildhost-*.jsonl`,
  `graph.json`, `names-private.json` не tracked (`git ls-files` их не содержит). Redaction-tool имеет риск
  m-12, но текущие файлы чистые.
- Стиль: прочитал [docs/code-style.md](../../../../../docs/code-style.md) полностью. Новый/изменённый C# в
  `spikes/` — один тип на файл, имена файлов совпадают, порядок членов (свойства/методы/private-поля),
  English-текст, XML-doc на контрактных членах, `CancellationToken` последним, 4 пробела/скобки на своих
  строках; обязательных нарушений не нашёл. `forks/` — Roslyn-стиль, `upstream/` исключён из `docs/code-style.md`.
  В новом C# нет кириллицы; в трассе нет source content/environment/secrets: пути, длины, счётчики, TFM,
  Configuration/Platform, имена целей, `OverallResult`, id/метод — этого достаточно для таймингов и не больше.
- Production: `git diff --name-only ac92063~1 a35f682` — только `docs/`, `forks/`, `spikes/`;
  [RoslynMcpServer.csproj:68,71](../../../../../RoslynMcpServer.csproj#L68-L71) исключает `spikes/**` и `forks/**`;
  `forks/roslyn-buildhost/build` — отдельные проекты; production package/analyzer/watcher не меняются.
  `AnalyzerLifecycle` не затронут. Полный main-suite не запускал (см. раздел 4), потому что production-код не
  менялся; отчётная цифра 1 390 passed — по отчёту.

### Что именно запускал

- Windows 11 10.0.26200, PowerShell 7.6.8, .NET SDK 10.0.402.
- `pwsh spikes/BuildHostFork/Test-BuildHostTrace.ps1` — сборка форка и spike Release, генерация фикстуры,
  трассированная загрузка, `trace-report`, 14/14. Скрипт запускался **вне файловой песочницы агента**: сессия
  запускает команды с пониженными правами, а подкаталоги репозитория не имеют соответствующего разрешения на
  запись (корень его имеет), поэтому запись в них отклоняется. Это не ошибка сборки/репозитория; исходники не
  менялись. Сгенерированные `results/trace-check-*` и отчёт трассы — ignored.
- Разбор сгенерированной трассы: `buildhost-*.jsonl` (126 span’ов, 21 rpc, 5 evaluate, 5 build-instance,
  5 extract, 3 reevaluate, 2 output-path), `client.json`, `report.json`, `graph.json`; проверка parent id,
  `restoresTargetFramework`, сумм.
- `git diff --stat/--name-only ac92063~1 a35f682`, `git log`, `git status`, версии `bin-fork` host’ов.
- Чтение исходников upstream `35d9211b` (ProjectLoadProgress/Extensions, Worker, ResolveReferences,
  BuildHostProjectFileInfoProvider, MSBuildProjectLoader, BuildHostProcessManager), форка (`BuildHostTrace`,
  `RpcServer`, `ProjectBuildManager`, `ProjectFile`, `Program`, `AbstractBuildHost`,
  `NetFrameworkBuildHost`), spike и PS1.

## 4. Не проверено и почему

- **Baseline на Roslyn.slnx и офисном решении.** Не запускал: нет Roslyn-checkout’а нужного commit’а, офисного
  решения и сырых трасс (они не в Git и принадлежат владельцу). Числа identical/×1,011/×1,000/×0,995, 2 544/1 096
  запросов, 5,0 с, 129/245/129, 2 882/1 435/2 740 — по отчёту/сводкам, не пересчитаны из сырых данных.
- **Linux-прогоны** (`RoslynMcpServer.sln`, фикстура) — не на Linux; цифры 14/14, 83 %, 6 960/7 253 мс — по отчёту.
- **Полный main CI suite и Release-сборка решения.** Не запускал: production-код не менялся; отчётная цифра
  1 390 passed/0 failed/0 skipped не воспроизведена. Сборку форка и spike запускал (0/0).
- **Отказные сценарии task-00** (`Invoke-BuildHostForkScenarios.ps1`) — не запускал; отчёт говорит, что с
  выключенной трассировкой они проходят. Трассировка с включённой переменной в сценарии ранней отмены/убийства
  host’а не проверялась (m-11).
- **Ошибка открытия/записи трассы** — не воспроизводил (нет удобного способа заполнить диск/сделать read-only
  каталог без риска для окружения); M-1 — вывод по коду, не по запуску.
- **Форк net472-host** — не собирал и не запускал; офисный net472 — исходный пакетный (m-1).
- **Изолированная цена трассировки** (fork без трассы в том же сеансе) — не запускал; m-9.
- **Сырые офисные `client.json`, `buildhost-*.jsonl`, `graph.json`, `names-private.json`** — не в Git, не
  смотрел; поэтому 5,0 с, 2,2 с, порядок проектов, 40 unmatched и 1 435/1 261 — по отчёту/владельцу.
- **Binlog-анализ** — сами binlog’и не в Git; `analyze` не запускал; 2 740/1 435 — по отчёту task-01.
- **Comparator task-00** — не запускал; по diff он не менялся, `summary.md` — от владельца.
- **Другие review-каталоги task-00** — не читал (независимость ревью); `review-*`, defence, arbitration не
  использовались.
- **`.github` CI/`test-suite.yml`** — не проверял, что отчётная команда совпадает с workflow; это вне task-01
  (production не менялся).
