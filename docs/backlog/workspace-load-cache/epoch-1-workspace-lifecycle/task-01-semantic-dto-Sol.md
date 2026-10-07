# E1/task-01 — semantic-dto

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **medium**.
- Примерная сложность: **3/5**.
- Риск ошибки: **высокий** — Потеря options, contexts или memberships в DTO меняет semantic результат hydrate.
- Необходимые способности: `реализация_по_контракту`, `семантика_roslyn_msbuild`, `анализ_зависимостей`, `проверка_целостности_данных`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E1/task-00](task-00-lifecycle-contract-Astra.md).

## Цель

Реализовать semantic DTO и public API mapping по принятому lifecycle contract.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Новые DTO/mapping helpers; не менять SolutionManager, store или dependency detector. Schema из task-00 обязательна.

## Пакет реализации до dispatch

- Producer: [E1/task-00](task-00-lifecycle-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-01.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Semantic DTO records и mapping helpers: roots/inner-instance/edges/options/document metadata/reference/decoding; exact names/fields/namespaces/files назначает task-00.

### Файлы, которые можно менять

- Только новые DTO/mapping files из packet; focused mapper tests в RoslynMcpServer.Tests/Workspace по конечному manifest.

### Файлы, которые нельзя менять

- Services/Workspace/SolutionManager.cs
- Services/Models/WorkspaceInputSession.cs
- Services/Analyzers/
- Tools/
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Semantic capture/map declarations из task-00: source Solution → portable DTO, DTO/context → Roslyn public graph data; exact cancellation/error/encoding contract.
- Read-only existing input identity: WorkspaceInputMap.InstancesForFile(string? projectFilePath) → IReadOnlyList<LoadedProjectInstance>; ProjectId/session IDs не portable output.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Portable roots/inner instances/edges/options/documents/references, path → all memberships и согласованный decoding policy. Dependency/admission evidence хранить отдельной моделью.

## Проверки и evidence

V01–V06: не терять parse/compilation options, linked/TFM memberships, generated input categories; сверить DTO поля с epoch-0 oracle.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не сериализовать Solution/Compilation/SyntaxTree, тексты, ProjectId/LoadSessionId или shadow paths; не выдумывать schema при пробеле в task-00.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
