# E1/task-07 — lifecycle-acceptance

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Ложная приёмка lifecycle переносит ошибки publication и writes в disk cache.
- Необходимые способности: `независимая_проверка`, `семантика_roslyn_msbuild`, `анализ_конкурентности`, `согласованность_компонентов`.
- Роль: **independent-acceptance**.
- Session constraint: отдельная fresh session; не автор ни одного design-документа этой эпохи и не автор проверяемой реализации.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E1/task-06](task-06-atomic-publication-Astra.md).

## Цель

Принять общий production lifecycle и выдать handoff для disk cache.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Полный diff/evidence эпохи, epoch-0 oracle и handoff-template; independent review плюс coordinator validation.

## Конкретный результат

handoff.md с V01–V09 для ordinary и production hydrate, semantic/dependency data tables, capability matrix, transitions и limits. Пять verdicts; disk lookup ещё отсутствует.

## Проверки и evidence

Общая Release build/main CI validation, SourceStructure после production C#, AnalyzerLifecycle для load/prepare/publication. Проверить реальные bytes, build, next semantic и shadow refs.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не принимать test-only integration, не закрывать dependency evidence равенством documents и не разрешать public activation по lifecycle тестам.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Независимость и ограничения исполнителя

До запуска записать review session ID и design/implementation author session IDs
проверяемого scope; совпадение запрещает dispatch независимо от модели Astra.
Приёмщик не меняет production implementation или design, который принимает.
Must-fix возвращается соответствующему автору; повторный review остаётся у этой
приёмочной сессии. Координатор выполняет final validation и фиксирует verdicts.
