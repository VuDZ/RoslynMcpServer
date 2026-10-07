# E1/task-03 — production-hydrate

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Ошибочная production hydration может опубликовать неполный граф или потерять владение candidate.
- Необходимые способности: `реализация_по_контракту`, `семантика_roslyn_msbuild`, `согласованность_компонентов`, `управление_ресурсами`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E1/task-01](task-01-semantic-dto-Sol.md), [E1/task-02](task-02-decoding-fixtures-Luna.md), [E1/task-00](task-00-lifecycle-contract-Astra.md).

## Цель

Встроить AdhocWorkspace candidate в настоящий load/prepare путь без disk lookup.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Hydrator и production wiring Services/Workspace; изменение SolutionManager только при эксклюзивном владении.

## Пакет реализации до dispatch

- Producer: [E1/task-00](task-00-lifecycle-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-03.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Production Adhoc hydrator/candidate owner и load adapter types из task-00; existing semantic DTO не перепроектировать.

### Файлы, которые можно менять

- Services/Workspace/SolutionManager.cs — только agreed hydrate seam с единственным integration owner.
- Services/Workspace/WorkspaceLoadPreparationResult.cs
- Новые hydrator/adapter files и focused production-hydrate tests из packet.

### Файлы, которые нельзя менять

- Services/Workspace/WorkspaceWriteBoundary.cs
- Services/Workspace/WorkspaceFilePersistence.cs
- Services/Models/WorkspaceInputSession.cs
- Services/Analyzers/InProcessAnalyzerAssemblyLoader.cs
- Tools/
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Current entry: SolutionManager.LoadAndPrepareAsync(string solutionOrProjectPath, bool shadowCopyInSolutionAnalyzers, CancellationToken cancellationToken, string? configuration = null, string? platform = null, string? targetFramework = null, string? buildArgs = null, WorkspaceLoadSource loadSource = WorkspaceLoadSource.ExplicitLoad) → Task<WorkspaceLoadPreparationResult>.
- SolutionManager.GetPublishedSolutionAsync(CancellationToken cancellationToken = default) → Task<Solution?>.
- DTO → owned Adhoc candidate и under-lock validate/prepare handoff: новые exact signatures/ownership в task-00 packet, существующий private LoadCoreAsync не является готовым hydrate API.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Ordinary и DTO hydrate используют общий candidate lifecycle и public API mappings. Hydrate доступен проверкам через production seam; semantic accessor получает только published snapshot.

## Проверки и evidence

V01–V06/V09; no Open* и no full DTB на hydrate, failed validation не публикует candidate. Сравнение с fresh MSBuild из Epoch 0.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не оставлять единственный working path в test hydrator, не включать cache lookup и не публиковать непроверенный graph.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
