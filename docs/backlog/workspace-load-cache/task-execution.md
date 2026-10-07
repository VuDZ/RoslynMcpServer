# Выполнение задач workspace load cache

Это рабочая декомпозиция v2, а не новая спецификация и не свидетельство реализации.
Каждая эпоха — единица приёмки; отдельный task — единица делегирования.
Наличие task-файла не разрешает implementation или public activation автоматически.

## Источники требований

Перед началом прочитать spec.md своей эпохи, [cache-contract](cache-contract.md),
нужные строки [verification](verification.md), связанные решения
[UNRESOLVED-v2](UNRESOLVED-v2.md) и outputs dependencies.
При расхождении task-файла со spec/contract решение не подменять:
вернуть конкретное расхождение координатору и зафиксировать его в handoff.
Task-файлы уточняют порядок и границы исполнения; нормативные MUST остаются в spec.

Уже выбранные AdhocWorkspace/.cs writer, sdk-project-v1 с Razor/Web, portable
DLL recheck + новый LoadSessionId, capture до ответа load_workspace и watcher
read policy не выбирать заново. useDiskCache остаётся opt-in с default=false.
Дополнительный внутренний feature flag не вводится.

U-ARB-03 отложен до полноценной технической реализации кеша. Он не блокирует
реализацию и функциональные fixtures, но остаётся gate public activation.
Численные budgets владелец утверждает до первых cache-hit performance results,
а не после замеров. Workload/budget и замеры — E4/task-16; независимый gate — E4/task-13. OrchardCore/Roslyn не обязательны;
BTCPayServer — кандидат, и его negative admission не становится positive reuse.

## Модель, reasoning, сложность и риск

Суффикс имени — рекомендуемый исполнитель: Astra, Sol или Luna. Это не запрет
замены более сильной моделью. Градация Luna/Sol/Astra пока сохраняется;
переход к профилям исполнителя low/med/high выполняется отдельно и не меняет
значение reasoning effort.

Оценка сложности 1–5 остаётся ориентиром для человека: она учитывает
неопределённость, число связанных contracts и объём проектирования/проверки.
Числа задач не изменены; вторая шкала complexity=low/medium/high не добавляется.
Сложность не является порогом dispatch: модель и reasoning заданы отдельно,
их нельзя автоматически выводить только из оценки 1–5.

- 1: механическая локальная правка по полностью заданному результату.
- 2: ограниченная модель данных, codec, fixtures или telemetry по принятой schema; Luna.
- 3: понятная реализация и focused проверки нескольких компонентов; обычно Sol.
- 4: admission/semantic equivalence/compatibility и несколько связанных paths; Sol
  по принятому design, Astra для design или независимой приёмки.
- 5: ownership, concurrency, публикация, provenance или новый reuse subset; Astra.

Модель не является критерием acceptance. Если Luna/Sol встречает незаданный
contract или архитектурный выбор, вернуть конкретный вопрос в design task:
не достраивать требования по догадке.

### Reasoning при запуске

В каждой задаче указано поле «Рекомендуемый reasoning». Это настройка запуска
выбранной модели, отдельная от сложности и её названия. Поле в Markdown само
настройку не применяет: координатор передаёт её при dispatch.

Для текущего пула стартовые рекомендации:

- Luna: medium для ограниченных fixtures/codec/telemetry по принятому packet.
- Sol: medium для понятной реализации и fixtures; high для сложных production
  paths, strict validation, reuse oracle, fault/restart matrix и index design.
- Astra: high для contracts, ownership/concurrency и независимой приёмки;
  medium для отдельного планирования gates направлений E4/task-00.

Это рабочие настройки пула. Low подходит механическим шагам; в текущих задачах
есть проверка contract/evidence, поэтому low не назначен автоматически по модели.
Xhigh допускается для сложных races/reuse расследований, когда повторяемая проверка
показывает пользу относительно high; max не назначается повсеместно.

Повышенный effort может увеличить время и расход токенов; доступные значения зависят от
модели. Настройка не заменяет filled packet, acceptance evidence или независимость
review session. [OpenAI Docs: reasoning effort](https://developers.openai.com/api/docs/guides/reasoning#reasoning-effort)

### Риск ошибки

«Риск ошибки» оценивает последствия неверного результата в заданном scope,
включая ошибочное evidence/acceptance. Это отдельная ось, не копия сложности
и не вероятность того, что выбранная модель ошибётся.

- низкий: локальная обратимая ошибка без влияния на semantic/disk state или gates;
- средний: неверные fixtures/coverage/diagnostics/измерения либо ограниченная
  feature regression, выявляемая следующей проверкой;
- высокий: неправильный graph/hit, запись по неверной базе, lost events,
  нарушение admission/lifetime/limits или неверный implementation/activation verdict.

Каждая задача содержит краткую причину своего уровня. Например, bounded codec
остаётся 2/5 и Luna/medium, но имеет высокий риск из-за malformed input/size bounds.
Высокий риск сам по себе не меняет scope, зависимости или модель.

### Необходимые способности

Поле «Необходимые способности» задаёт 2–4 основных навыка для этого среза
(у сложной приёмки перечень может быть шире). Это требования к исполнителю,
а не имена моделей, permissions или разрешение расширить scope.
Используются стабильные идентификаторы; новый ID сначала описывается здесь.

- `архитектурное_мышление`: Проектировать contracts, инварианты и границы scope; разрешать незаданные решения до implementation.
- `анализ_зависимостей`: Прослеживать positive/negative inputs, closure, instances и invalidation.
- `согласованность_компонентов`: Сохранять совместимые contracts между load, tools, shared input state и persistence.
- `семантика_roslyn_msbuild`: Работать с graph/options/memberships/TFM/analyzers и независимым semantic oracle.
- `анализ_конкурентности`: Разбирать interleavings, cancellation, locks, revisions и atomic publication.
- `управление_ресурсами`: Управлять candidate/reader/process lifetime, ownership, leases и bounded cleanup.
- `реализация_по_контракту`: Реализовывать принятые interfaces и packet без самостоятельного расширения scope.
- `проверка_целостности_данных`: Проверять bytes/encoding/checksum/schema/size limits и malformed inputs.
- `проектирование_проверок`: Строить fixtures, assertions, fault harness и воспроизводимые regression scenarios.
- `независимая_проверка`: Проверять чужой design/diff/evidence и отклонять неподтверждённые verdicts.
- `измерение_производительности`: Фиксировать workload/budget до замеров, измерять stages и оценивать raw attempts.
- `диагностика_и_наблюдаемость`: Различать outcomes/reasons, сохранять telemetry и пригодный для аудита report.

Рекомендуемый профиль хранится в шапке task-файла; README показывает модель/
reasoning, человеческую сложность и риск. Суффикс имени файла остаётся прежним.
Переход к low/med/high профилям исполнителя сможет использовать эти требования,
не смешивая их с API значениями reasoning.

## Зависимости и владение

Число в имени — стабильный ID, а не автоматический порядок запуска.
Depends on задаётся в каждом task-файле и карте эпохи. Для начала нужны принятые
outputs dependencies и применимый implementation verdict предыдущего handoff.
Принятый task может честно содержать failed/not-run/deferred outcome исследования;
это не positive evidence и не автоматическое разрешение следующего production шага.

Параллельно допустимы только задания с принятыми interfaces и независимыми
границами правок. SolutionManager, общий load/prepare/publication path и shared
input session одновременно правит один исполнитель. Координатор назначает
владельца и последовательность интеграции; тестовые задания не исправляют
production architecture незаметно для её владельца.

## Implementation packet до dispatch

Каждая реализация получает принятый пакет от design owner, а не один общий spec.
Пакет имеет стабильное имя `design/implementation-packet-task-NN.md` в каталоге
эпохи. Эти имена обозначают будущие outputs, файлы сейчас не выданы за completed.
В task-файле явно указаны producer и ребро Depends on к нему.

Design task не получает accepted status, пока для каждого своего потребителя
не готовы четыре перечня, привязанные к audited/current code:

1. **Новые типы:** точные имена, namespace, файлы, поля/инварианты либо явно «нет».
2. **Можно менять:** конечный список existing files и new files, диапазон ответственности,
   integration owner и согласованная очередность для общих файлов.
3. **Нельзя менять:** явный denylist; все files вне allowlist тоже запрещены.
4. **Потребляемые сигнатуры:** точные existing/proposed declarations, parameters/nullability,
   return/error/cancellation contract, ownership/acquisition и обязательные fixtures/assertions.

Списки в текущих task-файлах задают ограниченный срез и существующие seams.
Будущие типы/сигнатуры не выдуманы до design: producer обязан разрешить все
незаданные имена и записать окончательный пакет до acceptance. Исполнитель Sol/Luna
не извлекает этот пакет самостоятельно из spec и не проектирует чужие interfaces.
Незаполненный пакет оставляет design в review, downstream dispatch запрещён.
Изменение принятого interface требует обновить все затронутые packets до продолжения.

Для каждой задачи исполнитель предъявляет code/doc diff, результаты checks,
evidence/limits и gaps. Review возвращает исправления тому же исполнителю.

## Независимая приёмочная сессия

Суффикс Astra обозначает модель, а не личность исполнителя. Любая задача с
`Роль: independent-acceptance` запускается отдельной новой сессией, которая
не писала ни одного design-документа этой эпохи и не реализовывала проверяемый scope.
Координатор не назначает туда author session даже при той же модели или новом
prompt. Fresh reviewer получает spec, audited baseline, accepted contracts/packets,
diff и validation evidence; авторский диалог не подменяет независимую проверку.

Перед dispatch планировщик проверяет `reviewSessionId != designAuthorSessionIds`
и `reviewSessionId != implementationAuthorSessionIds` для scope. В status/отчёте
фиксируются review session и author session IDs. При конфликте задача не начинается.
Приёмщик пишет только acceptance report/handoff draft и must-fix findings;
исправления возвращаются автору и перепроверяются той же независимой сессией.
Координатор владеет final build/tests и фиксацией решений по [handoff](handoff-template.md);
его validation не заменяет независимую приёмку.

Все обязательные implementation prerequisites стоят в Depends on.
Условная приёмка не прячется в prose: независимые ветки имеют отдельные задачи
с обычными рёбрами. E4/task-12 принимает 4A после task-02, task-14 — 4B после
task-04, task-15 — 4D после task-10. Основной series gate task-13 не принимает
optional ветки и зависит от workload/performance task-16.

## Проверки кода и итоговая validation

Применимы [AGENTS.md](../../../AGENTS.md). До первой правки C# (включая tests) прочитать
[docs/code-style.md](../../code-style.md) целиком; передать это требование исполнителю.
Правила применяются к новому коду и изменённым строкам, остальной файл не перестраивать.
Comments, diagnostics, logs, exception/assertion text внутри кода — English.

Во время работы — focused tests. IDs из task/spec — минимум, не полный scope.
После production C# правок выполнить все tests в RoslynMcpServer.Tests/SourceStructure.
Не ослаблять expected inventory structural limits ради зелёного результата.

Перед окончательной приёмкой или commit code/test изменений координатор строит
RoslynMcpServer.sln и выполняет полный main CI suite в Release, Category!=AnalyzerLifecycle,
по [.github/workflows/test-suite.yml](../../../.github/workflows/test-suite.yml).
После исправлений affected tests повторить во время разработки, полный main suite —
на final code. Roslyn MCP build/test runners предпочтительны, если выражают нужный scope/env.

AnalyzerLifecycle дополнительно обязателен при затронутом analyzer/shadow-copy lifecycle,
workspace load/prepare/publication, lifecycle host или test/build configuration;
для него задать ROSLYN_MCP_ANALYZER_LIFECYCLE=1. Unrelated changes его не требуют.
Zero-test run не pass; failures/skips/timeouts/unavailable environment явно записываются.
Independent review и spec acceptance финальную build/test validation не заменяют.
Docs-only изменения не требуют запуска C# suite.

## Учёт и приёмка

Статусы задач ведутся явно в task-файле и README своей эпохи:
planned → in-progress → review → accepted; blocked и deferred содержат причину.
После выполнения координатор до итогового ответа синхронизирует эти статусы,
дату, принятый scope и ссылку на validation/acceptance report; обновляет
устаревшие execution-status summaries в reports и packets. Эти служебные правки
входят в завершение задачи, даже если implementation allowlist перечисляет только
code/evidence files; требования, contracts и activation permissions не меняются.
Статус planned не отменяет существующие spike/baseline evidence: задание проверяет,
что этого evidence достаточно для своего результата на актуальном коде.
Текущие результаты выполнения указаны в task-файлах и картах эпох; наличие
плана само по себе не означает запуск или приёмку задачи.

У Epoch 4 отдельные статусы/отчёты по 4A/4B/4C/4D. O5 — обязательный результат;
4C выбран как маршрут в пуле, иной доказанный механизм возможен по spec.
Незавершённые optional 4A/4B/4D сами по себе не блокируют O5.
Все verdicts независимы: experiment, implementation, public activation, next epoch,
series complete. Зелёные tests, disabled default и zero-DTB не заменяют их.
