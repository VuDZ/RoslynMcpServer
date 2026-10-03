# E4/task-10 — symbol-index-implementation

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **medium**.
- Примерная сложность: **3/5**.
- Риск ошибки: **средний** — Ошибка hint identity/delete/fallback может пропустить candidates или вернуть непроверенный результат.
- Необходимые способности: `реализация_по_контракту`, `семантика_roslyn_msbuild`, `проверка_целостности_данных`, `проектирование_проверок`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-09](task-09-symbol-index-contract-Sol.md).

## Цель

Реализовать 4D index как проверяемый hint по принятому design.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Новый index store/query adapters и focused/integration tests; не менять graph admission или generate-symbol contract.

## Пакет реализации до dispatch

- Producer: [E4/task-09](task-09-symbol-index-contract-Sol.md).
- Обязательный output producer: `design/implementation-packet-task-10.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Index identity/entry/storage/query-hint types из task-09; exact names/fields/files уже приняты до начала.

### Файлы, которые можно менять

- Новые index files и focused V26 fixtures из task-09 packet.
- Tools/NavigationTools.cs — только agreed candidate lookup/verification seam.
- Services/Navigation/ — только перечисленные в packet files, не весь каталог.

### Файлы, которые нельзя менять

- Services/Workspace/SolutionManager.cs
- Services/Models/WorkspaceInputSession.cs
- Services/Analyzers/
- Cache dependency/admission/validation files
- Generated-symbol search path
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Exact index lookup/upsert/remove и current-Roslyn candidate verification declarations из task-09 packet.
- SolutionManager.GetPublishedSolutionAsync(CancellationToken cancellationToken = default) → Task<Solution?> — current semantic base; hint не отдельный semantic answer.
- Expected full identity memberships/project/TFM/options/defines/hash/schema и deletion/invalid/miss contracts фиксирует task-09.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Versioned hints с полной identity, актуальная Roslyn verification, удаления и ordinary fallback при invalid/miss. Separate index cost/result telemetry.

## Проверки и evidence

V26 + stale/deleted/project removal/multi-TFM defines; никакой hint не является самостоятельным semantic answer. Сравнить с обычным search в утверждённом workload.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не добавлять generated symbols или считать index identity graph freshness доказательством; результаты принимает task-12 отдельно.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
