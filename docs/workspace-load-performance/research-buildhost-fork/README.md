# Исследование форка Roslyn BuildHost

Дата: **2026-10-22**. Статус: **in progress, 2026-10-24** — форк собран (task-00), независимое ревью
проведено, его находки исправляются; task-00 не принят, task-01 и task-02 не начаты. Git-ветка
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

- [task-00a — Временная шкала загрузки до форка](task-00a-load-timeline-before-fork-med.md) — **in progress, 2026-10-23**, решение владельца. Roslyn.slnx: 23 из 40 с binlog — вне сборок; ~20 с приходятся на промежутки перед первой сборкой каждого проекта и следуют за стоимостью evaluation (r = 0,67). Кандидат для форка — параллельная предварительная evaluation ([отчёт](evidence/task-00a-load-timeline.md)); офисная шкала pending.
- [task-00 — Воспроизводимый форк без изменения поведения](task-00-buildhost-fork-baseline-hi.md) — **in progress, 2026-10-24**: форк в `forks/roslyn-buildhost` (вариант A по решению владельца, стиль Roslyn). Независимое ревью проведено, находки исправлены ([разбор](evidence/task-00-review/disposition.md)); после исправлений identical на `RoslynMcpServer.sln` и Smoke, отказные сценарии совпадают, ранняя отмена оставляет BuildHost у обеих сторон (ограничение upstream, task-00b). Roslyn.slnx (434 проекта) и офис (142 проекта) — identical по сравнению до ревью; PowerToys исключён решением владельца; pending: повтор на Windows и в офисе ([отчёт](evidence/task-00-fork-baseline.md)).
- [task-00b — Утечка BuildHost при ранней отмене загрузки](task-00b-early-cancel-buildhost-leak-low.md) — **planned, 2026-10-24**, решение владельца: исправлено в `dotnet/roslyn` `2624f5c156` (клиент, после пакета 5.9.0); проверить новый пакет, до него оценить смягчение в сервере.
- [task-01 — Тайминги запросов и фактический load graph](task-01-buildhost-graph-and-timings-hi.md) — **planned**, после task-00.
- [task-02 — Направленный эксперимент оптимизации](task-02-buildhost-optimization-spike-hi.md) — **planned**, после task-01.

В активной реализации одновременно не больше двух задач. Сейчас идут task-00a и task-00. После каждой задачи обновляются
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
в отдельной Git-ветке, без reset/rebase существующей истории.

В Git: задачи, патчи/минимальный код интеграции, точная upstream revision,
build-инструкции, компактные сводки и hashes. Полный upstream checkout,
binlogs, traces, dumps, логи и ZIP — в ignored artifacts или отдельном архиве.
