# E4/task-15 — symbol-index-acceptance

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **средний** — Неполная 4D приёмка пропустит unverified hints или недоказанное ускорение symbol search.
- Необходимые способности: `независимая_проверка`, `семантика_roslyn_msbuild`, `проверка_целостности_данных`, `измерение_производительности`.
- Роль: **independent-acceptance**.
- Session constraint: отдельная fresh session; не автор ни одного design-документа этой эпохи и не автор проверяемой реализации.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-10](task-10-symbol-index-implementation-Sol.md).

## Цель

Независимо принять реализацию 4D после принятого task-10.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md), [общие правила исполнения](../task-execution.md),
принятый design, implementation packet и полный diff/evidence ветки обязательны.
Design/bottleneck/budget evidence — task-09; index implementation и Roslyn verification — task-10.
Исполнитель меняет только отчёт приёмки и статус ветки. Must-fix передаётся автору;
production исправления в этой сессии не выполняются.

## Конкретный результат

handoff-4d.md и статус только 4D в README. Отдельно указать пять verdicts, limits, not-run/deferred checks и
performance evidence. Невыполненные другие optional ветки не являются зависимостями.

## Проверки и evidence

V26: membership/TFM/defines/hash identity, invalid/miss/deleted fallback, current Roslyn verifies every candidate, generated symbols excluded; bottleneck/performance evidence отдельно. Финальную code validation выполняет координатор по общим правилам.
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
