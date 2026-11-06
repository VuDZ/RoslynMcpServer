# E1/task-07 — отчёт реализации

- Дата: **2026-11-06** (Europe/Moscow); база `1bd701f`.
- Исполнитель: Codex, сессия `/root/task07_implementer`; точный runtime model ID неизвестен, средой не предоставлен.
- Статус: **accepted, 2026-11-06**; [ревью — ACCEPT](evidence/task-07-review-round-1.md), [финальная validation](evidence/task-07-final-validation.md). Статусы задачи и индексов обновлены координатором.
- Scope: чистый внутренний VSTest-планировщик «`TestImpactResult` + текущий runnable-инвентарь → планы проектов», две DTO, офлайн unit-тесты, T06-C1. Публичная активация и вся эпоха вне этой приёмки.
- TDD: [RED координатора](evidence/task-07-red.md), [focused GREEN](evidence/task-07-green.md).

## Реализация

[`VstestRunPlanner`](../../../Services/Testing/VstestRunPlanner.cs) принимает composer-результат без изменения его контракта.
Для каждого выбранного или расширенного `ProjectId` возвращает [`VstestProjectRunPlan`](../../../Services/Testing/Models/VstestProjectRunPlan.cs): фильтр или `null` (проект целиком) и причины.
Проектное расширение всегда имеет приоритет и не требует известного runtime-имени. Причины включения тестов и расширения сохраняются и устраняются повторы.

[`VstestTestClass`](../../../Services/Testing/Models/VstestTestClass.cs) — минимальная передаваемая граница с будущей task-08.
Caller передаёт соответствие source `BaselineMemberKey` конкретному adapter/runtime-классу: вложенность через `+`, известные закрытые generic-имена, конкретного владельца inherited-теста.
Один source-метод может соответствовать нескольким конкретным runtime-классам; фильтр включает каждый. Планировщик не извлекает semantic/discovery-информацию и не угадывает runtime-имена по source-типу.
`RuntimeName=null`, заданный `UnresolvedReason` (в том числе неоднозначность) или отсутствие выбранного source-ключа в инвентаре переводят весь соответствующий проект на запуск целиком с явной причиной.

`IsComplete=true` означает доказанную полноту текущего тестового инвентаря конкретного класса.
Только при этом флаге и выборе всех перечисленных тестов используется свёртка `FullyQualifiedName~Namespace.Class.`.
Неполный инвентарь разрешает фильтры методов, но не свёртку. Значения дисъюнкции `FullyQualifiedName~...` экранируются существующим `TestFilterHelper.EscapeFilterValue`; helper и его callers не менялись.
Причина фильтрованного плана прямо называет надмножество (`Check` включает `CheckExtra`).
После свёртки и экранирования проверяется внутренний бюджет **16000 символов** для всего фильтра; превышение даёт проект целиком с причиной. Запас оставлен под путь процесса, проекта и остальные аргументы Windows. Новая публичная настройка отсутствует.

T06-C1 закрыто консервативным выбором проекта целиком при `HasFixtureArguments=true`.
Это metadata о runtime-аргументах NUnit-подобной фикстуры из constructor/source, включая generic-фикстуры с аргументами конструктора; обычные C# constructor parameters сами по себе не означают этот флаг.
Generic type arguments без аргументов конструктора (`[TestFixture(typeof(int))]`) флаг не поднимают и используют известное runtime-имя.
Безопасное извлечение этих metadata вместе с авторитетным concrete runtime-именем принадлежит task-08; неправильное утверждение caller о полноте или известности имени планировщик обнаружить не может.

## Проверки и TDD

Сначала написан [`VstestRunPlannerTests`](../../../RoslynMcpServer.Tests/Testing/VstestRunPlannerTests.cs), затем две DTO и единственная пустая production-сигнатура с `NotImplementedException`.
Координатор проверил реальный Release RED: **38/38 failed**, во всех ошибках `NotImplementedException`, без ошибок компиляции/fixture/catalog и без пропусков/таймаутов.
Только после GO реализовано поведение. Focused GREEN через Roslyn MCP `run_specific_test`, Release с pre-test build: **38/38 passed**, 0 failed.
Предварительная ручная оценка 39 тестов исправлена по фактическому discovery на 38.
После RED тесты не менялись: SHA-256 `25AB0649527040144545C5843921CF209D87EFE6B02CD333B1856090010A7677` совпадает в RED и GREEN; поздних добавлений тестов нет.

Шестнадцать replay-проб проверяют четыре сохранённых адаптера × nested/generic/inherited/parameters, точный фильтр и идентичности всех выбранных cases, включая обе строки.
Данные читаются из существующего `evidence/task-06-adapter-names/catalog.json` через `RepositoryRoot.Find()` без копии каталога и без живого discovery/adapter runner.
Остальные 22 cases проверяют `Check`/`CheckExtra`, объединение/дедупликацию причин и имён, полноту класса, несколько конкретных владельцев source-метода, неизвестные/неоднозначные/пропавшие identity, изоляцию проектов, приоритет расширения, экранирование, точные границы бюджета после экранирования, свёртку до бюджета, пустой набор и отмену.
T06-C1 использует фактические FQN второго рецензента `Ns.ArgFixture("a").M`, `Ns.ArgFixture("b").M`, `Ns.SourceFixture(1).S`; generic constructor case добавлен как консервативная unit-регрессия без утверждения о новом измерении адаптера.

Координатор после freeze завершил Release solution build, focused **47/47**, SourceStructure **123/123**, полный main CI suite **1912/1912**: [финальная validation](evidence/task-07-final-validation.md). Код и тесты после этих прогонов не менялись.
AnalyzerLifecycle дополнительно не требуется: analyzer/shadow-copy/workspace prepare/publication/host пути и конфигурация не менялись. Инвентари structural-analysis limits не менялись.

## Изменённые файлы и границы

Новые production-файлы: `Services/Testing/VstestRunPlanner.cs`, `Services/Testing/Models/VstestTestClass.cs`, `Services/Testing/Models/VstestProjectRunPlan.cs`.
Новый тестовый файл: `RoslynMcpServer.Tests/Testing/VstestRunPlannerTests.cs`. Документы: этот отчёт и evidence RED/GREEN; финальную validation и ревью сохраняет координатор.
Существующие production-файлы, версия, project/configuration/fixtures и каталог task-06 не менялись; новых dependencies нет.
Не реализованы task-08, formatter, execution engine, MTP, discovery или lifecycle-интеграция. Коммит/push, изменение требований и отметка accepted исполнителем не выполнялись. Несвязанный `.agents/skills/prepare-github-release/` сохранён.

Ограничения измерений task-06 сохраняются: другие версии адаптеров, custom runtime/test names и неизмеренные формы должны передаваться как неизвестные, пока caller не располагает авторитетной identity. Whole-project fallback может быть грубым, но не теряет выбранные тесты из-за предположенного FQN.

## Приёмка

- Дата: **2026-11-06** (Europe/Moscow).
- Принятый scope: чистый VSTest-планировщик по проектам, экранирование/свёртка, бюджет 16000 символов и закрытие T06-C1. Task-08/09, formatter, execution engine, MTP и публичная активация вне scope.
- Исполнитель: Codex, сессия `/root/task07_implementer`; рецензент: Codex, независимая сессия `/root/task07_review_round1`. Точные runtime model ID средой не предоставлены.
- [Ревью](evidence/task-07-review-round-1.md): **1 раунд, 0 новых исправлений после ревью task-07**, ACCEPT; T06-C1 закрыто как обязательное условие первоначальной реализации. Ограничения T07-L01–L03 сохранены.
- [Validation координатора](evidence/task-07-final-validation.md): Release build PASS, focused **47/47**, SourceStructure **123/123**, main **1912/1912**. Начальный тестовый файл совпадает с RED по SHA-256; поздних тестов нет.
- Следующий caller task-08 должен соблюдать документированные входные контракты полного/авторитетного runtime-инвентаря и fixture metadata. Эта приёмка не объявляет такую интеграцию реализованной.
- Версия сохранена; Linux/WSL, MTP и новый живой adapter discovery не запускались.
- [Второй рецензент раунда 1 — ACCEPT](evidence/task-07-review-round-1-claude.md): Linux main **1904/1904**; на реальных адаптерах `TestName`/`DisplayName` и generic-методы фильтр метода не ломают. Перевод имён по норме task-06 нигде не реализован; условие T07-C1 передано в task-08.
