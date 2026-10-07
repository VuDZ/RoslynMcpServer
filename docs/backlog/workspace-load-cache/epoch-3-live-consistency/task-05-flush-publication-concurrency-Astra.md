# E3/task-05 — flush-publication-concurrency

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Гонка flush/reset/publication может потерять pending события или принять stale candidate.
- Необходимые способности: `архитектурное_мышление`, `анализ_конкурентности`, `управление_ресурсами`, `согласованность_компонентов`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E3/task-04](task-04-own-write-revisions-Astra.md).

## Цель

Закрыть гонки live flush, writes, reset и cache load на общем acquisition.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

SolutionManager/accessors и production synchronization; один integration owner после task-04.

## Конкретный результат

Classify → refresh candidate → validate → prepare/gate → publish под одним acquisition. Immutable accessor и write generation recheck; newer events/cancellation сохраняют pending; disk validity/capture не привязываются к каждому edit.

## Проверки и evidence

V09/V21/V23: simultaneous semantic/edit/reset, graph refresh, stale candidate, event during flush, cancellation, prepare failure, two owners/no recursive acquire; banned/unavailable не обходятся.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не очищать новые events старым flush, не обещать MVCC/merge или автоматически писать новую generation после edit.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
