# E2/task-00 — cache-protocol

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Неполный cache/store protocol способен разрешить неверный hit или небезопасный cleanup.
- Необходимые способности: `архитектурное_мышление`, `анализ_зависимостей`, `анализ_конкурентности`, `управление_ресурсами`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E1/task-07](../epoch-1-workspace-lifecycle/task-07-lifecycle-acceptance-Astra.md).

## Цель

Зафиксировать contracts disk generation, strict validation, reader ownership и capture integration.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Cache-contract, spec, production lifecycle и текущие load responses; документ решения и точные интерфейсы downstream задач.

## Конкретный результат

Versioned schema, request identity, completeness predicate, detector outcomes, limits/checksum format, ownership/cleanup протокол и fallback/observability vocabulary. Показать RAM/disk/forceReload ветки и capture до ответа.

## Проверки и evidence

Trace V10–V18/V23 на каждый переход; проверить no partial hit, bytes consumed vs validated, lazy reader lifetime, competing writers и cancel/reset/shutdown.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не ослаблять unknown → whole ordinary load, не менять useDiskCache=false default, не добавлять второй внутренний флаг или background capture после ответа.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Пакеты потребителей

До accepted заполнить exact types/files/signatures по [общим правилам](../task-execution.md).
Producer выдаёт ограниченный пакет каждому потребителю; незаполненный packet
не переносится как design-работа на Sol/Luna исполнителя.

- E2/task-01: `epoch-2-conservative-disk-cache/design/implementation-packet-task-01.md`.
- E2/task-02: `epoch-2-conservative-disk-cache/design/implementation-packet-task-02.md`.
- E2/task-03: `epoch-2-conservative-disk-cache/design/implementation-packet-task-03.md`.
- E2/task-06: `epoch-2-conservative-disk-cache/design/implementation-packet-task-06.md`.
- E2/task-07: `epoch-2-conservative-disk-cache/design/implementation-packet-task-07.md`.
