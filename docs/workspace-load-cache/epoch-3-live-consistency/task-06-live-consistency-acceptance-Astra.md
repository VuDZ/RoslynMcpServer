# E3/task-06 — live-consistency-acceptance

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Ложная live приёмка может выдать непроверенную O4 гарантию или ослабить write preflight.
- Необходимые способности: `независимая_проверка`, `анализ_конкурентности`, `семантика_roslyn_msbuild`, `согласованность_компонентов`.
- Роль: **independent-acceptance**.
- Session constraint: отдельная fresh session; не автор ни одного design-документа этой эпохи и не автор проверяемой реализации.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E3/task-05](task-05-flush-publication-concurrency-Astra.md).

## Цель

Принять O4 в scope выбранной watcher policy и зафиксировать её практические ограничения.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Полный diff, shared input-state contract, V07–V09/V19–V23 evidence и handoff-template; independent review/coordinator validation.

## Конкретный результат

handoff.md с read/write/failure cases, accepted quiet-loss limitations, shared WPF/build freshness regression results, пятью verdicts и незакрытым O5/U-ARB-03. Performance часть deferred до budget gate.

## Проверки и evidence

Общая final validation, SourceStructure и затронутый AnalyzerLifecycle. Recheck existing semantic immediately after load, delivered edits, graph stale, overflow/unavailable и writes against wrong base.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не усиливать policy молча до строгой проверки; не приравнивать coverage unknown к неверной published generation и не объявлять series complete без O5.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Независимость и ограничения исполнителя

До запуска записать review session ID и design/implementation author session IDs
проверяемого scope; совпадение запрещает dispatch независимо от модели Astra.
Приёмщик не меняет production implementation или design, который принимает.
Must-fix возвращается соответствующему автору; повторный review остаётся у этой
приёмочной сессии. Координатор выполняет final validation и фиксирует verdicts.
