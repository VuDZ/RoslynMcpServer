# E2/task-07 — attempt-observability

- Рекомендуемая модель: **Luna**.
- Рекомендуемый reasoning: **medium**.
- Примерная сложность: **2/5**.
- Риск ошибки: **средний** — Ошибочные outcome поля могут скрыть fallback или исказить capture/performance evidence.
- Необходимые способности: `реализация_по_контракту`, `диагностика_и_наблюдаемость`, `проверка_целостности_данных`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E2/task-00](task-00-cache-protocol-Astra.md), [E2/task-05](task-05-load-capture-integration-Astra.md).

## Цель

Добавить согласованные observability поля и проверку их заполнения.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Response/log DTO и formatting/focused tests; approved vocabulary task-00 и готовые outcomes task-05, без изменений load branching.

## Пакет реализации до dispatch

- Producer: [E2/task-00](task-00-cache-protocol-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-07.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Attempt observation/response records и formatter fields по accepted vocabulary task-00; lifecycle types не вводить.

### Файлы, которые можно менять

- Tools/WorkspaceTools.cs — только response formatting, public defaults и branching read-only.
- Diagnostics/WorkspaceLoadDiagnosticsReporter.cs
- Services/Workspace/WorkspaceLoadPreparationResult.cs — только agreed observation mapping.
- Новые telemetry formatter/model files и focused outcome tests из packet.

### Файлы, которые нельзя менять

- Services/Workspace/SolutionManager.cs
- Services/Analyzers/
- Services/Models/WorkspaceInputSession.cs
- Validation/store strategy implementations
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Accepted attempt outcome DTO из task-05 → response/log mapping; exact fields/enums/duration units и declarations из task-00 packet.
- Public LoadWorkspace signature read-only; requested/effective/base/capture/write/overlay/readiness/reason contracts не меняются formatter-ом.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Requested/effective policy, base source, validation/capture/write status/reason, overlay readiness, coverage/fallback; counts/bytes и стадийные durations. Все branches имеют различимые результаты.

## Проверки и evidence

V23 observability subset для disabled/RAM/disk/miss/forceReload/reset/false→true/write failure; отсутствие secret properties, source texts и temp shadow paths.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не изобретать новые guarantees, не менять public default и не логировать весь environment/global properties.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
