# E4/task-02 — partial-metadata-implementation

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Смешение scope keys/coverage или CLI target может дать ответ и действие по неверному графу.
- Необходимые способности: `реализация_по_контракту`, `семантика_roslyn_msbuild`, `согласованность_компонентов`, `анализ_зависимостей`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-01](task-01-partial-metadata-contract-Astra.md), [E2/task-08](../epoch-2-conservative-disk-cache/task-08-cache-checkpoint-acceptance-Astra.md).

## Цель

Реализовать 4A по принятому scope/tool contract.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Scope/key handling, metadata loading и затронутые tool adapters; общий manager только при эксклюзивном владении.

## Пакет реализации до dispatch

- Producer: [E4/task-01](task-01-partial-metadata-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-02.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Canonical scope/key, partial coverage и metadata fallback types точно по task-01; schema/tools policy не достраивать.

### Файлы, которые можно менять

- Services/Workspace/SolutionManager.cs — согласованный scope/key seam, один owner.
- Tools/TestTools.cs — syntax coverage и explicit CLI target.
- Tools/WorkspaceTools.cs — scope/readiness reporting.
- Остальные semantic/refactoring tool files — только конечный per-tool manifest из task-01; blanket Tools/** не разрешён.
- Новые scope/metadata helpers и V24–V25 fixtures из packet.

### Файлы, которые нельзя менять

- Services/Models/WorkspaceInputSession.cs
- Services/Analyzers/InProcessAnalyzerAssemblyLoader.cs
- Services/Workspace/WorkspaceWriteBoundary.cs
- Services/Build/ — CLI target contract потреблять; изменение общего runner требует отдельной задачи.
- Tools files вне per-tool manifest
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- SolutionManager.GetPublishedSolutionAsync(CancellationToken cancellationToken = default) → Task<Solution?>; WorkspaceRootResolver.ResolveDotNetWorkingDirectory(string solutionOrProjectPath) → string.
- Exact scope canonicalization/key/expand/metadata loader declarations и per-tool signatures/refusal/coverage schemas — task-01 packet.
- GetTestList/RunDotNetTest и approved semantic tool signatures фиксируются на audited HEAD; public compatibility changes принимаются task-01 до реализации.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Independent RAM/disk keys, explicit expand, tool coverage/refusal, syntax-only test discovery и honest metadata fallback. Separate full-source comparator/evidence.

## Проверки и evidence

V24–V25 включая V24a/V24b; full/partial keys не пересекаются, CLI target берётся с диска явно, stale/corrupt DLL не выдаётся за свежий source graph.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не обобщать 4A на O5 и не ослаблять admission; результаты этой ветки принимает task-12 отдельно.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
