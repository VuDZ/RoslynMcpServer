# E3/task-02 — watch-and-probe-coverage

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Потеря explicit watch/probe или shared revisions оставляет значимый input вне заявленного покрытия.
- Необходимые способности: `реализация_по_контракту`, `анализ_зависимостей`, `управление_ресурсами`, `согласованность_компонентов`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E3/task-00](task-00-live-state-contract-Astra.md).

## Цель

Подключить bounded watch/probe coverage к общему input state.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

WorkspaceDiskWatcherStarter/Starter result, path filter и coverage models; reuse существующих session/revisions.

## Пакет реализации до dispatch

- Producer: [E3/task-00](task-00-live-state-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-02.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Watch/probe planning descriptors/results по task-00; existing coverage/session models переиспользовать.

### Файлы, которые можно менять

- Services/Workspace/WorkspaceDiskWatcherStarter.cs
- Services/Workspace/WorkspaceDiskWatcherStartResult.cs
- Services/Workspace/WorkspaceDiskPathFilter.cs
- Services/Models/InputWatcherDescriptor.cs
- Services/Models/InputCoverageScope.cs
- Services/Models/InputCoverageGap.cs
- RoslynMcpServer.Tests/Workspace/WorkspaceDiskWatcherStarterTests.cs
- Новые coverage/probe tests и helpers по packet.

### Файлы, которые нельзя менять

- Services/Workspace/SolutionManager.cs
- Services/Models/WorkspaceInputSession.cs — session/revision API read-only; необходимую delta вернуть design owner.
- Services/Analyzers/
- Tools/
- Services/Build/
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- WorkspaceDiskWatcherStarter.Start(IReadOnlyList<InputWatcherDescriptor> descriptors, WorkspaceInputSession session, Action<FileSystemEventArgs> onChanged, Action<RenamedEventArgs> onRenamed, Action<ErrorEventArgs> onError, Action<LogLevel, Exception?, string> log) → WorkspaceDiskWatcherStartResult.
- WorkspaceInputSession.Pull() → WorkspaceInputSnapshot; existing coverage notification API фиксируется в packet.
- Planning/probe startup/overflow/cancel/resource limit declarations с exact bounded outcomes задаёт task-00.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Regions, walk-up ancestors, external explicit paths, obj и possible absent paths; grouping/resource limits/startup/overflow/error/cancel с telemetry. Repeated pulls не потребляют evidence других consumers.

## Проверки и evidence

V19/V22/V23: unavailable watcher, directory rename/delete, ancestor props, explicit path внутри pruned tree и linked inputs; known-doc reread не делает coverage complete.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не дублировать session counter/index, не подавлять explicit watch/probe prune-фильтром и не обещать обнаружение quiet lost events.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
