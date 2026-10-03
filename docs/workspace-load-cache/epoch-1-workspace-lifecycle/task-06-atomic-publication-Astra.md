# E1/task-06 — atomic-publication

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Гонка publication/disposal способна потерять старую сессию, события или допустить stale write.
- Необходимые способности: `архитектурное_мышление`, `анализ_конкурентности`, `управление_ресурсами`, `согласованность_компонентов`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E1/task-04](task-04-write-capabilities-Sol.md), [E1/task-05](task-05-overlay-admission-Astra.md).

## Цель

Завершить атомарную publication и lifetime/lock integration общего lifecycle.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

SolutionManager и production accessors; один владелец интеграции после writer/overlay.

## Конкретный результат

Все стадии в одном acquisition, candidate ownership/disposal, retire old после publish, failure/cancel сохраняют pending и previous metadata. Readers не видят candidate; writes перепроверяют base generation.

## Проверки и evidence

V09 и races semantic/edit/reset/load/prepare: same/different key, two owners, cancellation, gate failure, no recursive semaphore; stale write отказ до disk mutation.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не использовать old другого key как ответ на новый request, не очищать новые events старым flush и не вводить MVCC.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
