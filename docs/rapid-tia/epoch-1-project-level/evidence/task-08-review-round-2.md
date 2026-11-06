# E1/task-08 — независимое ревью, раунд 2

- Дата: **2026-11-06** (Europe/Moscow).
- Рецензент: **Codex**, независимый агент `/root/task08_review_round2`; точный runtime model ID средой не предоставлен.
- База: `14fe3ee`, рабочая ветка `rapid-tia`, замороженные C#/project files версии **1.6.0**. Рассмотрены весь tracked diff и новые файлы task-08; заранее существовавший посторонний `.agents/skills/prepare-github-release/` исключён.
- Scope: публичный `estimate_affected_tests`, disk/MCP → baseline/current → mapper → composer → runtime inventory → VSTest-план → formatter; регистрация core/lite, schema/help/README, minor-версия и доказательства TDD. Перед проверкой C# полностью прочитаны `AGENTS.md` и `docs/code-style.md`; правила применены только к новым и изменённым строкам.
- Норма: полностью прочитана [task-08](../task-08-estimate-tool-hi.md), требования программы R-06/R-13/R-16/R-17/R-18 и разделы поверхности/ответа, [T07-C1 второго рецензента task-07](task-07-review-round-1-claude.md#t07-c1--вход-планировщика-строит-task-08), измеренные runtime-формы task-06. Реализационный отчёт не заменяет чтение кода.
- Вердикт: **ACCEPT по коду и закрытию R1-001/R1-002/R1-003**. Новых подтверждённых молчаливых потерь тестов или неверных ответов в реальном сценарии не найдено. Это второй из максимум трёх раундов по R-18; стоимость, ширина ответа и style/nits не служили основанием нового возврата.
- Это независимая приёмка diff, **не объявление завершения всей задачи**: окончательные build/test/live validation и статусы task/индексов остаются у координатора после фактического выполнения проверок.

## Закрытие трёх замечаний раунда 1

### R1-001 — CLOSED

В [`TestImpactEstimator.DocumentsAsync`](../../../../Services/Testing/TestImpactEstimator.cs), строка **80**, физическое отсутствие файла теперь проверяется **до** получения и сравнения cached syntax tree. Удалённый путь возвращается в `changedPaths`; при Known baseline создаются удаления членов, `DocumentImpact.Current` остаётся `null`. Поэтому `TestImpactComposer.ExpandedProjects` не может принять удаление за безопасную правку теста и передаёт владельца в транзитивное проектное расширение. Старое поведение `WorkspaceDocumentDiskSync`, сохраняющее cached document до reload, больше не стирает эту причину запуска.

Регрессия `Native_deleted_known_source_selects_its_dependent_tests_before_reload` проверяет Known baseline, native `File.Delete`, доставку настоящего watcher event и ответ публичного инструмента **до reload**: зависимый `Specs.csproj` выбран целиком, независимый `Isolated.csproj` отсутствует. Существующий отдельный сценарий после reload сохраняет проверку отсутствия повторного совета reload. Исправление закрывает исходную причину, а не маскирует её общим observation gap.

### R1-002 — CLOSED

В общем [`ProjectImpactMapper.FindTestProjectsAsync`](../../../../Services/Testing/ProjectImpactMapper.cs), строка **129**, к проектам с собственными тестовыми объявлениями присоединяются `ProjectId` из `VstestTestInventory.BuildAsync`. Это выполняется в обычном `MapAsync` до `Compose`, независимо от `RequiresFullScan`. Проект только с конкретным `Derived : Base` теперь участвует в том же транзитивном расширении, что и проект с собственными тестовыми методами.

Два cases `Real_MSBuild_inherited_only_test_project_is_selected_after_base_or_production_edit` реально загружают MSBuild solution с явной `Specs → Library` reference, независимо проверяют наличие `Derived.Inherited` в runtime inventory и отсутствие объявления базы в документах Specs. Затем меняется либо `Base.Inherited`, либо обычный production-член Library; публичный ответ обязан содержать Specs и исключать Isolated. Fixture-коррекция ссылки до валидного RED отдельно раскрыта в evidence первого раунда; неуспешная подготовка не считается доказательством дефекта.

### R1-003 — CLOSED

В [`VstestTestInventory.RuntimeIdentity`](../../../../Services/Testing/VstestTestInventory.cs), строки **98–101**, class attributes собираются по всей `BaseType` chain. До построения обычного имени проверяются NUnit `TestFixture` constructor arguments и `TestFixtureSource`, включая metadata абстрактной базы. Для обоих вариантов возвращаются `RuntimeName=null`, `HasFixtureArguments=true` и явная причина; существующий `VstestRunPlanner` выбирает проект целиком. Прежние неверные `Derived.`/`Derived.Own` больше не строятся.

Два inventory cases и два отдельных public-tool cases проверяют constructor/source варианты раздельно. Это соответствует фактическим именам из RED первого раунда — `DerivedArgument(42).Own` и `DerivedSource(1).Own`: проектный план включает их без угадывания аргументов. Нулевые исполнения прежних ошибочных фильтров из [RED](task-08-review-round-1-red.md) остаются доказательством исходной потери и **не считаются pass**. Повторные живые прогоны исправленного плана выполняет координатор.

## Связанный проверенный путь

- `TestTools.EstimateAffectedTests` сначала вызывает существующий `GetPublishedSolutionAfterDiskSyncAsync`, затем передаёт настоящий baseline, input map и оба текущих stale flags. Все обычные return, отсутствие workspace/map, cancellation и exception проходят через общий `Finish` на строке **95**: предупреждение — первая строка, `project-level result only` присутствует в каждом ответе.
- Сравнение использует тот же стабильный `BaselineSession.Inventory` и тот же builder ключей. Их тела при раскрытии внутренней доступности не изменены. Known/trivia/возврат к baseline не дают изменения; Pending/Unknown явно названы и расширяются. Сохранены отдельные пути удаления до/после reload, новых ещё не загруженных C# файлов, unmapped inputs и graph-file stale.
- `RequiresFullScan` не превращается в доказательство отсутствия не-C# изменений: все обнаруженные текущие тестовые проекты расширяются, постоянная оговорка наблюдения и возможного stale graph остаётся после reload. Обычный новый source не включает независимые проекты; прежняя удалённая `.cs` после reload не вызывает второй reload.
- R-17 получает объединение baseline/current base names, в том числе удалённого наследника. Полный текущий документный inventory передаётся composer; semantic runtime inventory собирает все partial-части и унаследованные source-тесты, включая базу в другом проекте, до утверждения `IsComplete`.
- T07-C1: вложенность использует `+`; унаследованные тесты принадлежат конкретным неабстрактным владельцам; закрытый наследник xUnit/MSTest использует его имя; измеренный NUnit `Int32` имеет закрытую форму. Неизмеренные generic-формы и fixture arguments передаются unresolved с whole-project причиной. Планировщик task-07 и экранирование переиспользованы без изменения алгоритма.
- Summary содержит изменения, план и причины. Details объединяет прямые тесты с текущими тестами whole-project планов и показывает известные runtime identities. Конкретные `Not covered` paths/unknown/gap/stale и compiler errors в изменённых документах передаются formatter.
- `estimate_affected_tests` зарегистрирован read-only в `core` и доступен в `lite`; schema содержит только `includeDetails=false`. `run` и symbol-параметры не публикуются, граф вызовов не обещается. Версии package/assembly/file согласованы: **1.6.0 / 1.6.0.0 / 1.6.0.0**.
- Точные catalog ожидания и README согласованы: full **55 / 46 574**, lite **16 / 20 178**, приращение **767 bytes**; абсолютные бюджеты **50 KiB / 22 KiB** сохранены. Исторический 40%-порог применяется к прежнему набору описаний, новый tool+parameter **525 символов** раскрыт отдельно; абсолютный byte check по-прежнему включает новый инструмент.
- SourceStructure tests и ожидаемые identities/kinds/paths известных **16** limits в diff не менялись и не ослаблялись.

## Доказательства и граница проверки

Рецензент самостоятельно прочитал XML counters и outcomes локальных исходных TRX: `TestResults/task-08-red/red.trx` — **49 executed / 49 failed**; `TestResults/task-08-review-red/review-red.trx` — **7 executed / 7 failed**. В обоих error/timeout/aborted/notExecuted равны нулю. Контекст и ожидаемые причины сохранены в [первоначальном RED](task-08-red.md), [двух поздних pre-review RED → GREEN](task-08-additional-red-green.md) и [RED раунда 1](task-08-review-round-1-red.md). Тесты, добавленные после начального пакета, названы отдельно; исправление не выдаётся за часть первоначальных 49 cases.

[Focused GREEN исполнителя после раунда 1](task-08-review-round-1-green.md): **43 + 15 = 58** новых cases и **123 SourceStructure**, без ослабления исходных assertions. Это evidence исполнителя, не собственный повторный запуск рецензента. По сообщению координатора окончательная Release solution build на 1.6.0 уже PASS с тремя прежними warnings; его повтор 58 cases, SourceStructure, main CI suite, 17 исходных adapter plans, два inherited NUnit plans и owner-project smoke на момент завершения этого ревью ещё выполнялись.

Рецензент **не запускал build/test параллельно координатору**, не менял C#, tests или specification, не использовал Computer Use/IDE UI, не делал commit/push. Окончательные счётчики, результаты живых запусков и статус задачи координатор фиксирует в отдельных final/live validation reports. Промежуточные owner-smoke файлы во время параллельных обратимых adapter writes не использованы как окончательное доказательство чистого восстановления baseline.

## Сохранённые ограничения

Это проектная оценка без call graph: helper edits, неизвестные baseline/runtime identity и gap могут выбирать весь проект. Обход документов, semantic inventory и diagnostics на запросе имеет стоимость; после R1-002 mapper также получает runtime inventory. Простые base names из всего solution могут расширять ответ из-за совпадений имён. Измерены только формы адаптеров из task-06; metadata-only базы не доказывают полный source inventory. Сохраняются известные пределы watcher/input map, включая постоянный gap и T04-C3 после полной пересборки.

По R-18 эти ограничения записаны отдельно и не являются новым MUST-FIX. Частоту gap и полноту на проектах владельца проверяет координатор; ранние либо нулевые прогоны не заменяют финальную validation. Task-09 `run`, MTP и граф следующих эпох не проверялись и не входят в принятый scope.
