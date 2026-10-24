# BuildHost/task-01 — Тайминги запросов и фактический load graph

- Дата: 2026-10-22.
- Статус: **in progress, 2026-10-24** — трассировка в форке и отчёт в spike готовы; проверены на фикстуре
  (14/14) и `RoslynMcpServer.sln` (Linux: identical с трассировкой, часы согласованы, 83 % окна — работа host).
  Pending: Roslyn.slnx на Windows и офис (прогоны владельца), независимое ревью, приёмка.
- Сложность: hi; риск — выдать неполный reference graph за граф всех операций загрузки.
- Depends on: [task-00](task-00-buildhost-fork-baseline-hi.md).
- Evidence/validation: [отчёт](evidence/task-01-graph-and-timings.md).

## Цель

Объяснить стоимость загрузки и получить граф, пригодный для проверки ownership
и порядка работ, из того же выполнения BuildHost. Не выполнять отдельную полную
evaluation бесплатно за пределами измеряемого процесса.

## Наблюдения

Для запроса фиксировать operation/request id, effective project context,
родительскую операцию, начало/конец, outcome и cache hit/reuse при наличии:

- загрузка XML/imports и evaluation, включая root evaluations;
- output/reference queries и изменения effective properties;
- design-time build, targets/tasks и extraction ProjectFileInfo;
- очередь/ожидание, RPC transport и сериализация — отдельные spans на нужной стороне.

Для длительностей использовать monotonic timer/TimeProvider на каждой стороне.
Не вычитать независимые monotonic clocks разных процессов без синхронизации.
Inclusive суммы не подменяют critical path/wall; непокрытый остаток остаётся unknown.

Узел графа включает путь и effective context (как минимум Configuration/Platform/
TFM и влияющие global properties). Каждое ребро имеет происхождение:
evaluated item, фактический reference/output query либо другая наблюдаемая операция.
Сохранить ROA=false, imported/conditional/dynamic зависимости, native/unloadable
entries и все исходные solution entries. Не считать статический ProjectGraph
или список ProjectReferences заранее полным графом загрузки.

## Проверки и результат

- Ответ original/fork по task-00 остаётся эквивалентен, comparator не ослабляется.
- Проверены условия/imports, несколько TFM, один путь с разными properties,
  ROA=false и повторные query; неполнота наблюдений названа явно.
- Разделены cold/warm, timestamp estimates и точные spans; измерен overhead логирования.
- Есть компактный graph witness, inventory повторов, critical-path профиль
  и ранжированный список кандидатов. 2882 project starts офисного binlog
  на 142 проекта не означают 2882 полных повторных сборки.
- Если граф используется для E0/task-07.3, отдельно соблюсти её public-API границы:
  fork evidence не превращается в её успешное выполнение.

Логи English, без source content/environment/secrets; пути редактировать перед
переносом в Git. Validation — правила task-00 и основной AGENTS.md, включая
AnalyzerLifecycle при затронутом пути. Отчёт: evidence/task-01-graph-and-timings.md.
После выполнения обновить статус этой задачи и README; full graph correctness
не объявлять по одному успешно загруженному решению.
