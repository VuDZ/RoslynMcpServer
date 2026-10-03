# E4/task-03 — validation-policy-contract

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Ошибка producer-reader matrix способна выдать weak evidence за strict validation.
- Необходимые способности: `архитектурное_мышление`, `анализ_зависимостей`, `проверка_целостности_данных`, `согласованность_компонентов`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-00](task-00-direction-gates-Astra.md).

## Цель

Определить producer evidence → reader policy для 4B без изменения live read policy.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Cache schema/evidence из Epoch 2, strict validator и выбранный Epoch 3 watcher contract; отдельный design.

## Конкретный результат

Compatibility matrix strict/stat/watcher, effective guarantees и явно отличимые outcomes. Strict reader слабого payload делает full validation либо miss. Schema/namespace меняется лишь при несовместимой форме evidence.

## Проверки и evidence

V10/V15/V23: строгий reader после weak producer, bytes change с теми же size/mtime, watcher unavailable и explicit fallback; описать required oracle для каждого режима.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не смешивать admission и freshness mode, не понижать strict молча и не переносить weak disk mode на semantic reads.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Пакеты потребителей

До accepted заполнить exact types/files/signatures по [общим правилам](../task-execution.md).
Producer выдаёт ограниченный пакет каждому потребителю; незаполненный packet
не переносится как design-работа на Sol/Luna исполнителя.

- E4/task-04: `epoch-4-measured-optimizations/design/implementation-packet-task-04.md`.
