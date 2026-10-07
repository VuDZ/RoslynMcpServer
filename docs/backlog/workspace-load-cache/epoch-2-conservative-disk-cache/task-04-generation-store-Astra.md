# E2/task-04 — generation-store

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Ошибка atomic pointer/lease/cleanup может удалить active generation или опубликовать неполные данные.
- Необходимые способности: `архитектурное_мышление`, `анализ_конкурентности`, `управление_ресурсами`, `проверка_целостности_данных`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E2/task-00](task-00-cache-protocol-Astra.md), [E2/task-02](task-02-payload-serialization-Luna.md).

## Цель

Реализовать immutable generation store и безопасное multiprocess ownership/cleanup.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Новый evaluation cache namespace/store; analyzer shadow store не менять. Codec из task-02, lifetime contract из task-00.

## Конкретный результат

Complete immutable generations, atomic current pointer, competing writer protocol, reader lease до last lazy read, bounded cleanup и fallback при corruption/permissions/no-space. Crash/pause/PID reuse не теряют active generation.

## Проверки и evidence

V17/V18 с независимыми процессами и fault injection: незавершённая запись, competing publish, active/paused reader, stale ownership, corruption/oversize и failed cleanup.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не считать PID единственным доказательством owner lifetime, не удалять active reader generation и не объявлять green store public activation.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
