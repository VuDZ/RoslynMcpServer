# Исследование форка Roslyn BuildHost

Дата: **2026-10-22**. Статус: **in progress, 2026-10-26** — task-00 (воспроизводимый форк без изменения
поведения) accepted 2026-10-24 после двух раундов ревью; task-00a accepted 2026-10-24; task-00b in progress
(смягчение в сервере принято, пакет ещё не вышел); task-01 accepted 2026-10-24; task-02 выполнен, приёмка владельца pending;
task-03 accepted 2026-10-26 ([валидация и приёмка](evidence/task-03-server-integration.md#приёмка-2026-10-26)). Git-ветка
продолжения: `research/workspace-load-performance`.

Постановка по решению владельца: сохранить результаты офисных замеров и
исследовать возможности форка BuildHost. Это самостоятельная исследовательская
ветка темы [производительности](../README.md), а не замена результатов E0
и не разрешение включить новый загрузчик по умолчанию.

## Отправная точка

- Сервер 1.5.9, офисный baseline: load 49,079 с, из него MSBuild open около 44,9 с;
  первый поиск 34,175 с. [Результаты](../epoch-0-stage-measurement/task-08-source-search-and-office-results-med.md).
- Поиск, индексы и compilation выполняются в основном сервере. Форк BuildHost
  напрямую нацелен на load; [I/O/поиск](../epoch-0-stage-measurement/task-09-first-search-io-and-private-project-profile-hi.md)
  исследуются отдельно.
- Предыдущий core-first spike дал ускорение на PowerToys, но не достиг correctness
  на полном Roslyn. Его no-go сохраняется. [Task-07.3](../epoch-0-stage-measurement/task-07.3-evaluated-load-graph-hi.md)
  остаётся самостоятельным public-API экспериментом; она не получает разрешения на форк.
- В существующем BuildHost уже есть ProjectCollection, кеш загруженных проектов
  и переиспользуемый ProjectBuildManager. Простой запуск долгоживущего процесса
  или добавление кеша не считается доказанной новой оптимизацией.

## Задачи

- [task-00a — Временная шкала загрузки до форка](task-00a-load-timeline-before-fork-med.md) — **accepted, 2026-10-24** (решение владельца, [приёмка](evidence/task-00a-office-timeline-2026-10-24.md#приёмка-task-00a-2026-10-24)). Roslyn.slnx: ~20 с до первой сборки каждого файла, r = 0,67 ([отчёт](evidence/task-00a-load-timeline.md)). Офисная шкала разобрана: открытие 44,4 с, сборки 17,4 с, не менее 27,0 с вне этих сборок; два перекрывающихся binlog, 1676 workspace Failure, r ≈ 0,36 по общей timestamp-оценке. Приоритет — spans task-01; весь остаток нельзя считать evaluation или IPC ([офисный отчёт](evidence/task-00a-office-timeline-2026-10-24.md)). Исправление `SolutionDir` проверено ([валидация](evidence/task-00a-solutiondir-fix.md)).
- [task-00 — Воспроизводимый форк без изменения поведения](task-00-buildhost-fork-baseline-hi.md) — **accepted, 2026-10-24** (решение владельца, [приёмка](evidence/task-00-fork-baseline.md#приёмка-2026-10-24)): форк в `forks/roslyn-buildhost` (вариант A по решению владельца, стиль Roslyn). После ревью раунда 1 ([разбор](evidence/task-00-review/disposition.md)) identical на Smoke, `RoslynMcpServer.sln`, Roslyn.slnx (434 проекта, Windows, загрузка ×0,992) и офисе (142 проекта, ×1,000); сценарии на Linux и Windows пройдены, ранняя отмена оставляет BuildHost у обеих сторон (ограничение upstream, task-00b); PowerToys исключён решением владельца. Ревью раунда 2: регрессии форка нет, находки исправлены ([разбор](evidence/task-00-review/disposition-round-2.md)); второй BuildHost в офисе — пакетный net472-host у обеих сторон; сценарии на Windows с исправленным spike пройдены ([отчёт](evidence/task-00-fork-baseline.md)).
- [task-00b — Утечка BuildHost при ранней отмене загрузки](task-00b-early-cancel-buildhost-leak-low.md) — **in progress, 2026-10-24**: ранняя отмена в сервере достижима; смягчение в сервере 1.5.16 принято 2026-10-24 после сужения до прямых потомков ([приёмка](evidence/task-00b-early-cancel.md#приёмка-смягчения-в-сервере-2026-10-24)). Проверка пакетом с `2624f5c156` и сценарий task-00 — pending, на nuget.org всё ещё 5.9.0.
- [task-01 — Тайминги запросов и фактический load graph](task-01-buildhost-graph-and-timings-hi.md) — **accepted, 2026-10-24** (решение владельца, [приёмка](evidence/task-01-graph-and-timings.md#приёмка-2026-10-24)): трассировка в форке (`ROSLYN_BUILDHOST_TRACE_DIRECTORY`) и тайминги клиента через `IProgress`; общие часы проверяются на каждой загрузке. Клиент 5.9.0 грузит проекты строго по одному, корневые evaluation в binlog не попадают. `RoslynMcpServer.sln` (Linux): identical с трассировкой, 83 % окна — работа host, первый проект в свежем процессе дорог (~1,5 с). Windows: Roslyn.slnx identical с трассировкой (×1,011), 94 % окна — работа host, design-time build 43 %, переоценка при смене TFM 19,6 % (из них возврат TFM 3,5 %), повторов, видимых трассой, нет; на офисном компьютере тот же Roslyn.slnx identical (×1,000), загрузка в 2,5 раза дольше, evaluation замедляется сильнее сборки, и с переоценкой TFM она весит больше (40,8 % против 32,9 %). Офисное решение: identical (×0,995), сборка 31,7 %, корневая evaluation 23,9 %, извлечение 23,2 %, переоценки TFM нет, 5 проектов пакетного net472-host без трассы — 5,0 с (11,6 %), первый проект загрузки 2,3–2,5 с (разогрев — гипотеза), повторов нет: одна сборка на проект; 2 740 вложенных запусков binlog — только `GetTargetPath`/`GetTargetFrameworks`, ~0,5 с ([отчёт](evidence/task-01-graph-and-timings.md)). Ревью раунда 1: блокеров нет, major (ошибка трассы меняла ответ host) и minor исправлены, изолированная цена трассировки — в task-02 ([разбор](evidence/task-01-review/disposition.md)); фикстура 19/19 (Linux), main-набор 1 390 passed.
- [task-02 — Повторные evaluation и упреждающая работа в host](task-02-buildhost-optimization-spike-hi.md) — **выполнен, 2026-10-26; приёмка владельца — pending**: A, B и A + B — go, A + B принимается как сочетание; все варианты identical, к форку без оптимизаций A + B ×0,732 (офис), ×0,835 и ×0,816 (Roslyn.slnx, офисный и домашний компьютеры), быстрее каждой части, пик памяти host не выше +9,1 % ([отчёт эксперимента](evidence/task-02-optimization-spike.md)). История: (постановка переписана по решению владельца: A — одна evaluation на проект, B — evaluation наперёд, затем A + B, каждая часть измеряется отдельно; изолированная цена трассировки — из task-01, [F10](evidence/task-01-review/disposition.md)). Шаг 0: evaluation внутри сборок повторяют корневые — офис 100 из 102 (5,3 с), Roslyn.slnx 180 из 250 (4,8 с); проба `spikes/BuildOrderProbe` (результаты identical): «ссылки раньше» — офис −15,9 %, Roslyn.slnx −9,6 % wall пробы; evaluation наперёд — офис ещё −3,7 с (вместе −29,3 %), Roslyn.slnx без выигрыша (одновременные evaluation ждут друг друга, опережение в один уровень оставляет сборки ждать); параллельные сборки в одном `BuildManager` не ускоряют; server GC — ещё −8 п. п. на Roslyn.slnx; опережение на всё замыкание на Roslyn.slnx (два компьютера) без выигрыша — сборка стоит, пока рядом идёт evaluation; в офисе evaluation идут параллельно и со сборками почти без цены: evaluation наперёд в один уровень −17,7 % сверх A (A + B −31,1 % wall пробы), Roslyn.slnx в пределах +1 %, замыкание не лучше одного уровня. Шаг 0 выполнен, B не снимается ([итог шага 0](evidence/task-02-step-0.md#итог-шага-0-для-b)). Этап 1 (A в форке) выполнен 2026-10-25: первая версия на офисе потеряла 5 предупреждений evaluation ссылок; с точным воспроизведением логов upstream (вариант 1) Roslyn.slnx и офис identical, диагностики поэлементно в том же порядке, загрузка ×0,928 и ×0,899 к оригиналу, сценарии task-00 и main-набор пройдены ([этап 1](evidence/task-02-stage-1-references-first.md#итог-этапа-1)); следующий — этап 2 (B), по решению владельца. Вывод шага 0 о B на Roslyn.slnx — артефакт пробы (`ProjectCollection.LoadProject` держит блокировку коллекции всю evaluation); с evaluation как у host замыкание даёт Roslyn.slnx −6 %, офис −19…−20 % к A, но записи решения, на которые никто не ссылается, поток сборок оценивает сам: host их заранее не знает. Потолок этого знания (`evaluate-solution-preorder`, Roslyn.slnx дома): простой потока сборок 0,27 с вместо 5 с, wall −10 % к A, но сборки удлиняются на 3,6–4,9 с; окно из 4 следующих записей даёт тот же выигрыш (−10,8 % к A), GC удлинение сборок не объясняет; на офисном компьютере с записями решения B даёт к A −20 % (Roslyn.slnx) и −41 % (офис, все записи), без них −6 % и −20 %, пик памяти не выше +7 %; предложение для этапа 2 — замыкание в прямом порядке, 4 потока, все записи решения с первого запроса, искать ли решение по `SolutionDir` — решение владельца ([итог B](evidence/task-02-step-0.md#итог-b-после-шага-0-2026-10-25)). Этап 2 (B в форке, `ROSLYN_BUILDHOST_EVALUATE_AHEAD`) — in progress: угадывание решения по `SolutionDir` на Roslyn.slnx не сработало (рядом три `.slnf`), по решению владельца (2026-10-26) решение передаёт клиент свойством `RoslynMcpSolutionPath`, host забирает его до любой evaluation; Linux identical; офисный компьютер, A + B (2026-10-26): Roslyn.slnx и офис identical, записи решения взяты, загрузка ×0,783 и ×0,742 к оригиналу (A — ×0,928 и ×0,899), пик памяти host не выше +8,8 %; дома Roslyn.slnx identical: A + B ×0,816, только B ×0,877 (A — ×0,931). Этап 2 выполнен 2026-10-26: только B на офисном компьютере identical, ×0,850 и ×0,834, но пик памяти host +11,5 % и +13,4 % (порог +10 %); A + B быстрее каждой части везде и в пределах порога памяти ([итог этапа 2](evidence/task-02-stage-2-evaluate-ahead.md#итог-этапа-2)); превышение порога памяти на несколько п. п. по решению владельца не стоп-фактор. Этап 3 (замер всех вариантов в одном прогоне, `Invoke-BuildHostForkMatrix.ps1`) — in progress: Linux-проверка identical; Roslyn.slnx дома — все варианты identical, к форку без оптимизаций A ×0,932, B ×0,857, A + B ×0,816, цена трассы не видна; офисный компьютер — все варианты identical, офис A ×0,898, B ×0,838, A + B ×0,732; этап 3 выполнен 2026-10-26 ([этап 3](evidence/task-02-stage-3-matrix.md)).
- [task-03 — Форк BuildHost с A + B в сервере](task-03-buildhost-fork-server-integration-hi.md) — **accepted, 2026-10-26**: A + B включены по умолчанию, 4 потока B; Linux build/publish/pack и Windows Release build/publish пройдены, main 1467/0/0 (включая SourceStructure 123/0/0), `AnalyzerLifecycle` 13/0/0. Roslyn.slnx: ответ без изменений, медиана `load_workspace` 54,13 → 44,73 с к 1.5.16 (−17,4 %); подбор параметров и релиз вне приёмки ([отчёт и приёмка](evidence/task-03-server-integration.md#приёмка-2026-10-26)).
- [task-04 — Извлечение ProjectFileInfo в host](task-04-buildhost-projectfileinfo-extraction-hi.md) — **in progress, 2026-10-26**: инструментирование шага 0 выполнено, расчёт полей сохранён; части `extract` и counts видны в трассе и сводке. Фикстура 74/74, Linux `RoslynMcpServer.sln` и Windows Roslyn.slnx identical, Release build, AnalyzerLifecycle 13/13; полный main 1467/0/0 без параллелизма коллекций (обычный запуск воспроизводит сбой теста атрибуции). Windows Roslyn.slnx: globs — 8,39 с (21,6 % окна), повторы между TFM — 24 мс за загрузку. Обработка относительных путей matrix/baseline/scenarios исправлена, автоматический каталог проверен. Офисные матрицы обоих решений получены и identical, но без новых под-span extraction (0 `extract-*` в Roslyn JSONL); для повторного замера правки передаются через Git по решению владельца. Итог шага 0 и порог go — pending; оптимизационного кандидата пока нет ([отчёт](evidence/task-04-projectfileinfo-extraction.md)).

В активной реализации одновременно не больше двух задач. Сейчас идёт task-04; task-02 выполнен и ждёт приёмки владельца; task-00b ждёт пакета новее 5.9.0; task-00, task-00a, task-01 и task-03 приняты. После каждой задачи обновляются
её статус, этот индекс и evidence; незавершённая проверка сохраняется как pending.

## Где искать выигрыш

1. Выдача графа из уже выполненной evaluation и наблюдаемых запросов, без отдельной
   повторной загрузки ради планировщика. Учитывать imported/dynamic references,
   effective properties, TFM, native entries и запросы output paths.
2. Пакетная/ограниченно параллельная обработка независимых проектов в общей
   MSBuild-сессии. Исключать повторы общего ядра и гонки в общих obj/outputs;
   общий mutable Project нельзя одновременно переключать между TFM.
3. Устранение измеренных повторов reference/output queries, evaluation и extraction
   ProjectFileInfo; кешировать только результаты с корректным ключом и временем жизни.
4. Сокращение RPC и сериализации через пакетные ответы, если их цена подтверждена
   собственными spans. Остаток между binlog events не объявлять временем IPC.
5. Анализ targets/tasks: reference resolution, WPF markup, генераторы, SDK/import
   resolution. SkipCompilerExecution уже используется; нельзя ускорять потерей
   compiler args, .g.cs, options, references, diagnostics или analyzer evidence.
6. Переиспользование состояния между загрузками — отдельный эксперимент с invalidation
   на changes в проектах/imports/SDK/global properties. Не подменять им fresh-process baseline.

Сначала нужен профиль, затем один кандидат за раз. Направления — гипотезы, не
обещание конкретного speedup. Общая метрика: load + first useful semantic query,
дополнительно wall каждой стадии, CPU, peak RAM, ошибки и полный результат загрузки.

## Границы

Исторические evidence не переписываются. Изолированный форк не изменяет production
package references/defaults, analyzer admission, write boundary или watcher policy.
Изменение RPC потребует согласованного клиента; подмена только host DLL не
считается достаточной. Путь main остаётся на исходном commit; работа продолжается
в отдельной Git-ветке, без reset/rebase существующей истории. Исключение — решение владельца
2026-10-24: история ветки один раз переписана, чтобы убрать имена офисных проектов (R2-01); итоговые
файлы не изменились, хеши коммитов в документах и JSON результатов заменены новыми.

В Git: задачи, патчи/минимальный код интеграции, точная upstream revision,
build-инструкции, компактные сводки и hashes. Полный upstream checkout,
binlogs, traces, dumps, логи и ZIP — в ignored artifacts или отдельном архиве.
