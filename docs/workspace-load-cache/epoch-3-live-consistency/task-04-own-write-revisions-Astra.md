# E3/task-04 — own-write-revisions

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Ошибочное own-write подтверждение способно поглотить более позднюю внешнюю правку.
- Необходимые способности: `архитектурное_мышление`, `анализ_конкурентности`, `проверка_целостности_данных`, `согласованность_компонентов`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E3/task-03](task-03-live-reconciliation-Sol.md), [E1/task-04](../epoch-1-workspace-lifecycle/task-04-write-capabilities-Sol.md).

## Цель

Совместить persistence acknowledgements с pending events без потери внешних правок.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

WorkspaceFilePersistence/write notifications/shared input revisions и reconciliation; изменения manager последовательно после task-03.

## Конкретный результат

Own write содержит actual saved bytes/BOM/hash/revision и только successful paths при partial result. Matching bytes не добавляют revision; later/mismatch/ABA/unstable cases сохраняют pending/untrusted по contract.

## Проверки и evidence

V20/V21 с controllable event interleavings: own write + external overwrite, event newer than own revision, partial failure, duplicate delivery и repeated non-consuming pull. csproj не меняется от sync.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не использовать time-window suppression и не трактовать совпавший hash как доказательство автора записи.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
