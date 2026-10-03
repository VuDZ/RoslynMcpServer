# E2/task-08 — cache-checkpoint-acceptance

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Ложная checkpoint приёмка может разрешить следующий этап без корректного cross-process reuse.
- Необходимые способности: `независимая_проверка`, `анализ_зависимостей`, `семантика_roslyn_msbuild`, `анализ_конкурентности`.
- Роль: **independent-acceptance**.
- Session constraint: отдельная fresh session; не автор ни одного design-документа этой эпохи и не автор проверяемой реализации.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E2/task-06](task-06-fault-and-restart-matrix-Sol.md), [E2/task-07](task-07-attempt-observability-Luna.md).

## Цель

Принять unchanged checkpoint и отдельно оценить store, reuse и activation.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Полный diff, fresh MSBuild oracle, V01–V18/V23 reports и handoff-template; independent review и coordinator validation.

## Конкретный результат

handoff.md с пятью verdicts, completeness/zero-DTB proof, store lifecycle и ограничениями. U-ARB-03 остаётся deferred до итоговой реализации; series complete=false, O4/O5 не закрыты.

## Проверки и evidence

Общая final validation, SourceStructure и AnalyzerLifecycle; подтвердить cross-process positive, generated outputs, capability/writes и ordinary fallback.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не снимать opt-in/default=false и не разрешать activation без заранее согласованных workload/budgets; next epoch verdict не подменяет activation.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Независимость и ограничения исполнителя

До запуска записать review session ID и design/implementation author session IDs
проверяемого scope; совпадение запрещает dispatch независимо от модели Astra.
Приёмщик не меняет production implementation или design, который принимает.
Must-fix возвращается соответствующему автору; повторный review остаётся у этой
приёмочной сессии. Координатор выполняет final validation и фиксирует verdicts.
