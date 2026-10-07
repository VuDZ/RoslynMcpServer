# E4/task-14 — validation-modes-acceptance

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Ложная 4B приёмка может подтвердить strict гарантию для weak payload.
- Необходимые способности: `независимая_проверка`, `анализ_зависимостей`, `проверка_целостности_данных`, `согласованность_компонентов`.
- Роль: **independent-acceptance**.
- Session constraint: отдельная fresh session; не автор ни одного design-документа этой эпохи и не автор проверяемой реализации.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-04](task-04-validation-modes-Sol.md).

## Цель

Независимо принять реализацию 4B после принятого task-04.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md), [общие правила исполнения](../task-execution.md),
принятый design, implementation packet и полный diff/evidence ветки обязательны.
Design — task-03; producer → reader compatibility matrix и strict regressions — task-04.
Исполнитель меняет только отчёт приёмки и статус ветки. Must-fix передаётся автору;
production исправления в этой сессии не выполняются.

## Конкретный результат

handoff-4b.md и статус только 4B в README. Отдельно указать пять verdicts, limits, not-run/deferred checks и
performance evidence. Невыполненные другие optional ветки не являются зависимостями.

## Проверки и evidence

V10/V15/V23: strict reader weak payload, collision по size/mtime, full-check/miss вместо silent downgrade; admission/live read policy не ослаблены. Финальную code validation выполняет координатор по общим правилам.
Проверить independently, что результат реализован, а не только описан в design.

## Недопустимые упрощения

Не принимать собственный design/implementation, не переносить go между ветками
и не считать handoff разрешением activation без workload/budget gate.

## Независимость и ограничения исполнителя

До запуска записать review session ID и design/implementation author session IDs
проверяемого scope; совпадение запрещает dispatch независимо от модели Astra.
Приёмщик не меняет production implementation или design, который принимает.
Must-fix возвращается соответствующему автору; повторный review остаётся у этой
приёмочной сессии. Координатор выполняет final validation и фиксирует verdicts.
