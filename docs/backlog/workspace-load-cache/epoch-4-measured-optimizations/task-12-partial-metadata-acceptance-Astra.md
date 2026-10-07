# E4/task-12 — partial-metadata-acceptance

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Ошибочная 4A приёмка может разрешить неполные semantic/refactoring ответы без честной coverage.
- Необходимые способности: `независимая_проверка`, `семантика_roslyn_msbuild`, `согласованность_компонентов`, `анализ_зависимостей`.
- Роль: **independent-acceptance**.
- Session constraint: отдельная fresh session; не автор ни одного design-документа этой эпохи и не автор проверяемой реализации.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-02](task-02-partial-metadata-implementation-Sol.md).

## Цель

Независимо принять реализацию 4A после принятого task-02.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md), [общие правила исполнения](../task-execution.md),
принятый design, implementation packet и полный diff/evidence ветки обязательны.
Design — task-01; full-source comparator и V24/V25 результаты — task-02.
Исполнитель меняет только отчёт приёмки и статус ветки. Must-fix передаётся автору;
production исправления в этой сессии не выполняются.

## Конкретный результат

handoff-4a.md и статус только 4A в README. Отдельно указать пять verdicts, limits, not-run/deferred checks и
performance evidence. Невыполненные другие optional ветки не являются зависимостями.

## Проверки и evidence

V24/V24a/V24b/V25: scope/key separation, explicit expand, tool coverage/refusal, CLI disk target, ambiguity, stale/corrupt metadata fallback. Финальную code validation выполняет координатор по общим правилам.
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
