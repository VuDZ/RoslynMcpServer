# E0/task-01 — ordinary baseline

Дата: 2026-10-08 (Europe/Moscow). Это отчёт прогона, не смена статуса задачи.
Статус task-01 остаётся planned: его выставляет родитель после ревью.
Production C# и версия `RoslynMcpServer.csproj` не менялись. Source version остаётся `1.5.5`.

Раннер: [scripts/workspace-state-cache/ordinary_baseline.py](../../../../scripts/workspace-state-cache/ordinary_baseline.py).
Как передавать workspace, query и server и что внутри таймера:
[README](../../../../scripts/workspace-state-cache/README.md).
Архивный `docs/backlog/workspace-load-cache/baseline_bench.py` не менялся.

Числа median/min/max ниже скопированы из [summary.json](task-01/summary.json).
Отдельного пересчёта нет.

## Контроль и заранее названный ответ

Workspace передан аргументом, не зашит в раннер:

`E:\Devel\RoslynMcpServer\samples\RoslynMcpPlugin\RoslynMcpPlugin.csproj`

Query: `find_symbol_definition`, `symbolName=SamplePlugin`.
Ожидаемое объявление, названное до прогона: `RoslynMcpPlugin.SamplePlugin`
в `samples/RoslynMcpPlugin/SamplePlugin.cs`.

Каждая успешная попытка нашла ровно это объявление: файл
`E:\Devel\RoslynMcpServer\samples\RoslynMcpPlugin\SamplePlugin.cs`, строка 5, колонка 21.
Промаха не было. Промах в медиану не входил бы.

Открытие этого csproj загрузило и ссылаемый проект. В ответе load два проекта:
`RoslynMcpPlugin` и `RoslynMcpServer`. Полный `RoslynMcpServer.sln` не загружался.
`.slnf` не передавался.

`configuration`, `platform`, `targetFramework`, `buildArgs` и
`shadowCopyInSolutionAnalyzers` не передавались. Строка health
`(SDK/workspace default)` — наблюдение текста ответа, не evaluated TFM.
BuildArgs в том же ответе `(none)`, global.json не найден, pinned SDK `(none)`,
resolved SDK directory `(not resolved)`. Restore assets: ok, 2/2.
Evaluated Configuration/Platform/TFM сняты отдельно, не из текста load:
[evaluated-instance.json](task-01/evaluated-instance.json).

## Publish и restore вне тайминга

Restore не запускался. Контроль уже на диске. В тайминг load он не входил.

Publish выполнен до первой попытки скриптом `publish-and-verify.ps1`
(`Release`, `win-x64`). Запись: [publish.json](task-01/publish.json).

| Публикация | Старт | Конец | Длительность | Код |
|---|---|---|---|---|
| первая | 2026-10-08T13:13:25.5857473+03:00 | 2026-10-08T13:16:03.5742876+03:00 | 157986.9 ms | 1 |
| вторая | 2026-10-08T13:17:51.0068614+03:00 | 2026-10-08T13:18:06.2238061+03:00 | 15215.4 ms | 0 |

Первая остановлена на MSB3027: после начальной проверки lock файл держал
`RoslynMcpServer.exe` PID 33564. Вторая завершилась с кодом 0.
Exe после неё: `bin\Release\net10.0\win-x64\publish\RoslynMcpServer.exe`,
FileVersion `1.5.5.0`, product `1.5.5+442ae060b85be0f4ab641274fe5c8c94d13642d7`.
Обе публикации кончились до warmup (первая строка журнала попыток — 10:18:34 UTC,
то есть 13:18:34+03:00). Их длительности в load, semantic и useful не входят.

## Compare key

Один ключ на все пять успешных measured-попыток.
Идентификатор: `85163a2486ed8a50a0a0946a344adf27bebcebca386eb4ee372ae756a270884d`.
Полный объект: [compare-key.json](task-01/compare-key.json).

Прежний идентификатор прогона был
`732e1edef5bc0f2b393f0ce5740ec527d093afc115cc8b3779607f1f77d3c6c4`.
Он посчитан по материалу, где строка health стояла на месте
Configuration/Platform/TFM. Материал ключа изменён, поэтому `compareKeyId`
другой. Пять load/semantic/useful заново не снимались. Файлы `attempt-*.json`
хранят ключ, записанный во время тех попыток.

| Поле | Значение |
|---|---|
| server commit / workspace revision | `442ae060b85be0f4ab641274fe5c8c94d13642d7` |
| server file version | `1.5.5.0` (product `1.5.5+442ae060…`) |
| source / assembly / file version | `1.5.5` / `1.5.5.0` / `1.5.5.0` |
| OS | `Windows-11-10.0.26200-SP0` |
| CPU | AMD Ryzen 7 9800X3D 8-Core Processor, `cpuCount` 16 |
| RAM | 100562268160 байт |
| content identity | `818ef96e131261bdc5d3760a1b9f937a156cfa8e405d8fa1406f247d4fc688c3` |
| scope identity | csproj и исходники project reference, 205 файлов |
| SDK | `10.0.300` |
| MSBuild | host `18.6.3.23102`, package pin `18.6.3` |
| Roslyn package | `5.9.0` |
| raw requested | configuration, platform, targetFramework, buildArgs, shadowCopy: `absent` |
| loader globals before open | fresh process; inherited Loaded* `absent`; `RoslynMcp.jsonc` нет ни у exe, ни в cwd; merged configuration/platform/targetFramework/buildArgs: `absent` |
| evaluated instance | Configuration `Debug`, Platform `AnyCPU`, TargetFramework `net10.0` |
| server health observation | `(SDK/workspace default)` для Configuration, Platform и TargetFramework. Это текст ответа, не три уровня выше |
| excluded emit/signing | `excluded` |
| semantic / overlay | forced ordinary baseline, overlay off |
| binary policy / threshold / chunks | not-applicable |
| query | `find_symbol_definition:SamplePlugin` |
| H-001 | external XML excluded |
| H-002 | strong-name variant B |
| cache hit | false |

Дерево на старте было грязным. Список porcelain входит в ключ
(`mutationsAtStart` в compare-key.json): незакоммиченные тесты
WorkspaceLoadCache, `scripts/`, каталог этой evidence и прочие пути,
которые задача не редактировала. Между попытками хеш 205 входных файлов
не менялся: `inputDifferences` у каждой попытки пустой
(`added`, `removed`, `changed`).

Полная XML-doc эквивалентность и точный перенос strong-name state не заявляются.
Это forced ordinary baseline, не cache hit и не RAM-hit.

## Прогон

Новый процесс на попытку. Файловый кэш ОС не сбрасывался.
Сценарий — new-process/warm-files.

`loadMs` — от отправки `tools/call load_workspace` до ответа.
`semanticMs` — от отправки `tools/call find_symbol_definition` до ответа.
`usefulMs` = сумма, только у успеха. `startupMs` (до ответа `initialize`)
в эти три метрики не входит. Первая компиляция, если она была внутри вызова,
не вычиталась. Отдельных стадий компиляции раннер не видит.

Один warmup в медиану не входит. Дальше пять measured, все успешные, один compare key.
Итог раннера: `partial=false`.

| # | Роль | PID | Исход | loadMs | semanticMs | usefulMs | В медиане |
|---|---|---|---|---|---|---|---|
| 0 | warmup | 79256 | success | 1536.8 | 966.1 | 2502.9 | нет |
| 1 | measured | 65268 | success | 1411.5 | 960.7 | 2372.2 | да |
| 2 | measured | 68420 | success | 1418.1 | 970.7 | 2388.8 | да |
| 3 | measured | 72152 | success | 1413.5 | 957.1 | 2370.6 | да |
| 4 | measured | 76124 | success | 1399.8 | 942.1 | 2341.9 | да |
| 5 | measured | 50244 | success | 1415.0 | 919.7 | 2334.7 | да |

PID: 79256, 65268, 68420, 72152, 76124, 50244. Все шесть различны.
Каждый measured PID отличается от warmup.

Measured: successes 5, errors 0, timeouts 0. Ретраев не было.

| Метрика | n | median | min | max |
|---|---|---|---|---|
| load | 5 | 1413.5 ms (1.41 s) | 1399.8 ms (1.40 s) | 1418.1 ms (1.42 s) |
| semantic | 5 | 957.1 ms (0.96 s) | 919.7 ms (0.92 s) | 970.7 ms (0.97 s) |
| useful | 5 | 2370.6 ms (2.37 s) | 2334.7 ms (2.33 s) | 2388.8 ms (2.39 s) |

p95 не считался и не обещается: n=5, порог протокола — 20.

## Raw

Каталог: [task-01/](task-01/).

- [summary.json](task-01/summary.json) — счётчики, median/min/max, таблица PID
- [compare-key.json](task-01/compare-key.json) — исправленный ключ, id `85163a24…`
- [evaluated-instance.json](task-01/evaluated-instance.json) — наблюдение evaluated Configuration/Platform/TFM вне таймера
- [publish.json](task-01/publish.json)
- [content-identity.json](task-01/content-identity.json) — 205 путей и их sha256
- [run.txt](task-01/run.txt)
- попытки: [warmup](task-01/attempt-00-warmup.json),
  [1](task-01/attempt-01.json), [2](task-01/attempt-02.json),
  [3](task-01/attempt-03.json), [4](task-01/attempt-04.json),
  [5](task-01/attempt-05.json)
- тела ответов рядом: `attempt-XX-load.txt`, `attempt-XX-semantic.txt`
  (у warmup — `attempt-00-warmup-load.txt` и `attempt-00-warmup-semantic.txt`)

Секретов и командных строк с токенами в этих файлах нет.

## Наблюдение на каждом semantic-ответе

Под объявлением во всех шести телах, включая warmup, сервер дописал одну и ту же
заметку disk-sync: сохранённый `.cs` появился или исчез вне снимка workspace.
Объявление `RoslynMcpPlugin.SamplePlugin` при этом на месте, исход попытки — success.
Хеш входных исходников между попытками не менялся. Заметка не превращена в ноль
и не выкинута из raw.

## Пределы

Не измерялись, и это не нули: input enumeration, stat, text hashing,
binary sample/read, decode, hydrate, prepare, publication, capture, write,
visited entries, bytes hashed, unique binaries, dirty/reloaded projects,
Open/DTB observations, disk envelope. Хеш исходников для compare key снят
снаружи таймера load.

Сэмпл working set на попытке есть в JSON (`peakWorkingSetBytes`), интервал
около 2 с при жизни процесса около 2.5 с. В median load/semantic/useful он не входит.

n=5, p95 нет. Сценарий только forced ordinary baseline.
H-001: внешний XML исключён. H-002: strong-name scope — вариант B.
Полная XML-doc и strong-name эквивалентность не заявляются.
Таймауты этого раннера (initialize 120 с, load 600 с, semantic 600 с)
не являются пинами архивного baseline.
