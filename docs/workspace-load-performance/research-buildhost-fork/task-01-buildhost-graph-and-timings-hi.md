# BuildHost/task-01 — Тайминги запросов и фактический load graph

- Дата: 2026-10-22.
- Статус: **accepted, 2026-10-24** (решение владельца, [приёмка](evidence/task-01-graph-and-timings.md#приёмка-2026-10-24);
  принятый объём и ограничения — там же) — трассировка в форке и отчёт в spike готовы; проверены на фикстуре
  (14/14) и `RoslynMcpServer.sln` (Linux: identical с трассировкой, часы согласованы, 83 % окна — работа host).
  Windows (2026-10-24): фикстура 14/14; Roslyn.slnx identical с трассировкой (×1,011), 94 % окна — работа
  host; design-time build 43 %, переоценка при смене TFM 19,6 % (из них возврат TFM 3,5 %), повторов, видимых
  трассой, нет. Roslyn.slnx на офисном компьютере: identical (×1,000), загрузка в 2,5 раза дольше; evaluation и
  извлечение медленнее в 3,0–3,3 раза, сборка в 1,9, и с переоценкой TFM evaluation (40,8 %) весит больше
  сборки (32,9 %). Офисное решение: identical (×0,995); сборка 31,7 %, корневая evaluation 23,9 %, извлечение
  23,2 %, переоценки TFM нет; 5 проектов пакетного net472-host без трассы — 5,0 с (11,6 %); первый проект
  загрузки 2,3–2,5 с (разогрев — гипотеза); повторов нет, одна сборка на проект. 2 882 запуска в binlog — 142
  сборки и 2 740 вложенных вызовов только `GetTargetPath`/`GetTargetFrameworks`, вместе ~0,5 с; полных
  повторных сборок нет. Ревью раунда 1 (Grok 4.7, DeepSeek Flash): блокеров нет; major — ошибка трассы меняла
  ответ host — исправлена, minor исправлены, изолированная цена трассировки отложена в task-02
  ([разбор](evidence/task-01-review/disposition.md)); фикстура после исправлений — 19/19 (Linux), main-набор
  1 390 passed.
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
