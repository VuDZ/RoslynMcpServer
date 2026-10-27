# D/E0/task-07.3 — Core-first: проверка на графе зависимостей загрузки MSBuild

- Дата постановки: 2026-10-21.
- Рекомендуемые модели: GPT-6.1 Sol; альтернатива: Claude Opus 5.5.
- Рекомендуемый reasoning: GPT: high.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — compilation references не равны load dependencies; бесплатный граф из уже выполненного baseline может скрыть цену первой загрузки.
- Необходимые способности: `семантика_roslyn_msbuild`, `проектирование_проверок`, `анализ_производительности`.
- Статус: **cancelled, 2026-10-27 (решение владельца) — superseded [исследованием форка BuildHost](../research-buildhost-fork/README.md)**.
  Порядок загрузки и evaluation наперёд теперь делает сам host (A + B + C, task-02–task-04 форка):
  на Roslyn.slnx и офисном решении загрузка identical и быстрее без параллельных `MSBuildProjectLoader`.
  Реализация не начиналась; план ниже сохранён как история. Прежний статус: planned, 2026-10-21.
- Depends on: [task-07.2](task-07.2-refusal-fast-planner-roslyn-hi.md).
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Вопрос

Сохраняет ли core-first выигрыш на полном Roslyn, если разбиение строится по
достоверному графу **зависимостей загрузки**, а не по буквальным ProjectReference
в XML? Сначала установить корректность одного N=4; только затем измерять скорость.
Это следующий standalone эксперимент, не разрешение production или E1.

## Основания

[Task-07.2](evidence/task-07.2-refusal-fast-planner-roslyn.md) получила на PowerToys
корректные 23,306 с против baseline 41,536 с. На Roslyn N=4 — 422 variants вместо
425, 12 plan violations и 66 unexplained differences на попытку. Ordinary fallback
совпадает с baseline, но не делает параллельную попытку успешной.

Literal XML graph пропускает references из CompilersPackage.targets и
CoreComponents.props, не раскрывает свойства и не проверяет условия. Условное
ребро на RazorDeployment исключает его из roots, хотя сама загрузка может этого
ребра не обходить. Точное evaluated значение того условия пока не установлено.
Дополнительные GetTargetPath diagnostics также не сведены к одной доказанной
причине; получение графа само по себе не гарантирует устранения всех 66 различий.

Существующий snapshot-load добавляет к compilation references лишь буквальные
ROA=false references; полноту imported/dynamic dependencies он не доказывает.

## Границы

- Только standalone `spikes/ParallelLoad`, его проверки и evidence. Production
  load/prepare/publication, Version, CHANGELOG и публичные параметры не менять.
- Публичные API Roslyn/MSBuild; без reflection во внутренний loader.
- Comparator и SolutionObservation сохранить без изменений. Не исправлять
  projects, properties или diagnostics после загрузки.
- Сохранить SolutionDir, исходные native/unloadable запросы фазы 1, отдельную
  ProjectMap.Create(coreSolution) на группу и защиту общего каталога/obj.
- Прежние task-07/task-07.1/task-07.2, их результаты, правила отказа и evidence
  не переписывать. N=8, ускорение поиска и средства против прогрева не входят
  в эту задачу. Ограничение детерминизма deadline-limited N8 остаётся открытым.
- Антивирусы и cache policy не перенастраивать. Перед C# правками прочитать
  полный [code style](../../code-style.md) и [AGENTS.md](../../../AGENTS.md).

## Последовательность

### 1. Получить и проверить load graph

Использовать evaluation/наблюдения MSBuild в том же SDK и контексте загрузки:
imports, Conditions, раскрытые свойства, ROA=false, TFM и effective properties.
Выбор конкретного публичного механизма — часть эксперимента; обычный ProjectGraph
или список Solution.ProjectReferences заранее не объявляются полным load graph.

Сохранить для рёбер источник и effective context. При свёртке вариантов до пути
для ownership учитывать зависимости всех загружаемых вариантов. Каждый проект
из исходного solution должен оставаться явно покрытым запросами; отсутствие
активного входящего ребра не должно терять solution entry. Для native/unloadable
entries сохранить прежний режим запроса и diagnostics.

Для первой диагностической проверки допустимы уже сохранённые baseline/binlog
этой серии как oracle. Пометить их стоимость отдельно: это проверка возможности
разбиения, а не доказательство ускорения первой загрузки. Если полноту графа
публичным механизмом установить нельзя, зафиксировать границу и остановиться
с отрицательным результатом; не заменять её набором исключений для путей Roslyn.

### 2. Один Roslyn N=4 — сначала корректность

Полный Roslyn.slnx, commit 013d3a758df6c137497ff37a93f0d4bed103853a,
локальный SDK 10.0.301, Configuration=Debug без Platform/TargetFramework,
restore и регистрация как в task-07.2.

Сохранить graph/plan hashes и запустить одну проверку N=4. Сравнить actual
parallel snapshot и ownership с обычной загрузкой тем же comparator.
Требуются ноль plan violations, unexplained differences, group errors и timeouts;
состав вариантов/ссылок/diagnostics должен совпасть. Проверять attempted snapshot
отдельно от любого ordinary fallback. Отдельно проверить запросы отсутствовавших
трёх variants и imported references, вызвавшие повторное владение.

Пока этот контроль не проходит, расширенную матрицу и оптимизацию скорости не
выполнять. Неуспешный контроль сохранить с причиной и результатом разбирательства.

### 3. При успехе — цена получения графа и полный wall

Сначала выбрать и описать способ получения графа **до** параллельной загрузки.
Если graph доступен лишь после полной ordinary load, такой oracle не является
готовым способом ускорения первого открытия. Разделить результаты для первой
загрузки и повторного использования ранее полученного графа; второй случай не
объявлять решённым без проверки актуальности входов.

Для доступного способа — новый процесс на каждый запуск, warmup + n=3:
Roslyn baseline и N=4, PowerToys baseline и N=4, Files baseline и прежний N=2
pre-load refusal как регрессия. Сохранить SDK/globals и условия сравниваемой серии.

Wall включает graph capture/validation, planning, фазу 1, группы, merge и fallback,
если он потребовался. Опубликовать цену графа, стоимость/долю ядра, wall ratio,
BuildHost peak, решения отказа и раздельную эквивалентность attempt/fallback.
Предыдущие модель и пороги task-07.2 не подгонять по этим результатам.

Если правильный граф увеличил ядро или его получение съело экономию, это
содержательный no-go. Показать числа и оставить ordinary load допустимым исходом,
не продолжая поиск оптимизаций в этой же задаче.

## Проверки и результат

До загрузок — небольшие regression checks: imported reference, true/false
Condition, property-generated path, ROA=false, TFM contexts, rootless cycle,
сохранение всех solution entries и directory ownership. Повторно проверить
прежние core-first/refusal/merge/profile self-checks.

После изменений — MCP Release build spike и решения, полный main
Category!=AnalyzerLifecycle по AGENTS.md; SourceStructure входит в обязательный
контроль. AnalyzerLifecycle дополнительно нужен только при изменении production
или lifecycle paths/config; такой scope в этой задаче не запланирован.
Нулевые тестовые запуски и неуспешные загрузки не считаются успехом.

Результат — `evidence/task-07.3-evaluated-load-graph.md`: источник/границы полноты
графа, первая проверка корректности, условная матрица, цена графа и ядра, verdict.
Если этап остановлен, указать точную причину и какие этапы не выполнялись.
Тяжёлые snapshots/binlogs/TRX — в ignored
`artifacts/parallel-load/task-07.3-evidence/`; в Git — компактные evidence по
[правилам хранения](../README.md#хранение-evidence).

## Продолжение направления, 2026-10-22

Задача остаётся planned и сохраняет public-API scope. По решению владельца
создано отдельное [исследование форка BuildHost](../research-buildhost-fork/README.md):
оно может дать наблюдения о реальном графе и повторной работе, но не считается
исполнением или успешной приёмкой task-07.3. Прежний no-go task-07.2 сохраняется.
