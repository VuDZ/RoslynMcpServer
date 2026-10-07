# E1/task-04 — write-capabilities

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Ошибка preflight/persistence может изменить disk bytes по неверной базе или скрыть partial write.
- Необходимые способности: `реализация_по_контракту`, `согласованность_компонентов`, `проверка_целостности_данных`, `семантика_roslyn_msbuild`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E1/task-03](task-03-production-hydrate-Sol.md), [E1/task-00](task-00-lifecycle-contract-Astra.md).

## Цель

Реализовать .cs persistence и document capabilities выбранного host с A-WRITE.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

WorkspaceWriteBoundary, WorkspaceFilePersistence и document mutation adapters; production wiring последовательно после task-03.

## Пакет реализации до dispatch

- Producer: [E1/task-00](task-00-lifecycle-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-04.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- .cs document capability/persistence adapter result types по task-00; existing write result/preflight models переиспользовать.

### Файлы, которые можно менять

- Services/Workspace/SolutionManager.cs — agreed write adapter seam, один owner.
- Services/Workspace/WorkspaceWriteBoundary.cs
- Services/Workspace/WorkspaceFilePersistence.cs
- Services/Workspace/WorkspaceWriteResult.cs
- Новые .cs adapter files и focused write/encoding tests по packet.

### Файлы, которые нельзя менять

- Services/Models/WorkspaceInputSession.cs
- Services/Analyzers/InProcessAnalyzerAssemblyLoader.cs
- Services/Workspace/WorkspaceNonCSharpDiskSync.cs
- Tools/
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- SolutionManager.ApplySolutionChangesToDiskAsync(Solution oldSolution, Solution newSolution, CancellationToken cancellationToken = default) → Task<WorkspaceWriteResult>.
- WorkspaceWriteBoundary.Preflight(Solution candidate, Solution workspaceCurrent, WorkspaceWriteOperationContext? operationContext, WorkspaceWriteFreshnessState current, IAnalyzerAssemblyLoader loader, Solution? heldBase = null) → WorkspaceWritePreflight.
- WorkspaceFilePersistence.WriteTextAsync(WorkspaceInputSession? session, string fullPath, string text, Encoding encoding, CancellationToken cancellationToken) → Task.
- Add/remove/rename adapter declarations, saved paths/bytes/revision outcomes и host capability refusal из task-00 packet.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Preflight по published generation, .cs text/add/remove/rename SDK-glob, partial saved paths/bytes/hash/revision; existing project helpers помечают graph stale. External reconciliation не пишет csproj.

## Проверки и evidence

V07: отказ не меняет bytes; explicit Compile без glob unsupported; partial persistence точно перечисляет успехи; следующий semantic call/build; unknown-dirty path оставляет csproj идентичным.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не добавлять csproj writer, multi-file rollback, reload-on-write или запись analyzer/shadow references на диск.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
