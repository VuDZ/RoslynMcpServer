# E3/task-01 — role-classifier

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Неверная role priority может пропустить graph change или применить неподдержанный text update.
- Необходимые способности: `реализация_по_контракту`, `анализ_зависимостей`, `семантика_roslyn_msbuild`, `согласованность_компонентов`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E3/task-00](task-00-live-state-contract-Astra.md).

## Цель

Реализовать classification по roles/evidence с сохранением всех memberships.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

WorkspaceDiskEventClassifier/InputMapBuilder и согласованные helpers; общий manager не править параллельно.

## Пакет реализации до dispatch

- Producer: [E3/task-00](task-00-live-state-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-01.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Role decision/negative-region classification types только по packet; existing InputRole/InputMap переиспользовать, второй index не создавать.

### Файлы, которые можно менять

- Services/Workspace/WorkspaceDiskEventClassifier.cs
- Services/Workspace/WorkspaceInputMapBuilder.cs
- Services/Models/InputRole.cs — только accepted role delta.
- RoslynMcpServer.Tests/Workspace/WorkspaceDiskEventClassificationTests.cs
- Новые согласованные classifier fixtures/helpers из packet.

### Файлы, которые нельзя менять

- Services/Workspace/SolutionManager.cs
- Services/Models/WorkspaceInputSession.cs
- Services/Workspace/WorkspaceFilePersistence.cs
- Services/Analyzers/
- Tools/
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- WorkspaceDiskEventClassifier.Classify(string fullPath, WorkspaceInputMap map) → DiskEventDecision.
- WorkspaceDiskEventClassifier.SyncsNonCSharpDocumentText(WorkspaceInputMap? map, string fullPath) → bool.
- WorkspaceInputMapBuilder.Build(Solution solution, Guid generation, string? loadedWorkspacePath, bool loadGraphComplete) → WorkspaceInputMap.
- Новые profile-evidence consult declarations и combined-role priority exact contract — packet task-00.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Known dependency, potential membership/negative region, proven irrelevant и relevant unknown; graph role приоритетна. Custom-extension inputs распознаются по роли, irrelevant/editor temp не вызывают DTB.

## Проверки и evidence

V11/V19/V22: Add/Remove/missing document, custom AdditionalFile/import, явный obj input, combined content/evaluation roles и nonmember. Ни один unknown не превращается в disk hit.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не использовать extension blacklist как полноту и не объявлять missing document успешно applied.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
