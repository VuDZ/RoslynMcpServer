# E4/task-11 — o5-reuse-acceptance

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Ложная приёмка closed subset ошибочно закроет обязательный O5.
- Необходимые способности: `независимая_проверка`, `анализ_зависимостей`, `семантика_roslyn_msbuild`, `анализ_конкурентности`.
- Роль: **independent-acceptance**.
- Session constraint: отдельная fresh session; не автор ни одного design-документа этой эпохи и не автор проверяемой реализации.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-08](task-08-reuse-oracle-matrix-Sol.md).

## Цель

Независимо принять техническое закрытие O5 без зависимости от optional 4A/4B/4D.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Diff/evidence 4C, fresh oracle и handoff-template; independent review и coordinator final validation.

## Конкретный результат

handoff-4c.md с exact subset, limitations, separate content/membership outcomes, DTB evidence, capabilities/admission и пятью verdicts. До task-13 public activation/performance остаются неразрешёнными.

## Проверки и evidence

Full applicable equivalence/admission matrix, SourceStructure, Release build/main CI и AnalyzerLifecycle. Unknown fallback и transitive/TFM correctness сверить по текущему code.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не закрывать series complete по O5 fixtures и не менять бюджет или subset во время review; исправления вернуть в task-05/06/07/08.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Независимость и ограничения исполнителя

До запуска записать review session ID и design/implementation author session IDs
проверяемого scope; совпадение запрещает dispatch независимо от модели Astra.
Приёмщик не меняет production implementation или design, который принимает.
Must-fix возвращается соответствующему автору; повторный review остаётся у этой
приёмочной сессии. Координатор выполняет final validation и фиксирует verdicts.
