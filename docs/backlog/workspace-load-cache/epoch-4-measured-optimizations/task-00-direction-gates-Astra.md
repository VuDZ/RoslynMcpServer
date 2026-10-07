# E4/task-00 — direction-gates

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **medium**.
- Примерная сложность: **4/5**.
- Риск ошибки: **средний** — Смешение design gates направлений ведёт к неверному scope или неоправданной реализации optional ветки.
- Необходимые способности: `архитектурное_мышление`, `анализ_зависимостей`, `измерение_производительности`, `согласованность_компонентов`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E2/task-08](../epoch-2-conservative-disk-cache/task-08-cache-checkpoint-acceptance-Astra.md).

## Цель

Определить доказательства и design gates отдельно для 4A–4D.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Spec, baseline/candidate reports, previous handoffs и verification; план направлений без включения production оптимизаций.

## Конкретный результат

Четыре отдельные карточки scope/oracle/compatibility/experiment/implementation/activation, наличия bottleneck evidence и необходимых измерений. Для O5 указать 4C маршрут; U-ARB-03 performance gate deferred до реализации.

## Проверки и evidence

Каждая ветка имеет независимый target, comparator и budget plan; существующий baseline не выдаётся за доказательство ускорения. Generated/index и weak validation boundaries явны.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не включать все optional ветки автоматически и не блокировать 4C отсутствием symbol-index bottleneck; не утверждать новые budgets за владельца.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Пакеты потребителей

До accepted заполнить exact types/files/signatures по [общим правилам](../task-execution.md).
Producer выдаёт ограниченный пакет каждому потребителю; незаполненный packet
не переносится как design-работа на Sol/Luna исполнителя.

- E4/task-09: `epoch-4-measured-optimizations/design/implementation-packet-task-09.md`.
- E4/task-16: `epoch-4-measured-optimizations/design/implementation-packet-task-16.md`.
