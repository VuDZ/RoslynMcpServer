# E0/task-03 — oracle-fixtures

- Рекомендуемая модель: **Luna**.
- Рекомендуемый reasoning: **medium**.
- Примерная сложность: **2/5**.
- Риск ошибки: **средний** — Ошибочные fixture expectations могут оставить semantic/admission regression незамеченной.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`, `проверка_целостности_данных`.
- Статус и маршрут: [карта эпохи](README.md).
- Статус исполнения: **accepted — isolated fixtures, negative expectations only
  (2026-10-04)**. Принятый scope: 13 disk layouts manifest, пять fixture/model/test
  файлов, независимо наблюдённые goldens, восстановление байтов/owned outputs,
  детерминированный cleanup-failure witness и task/index/evidence bookkeeping.
  Все R03-01..R03-06 закрыты; координатор завершил две стилевые правки перед
  commit. Final Release build exit 0, focused **48/48**, main
  `Category!=AnalyzerLifecycle` **1180/1180 passed**, без failures/skips.
  [Финальная validation и приёмка](evidence/task-03-fixtures.md#финальная-coordinator-validation-и-приёмка-2026-10-04).
  Fixture-зависимость task-03 для task-04 закрыта; положительные
  admission/equivalence остаются hold по task-02. Task-04 не выполнялся;
  приёмка эпохи и public activation не разрешены этим результатом.
- Depends on: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md), [E0/task-01](task-01-hydrate-host-spike-Sol.md), [E0/task-02](task-02-dependency-admission-spike-Astra.md).

## Цель

Создать ограниченные фикстуры и ожидаемые данные по уже принятому плану oracle.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Только новые fixtures/test data и их builders; план из task-00 и результаты task-01/task-02 обязательны.

## Пакет реализации до dispatch

- Producer: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-03.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Только fixture builders/expected-data types из packet; новых production types нет.

### Файлы, которые можно менять

- Новые SDK fixture project/source/additional/config data files и test builders по конечному manifest packet.

### Файлы, которые нельзя менять

- Services/
- Tools/
- Diagnostics/
- RoslynMcpServer.csproj
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Fixture constructors/paths/expected values и oracle runner declaration из packet task-00, обновлённые по принятым результатам task-01/task-02.
- Обязательные expected outcomes: linked all-memberships, inner-TFM defines, decoding bytes/BOM, generator full output, absent import/custom Target admission.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Малые SDK проекты: linked .cs в двух memberships, два inner TFM с разными defines, UTF-8 BOM/non-ASCII/другая согласованная encoding, generator marker + AdditionalFile/editorconfig, absent import и запрещённый custom Target. Явные expected characters/bytes/options и admission outcomes.

## Проверки и evidence

Проверить восстановление исходных fixture bytes после mutation; fresh MSBuild реально выдаёт ожидаемый generator marker. Unknown/rejected случаи отмечать negative admission.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не проектировать общий oracle или detector; если expected outcomes не определены предшественниками, вернуть конкретный пробел в task-00.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
