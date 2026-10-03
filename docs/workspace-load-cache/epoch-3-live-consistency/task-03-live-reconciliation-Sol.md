# E3/task-03 — live-reconciliation

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Ошибка reconciliation может обновить не все memberships или изменить project/additional bytes на диске.
- Необходимые способности: `реализация_по_контракту`, `семантика_roslyn_msbuild`, `согласованность_компонентов`, `проверка_целостности_данных`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E3/task-01](task-01-role-classifier-Sol.md), [E3/task-02](task-02-watch-and-probe-coverage-Sol.md), [E1/task-04](../epoch-1-workspace-lifecycle/task-04-write-capabilities-Sol.md), [E3/task-00](task-00-live-state-contract-Astra.md).

## Цель

Обновлять опубликованный снимок по доставленным событиям в выбранной read policy.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

WorkspaceInputReconciler, DocumentDiskSync/NonCSharpDiskSync и under-lock production wiring; один владелец SolutionManager.

## Пакет реализации до dispatch

- Producer: [E3/task-00](task-00-live-state-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-03.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Дополнительные reconcile result/adapters только из packet; existing snapshot/nonCSharp layer не заменять собственным хранилищем.

### Файлы, которые можно менять

- Services/Workspace/SolutionManager.cs — flush/publication seam с эксклюзивным owner.
- Services/Workspace/WorkspaceInputReconciler.cs
- Services/Workspace/WorkspaceDocumentDiskSync.cs
- Services/Workspace/WorkspaceNonCSharpDiskSync.cs
- RoslynMcpServer.Tests/Workspace/WorkspaceNonCSharpDiskSyncTests.cs
- RoslynMcpServer.Tests/Workspace/WorkspaceSharedSourceSyncTests.cs
- Новые reconcile regressions по packet.

### Файлы, которые нельзя менять

- Services/Models/WorkspaceInputSession.cs
- Services/Workspace/WorkspaceFilePersistence.cs
- Services/Workspace/WorkspaceWriteBoundary.cs
- Services/Analyzers/
- Tools/
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- WorkspaceInputReconciler.Reconcile(WorkspaceInputSession session) → IReadOnlyList<InputContentReconcileResult>.
- WorkspaceDocumentDiskSync.ApplyAsync(Solution solution, IReadOnlyCollection<string> dirtyFullPaths, bool refreshAllDocuments, StringComparison pathComparison, WorkspaceInputMap? inputMap, CancellationToken cancellationToken = default) → Task<WorkspaceDocumentDiskSyncResult>.
- WorkspaceNonCSharpDiskSync.ReadUpdatesAsync(Solution solution, IReadOnlyCollection<string> paths, StringComparison pathComparison, CancellationToken cancellationToken) → Task<IReadOnlyList<KeyValuePair<string, SourceText>>>; Apply(Solution solution, IReadOnlyDictionary<string, SourceText> texts, StringComparison pathComparison) → Solution.
- Under-lock publication/retained non-CSharp layer declarations, graph-stale transitions и cancellation/pending ownership — packet task-00.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Existing .cs читается один раз для всех memberships; известные additional/config без evaluation role синхронизируются. New/deleted files/project/solution/Directory.Build меняют graph stale. XAML/resx/Razor учитываются по роли без text sync.

## Проверки и evidence

V07/V19/V22: next semantic видит delivered text, new file входит только next load, csproj bytes неизменны. Standing unknown не блокирует существующие reads/.cs writes само по себе.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не добавлять graph documents во время external sync, не писать csproj и не объявлять quiet missing callback доказательством unchanged.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
