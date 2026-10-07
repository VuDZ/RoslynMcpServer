# E4/task-04 — validation-modes

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Silent downgrade validation может разрешить reuse без обещанной проверки входов.
- Необходимые способности: `реализация_по_контракту`, `анализ_зависимостей`, `проверка_целостности_данных`, `диагностика_и_наблюдаемость`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-03](task-03-validation-policy-contract-Astra.md), [E2/task-08](../epoch-2-conservative-disk-cache/task-08-cache-checkpoint-acceptance-Astra.md).

## Цель

Реализовать 4B compatibility и distinct observability по принятой matrix.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Validation strategy/codec compatibility/response helpers; cache integration через agreed interfaces, без rewrite watcher policy.

## Пакет реализации до dispatch

- Producer: [E4/task-03](task-03-validation-policy-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-04.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Validation strategy/policy compatibility result types из task-03; profile admission grammar и live states неизменны.

### Файлы, которые можно менять

- Validation strategy/producer-evidence compatibility files Epoch 2 — точный manifest task-03.
- Observation mode mapping и focused V10/V15/V23 tests из packet.

### Файлы, которые нельзя менять

- Services/Workspace/SolutionManager.cs
- Services/Models/WorkspaceInputSession.cs
- Services/Workspace/WorkspaceDiskEventClassifier.cs
- Services/Analyzers/
- Tools/NavigationTools.cs
- Strict schema files вне accepted versioning delta
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Exact strict validator/codec declarations Epoch 2 + requested/effective validation strategy API из task-03.
- Producer evidence → reader policy matrix включает explicit full-check/miss и error outcomes; schema namespace/version changes перечислены producer-ом, не implementation guess.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Explicit requested/effective modes, stricter reader full check/miss, versioning только при необходимости и понятные fallback reasons. Сохранить independent semantic admission.

## Проверки и evidence

V10/V15/V23 и prior strict regression matrix; same-size/mtime collision ловится strict. Weak mode evidence не проходит как strict success.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не принимать weak payload по одному schema match; результаты этой ветки принимает task-12 отдельно.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
