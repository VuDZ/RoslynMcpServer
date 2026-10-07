# E2/task-02 — payload-serialization

- Рекомендуемая модель: **Luna**.
- Рекомендуемый reasoning: **medium**.
- Примерная сложность: **2/5**.
- Риск ошибки: **высокий** — Ошибка bounds/checksum обработки допускает некорректный payload или неограниченную allocation.
- Необходимые способности: `реализация_по_контракту`, `проверка_целостности_данных`, `проектирование_проверок`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E2/task-00](task-00-cache-protocol-Astra.md), [E1/task-01](../epoch-1-workspace-lifecycle/task-01-semantic-dto-Sol.md).

## Цель

Сделать bounded DTO serialization/checksum по фиксированной schema.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Только payload codec и focused tests; schema/limits/checksum contract из task-00, semantic DTO Epoch 1. Не реализовать locks, store или admission.

## Пакет реализации до dispatch

- Producer: [E2/task-00](task-00-cache-protocol-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-02.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Payload codec/checksum и DTO validation error types из task-00; новые semantic DTO или store ownership types не вводить.

### Файлы, которые можно менять

- Только новые payload codec files и bounded serialization tests из packet; DTO schema files read-only.

### Файлы, которые нельзя менять

- Services/Workspace/SolutionManager.cs
- Services/Analyzers/
- Services/Models/WorkspaceInputSession.cs
- Tools/
- Generation store files task-04
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Exact Encode/Decode declarations, checksum algorithm, version/count/size bounds, truncation/invalid-value outcomes — packet task-00.
- Semantic DTO declarations task E1/01 фиксируются input schema; return types не включают Roslyn object graph или source/generated texts.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Round-trip согласованных полей, schema mismatch, size/count bounds до allocations, checksum mismatch и malformed/truncated input outcomes. Portable paths/identities без process IDs и source/generated texts.

## Проверки и evidence

V15/V18 codec subset: oversized lengths/counts, invalid values, truncation, incompatible producer; tests сверяют contract outcomes.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не менять schema/limits по своему усмотрению и не использовать произвольную десериализацию runtime objects.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
