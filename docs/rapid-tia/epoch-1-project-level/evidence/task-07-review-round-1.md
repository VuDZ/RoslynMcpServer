# E1/task-07 — независимое ревью, раунд 1

- Дата: **2026-11-06** (Europe/Moscow).
- Рецензент: Codex, независимая сессия `/root/task07_review_round1`; точный runtime model ID средой не предоставлен (`unknown`). Дополнительные агенты не запускались.
- База: `1bd701f`; проверены новые untracked-файлы напрямую, поскольку обычный `git diff` их не показывает. На момент проверки tracked diff относительно базы пуст.
- Вердикт: **ACCEPT** в scope task-07. MUST-FIX: **0**; новых исправлений по ревью task-07: **0**. Перенесённое обязательство T06-C1 закрыто отдельно и новым замечанием task-07 не считается.
- Правило возврата: [R-18 программы](../../README.md#решения-владельца-2026-10-29), только молчаливая потеря тестов или неверный результат в реальном сценарии; максимум три раунда. Подтверждённого такого сценария в проверенном scope нет.
- Этот вердикт не объявляет принятой всю эпоху, публичную активацию или task-08/09. Финальная validation, статусы задачи/индексов и коммит принадлежат координатору.

## Проверенный scope и основания

Прочитаны целиком [task-07](../task-07-run-plan-med.md), включая T06-C1, [README эпохи](../README.md), `AGENTS.md`, `docs/code-style.md`, skill `epoch-subagent-loop` и Ponytail. В программе проверены актуальные R-18 и раздел [«Ответ инструмента»](../../README.md#ответ-инструмента). Прочитаны [основное evidence task-06](task-06-adapter-names.md), его replay-каталог и [второй рецензент task-06](task-06-review-round-1-claude.md); task-06 не переоткрывалась.

Непосредственно проверены:

- [`VstestRunPlanner.cs`](../../../../Services/Testing/VstestRunPlanner.cs);
- [`VstestTestClass.cs`](../../../../Services/Testing/Models/VstestTestClass.cs) и [`VstestProjectRunPlan.cs`](../../../../Services/Testing/Models/VstestProjectRunPlan.cs);
- [`VstestRunPlannerTests.cs`](../../../../RoslynMcpServer.Tests/Testing/VstestRunPlannerTests.cs);
- [отчёт реализации](../task-07-implementation-report.md), [RED](task-07-red.md) и [GREEN](task-07-green.md).

Соединение проверялось по фактическим неизменённым `TestImpactComposer`, `TestImpactResult`, `AffectedTest`, `BaselineMemberKey` и `TestFilterHelper`, а не только по описанию исполнителя. Новых production-callers пока нет: это внутренняя чистая функция с явным входным runnable-инвентарём. Существующий helper, конфигурация, fixture-проекты и каталог task-06 не менялись. Несвязанный `.agents/skills/prepare-github-release/` исключён из scope.

## Результат проверки

В `Plan` множество проектов образуется из расширенных проектов и выбранных тестов. Фильтрация инвентаря и source-ключей выполняется внутри конкретного `ProjectId`; совпадающий ключ другого проекта не закрывает отсутствующую identity. Проектное расширение проверяется первым, сохраняет причины и возвращает `Filter=null` без требования runtime-инвентаря.

Для проекта без расширения проверена цепочка, исключающая молчаливое выпадение выбранного ключа: ключ, отсутствующий во всех подходящих `Tests`, порождает причину и запуск проекта целиком; `RuntimeName=null`/пустое имя либо заданный `UnresolvedReason` в любом выбранном владельце также переводит весь проект на этот путь. Один source-ключ может присутствовать у нескольких concrete owners — каждый известный владелец участвует в фильтре. Проверка не использует source-имя generic или declaring base как guessed runtime-имя.

Свёртка выполняется только при `IsComplete` и выборе всех `testClass.Tests`. При частичном выборе или неполном инвентаре строятся method-термы. Все значения проходят через фактический `TestFilterHelper.EscapeFilterValue`; повторы термов и причин устраняются. Дизъюнкция содержит `FullyQualifiedName~`, и причина прямо сообщает о надмножестве, включая случай `Check`/`CheckExtra`. Бюджет проверяется на итоговой экранированной строке после свёртки: 16000 символов допускаются, превышение даёт проект целиком с причиной.

Шестнадцать replay-cases используют сохранённые четыре адаптера × nested/generic/inherited/parameters: проверяют точный итоговый фильтр и совпадение идентичностей всех выбранных cases каталога, включая оба параметризованных ряда. Остальные 22 cases покрывают причины/дедупликацию, полный и неполный инвентарь, несколько concrete owners, неизвестные/неоднозначные/пропавшие identity, изоляцию проектов, расширение, экранирование, границы бюджета, свёртку до бюджета, пустой выбор и отмену. Избыточность запуска из-за contains и whole-project fallback соответствует принятому контракту и не образует MUST-FIX по R-18.

## T06-C1 — закрыто

В `UnresolvedReasons` `HasFixtureArguments=true` всегда добавляет причину `Runtime fixture constructor/source arguments require the whole project.` до построения фильтра. Тем самым не строятся ни `Class.Method`, ни `Class.` для аргументных NUnit-фикстур; выбран допустимый программой вариант запуска проекта целиком.

Офлайн-регрессия `Fixture_constructor_or_source_arguments_force_a_safe_whole_project_plan` использует фактические FQN второго рецензента `Ns.ArgFixture("a").M`, `Ns.ArgFixture("b").M`, `Ns.SourceFixture(1).S`; проверяет `Filter=null` и причину. Generic constructor case проверен консервативно без утверждения о новом измерении. Отдельный тест сохраняет известную форму `GenericTests<Int32>.Generic` для `[TestFixture(typeof(int))]`: одни generic type arguments не означают fixture constructor arguments.

Контракт DTO явно отличает runtime constructor/source arguments от обычных C# constructor parameters. Безопасное извлечение флага и авторитетных runtime-имён остаётся за task-08; оно не подменено эвристикой в планировщике.

## TDD и validation

Рецензент независимо прочитал существующий ignored `TestResults/task-07-red/red.trx`: `total=executed=failed=38`, `passed=error=timeout=aborted=notExecuted=0`; во всех 38 `ErrorInfo` есть `NotImplementedException`, в том числе inner exception cancellation-теста. Это падение на разрешённой production-заглушке, а не на компиляции, fixture или чтении каталога.

Текущий SHA-256 тестового файла — `25AB0649527040144545C5843921CF209D87EFE6B02CD333B1856090010A7677`, совпадает с зафиксированными RED и GREEN. SHA-256 реализации — `D88731D7CB798744A0DD9ED264164125248A78F67FED7AD6A77CFAEDEF9341F8`, совпадает с GREEN. По записи исполнителя focused GREEN: **38/38**. Поздних изменений тестов относительно RED не обнаружено.

По [финальной validation координатора](task-07-final-validation.md) на том же frozen-коде: Release solution build **PASS** с теми же тремя warnings, focused planner+helper **47/47**, SourceStructure **123/123**, полный Windows main CI suite (`Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`) **1912/1912**. Все прогоны ненулевые, failed=0; точный inventory structural-analysis limits не менялся. После финальных прогонов код, тесты и их SHA-256 не менялись. Этот абзац обновлён по завершённому отчёту без нового раунда ревью; ACCEPT, раунд 1 и 0 новых review fixes сохранены.

Рецензент не запускал конкурирующие сборки, тесты или adapter probes. Проверка была по native read-only инструментам, текущему коду, сохранённым данным и TRX; Computer Use/IDE UI не применялись.

## Сохранённые ограничения

- **T07-L01 — доверие явному инвентарю.** Planner принимает авторитетное соответствие source-ключей конкретным runtime-классам. Он не может обнаружить ложное утверждение caller об известности имени, полноте класса или полном перечислении concrete owners. При неизвестности caller должен передать unresolved identity; извлечение и интеграция принадлежат task-08. Это граница текущего чистого компонента, а не доказанный дефект результата в task-07.
- **T07-L02 — границы измерений task-06 сохранены.** Другие версии адаптеров, nested+generic combinations, generic-методы, custom TestName/DisplayName и MTP не измерялись; основной каталог также не измерял multiple derived classes. Несколько владельцев проверены unit-тестом планировщика, без нового утверждения о discovery. Неизвестные runtime-формы должны оставаться unresolved, пока caller не имеет авторитетной identity.
- **T07-L03 — интеграция и стоимость.** Whole-project fallback может запускать лишние тесты; contains тоже расширяет выбор. Бюджет ограничивает сам фильтр и оставляет запас для будущей команды, но task-07 не строит и не исполняет её. Formatter, runner, task-08/09 и публичная активация этим ревью не проверены. AnalyzerLifecycle отдельно не запускался: его lifecycle/workspace/host/configuration пути не менялись.

Ни одно из этих ограничений не выдано за молчаливую потерю тестов или неверный реальный ответ. MUST-FIX отсутствуют; T06-C1 закрыто, счётчик новых review fixes task-07 остаётся **0**.
