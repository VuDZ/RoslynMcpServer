# E0/task-01 — hydrate-host-spike

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **средний** — Неверный host experiment может скрыть ограничения записи или decoding до production integration.
- Необходимые способности: `реализация_по_контракту`, `семантика_roslyn_msbuild`, `проектирование_проверок`, `проверка_целостности_данных`.
- Статус: **accepted (isolated experiment, 2026-10-03)**; [validation, независимая приёмка и ограничения](evidence/task-01-hydrate-host.md).
- CI prerequisite fix: **validated locally (2026-10-05)** — установка exact SDK 10.0.300 в test-suite; [причина падения и повторная validation](evidence/task-01-hydrate-host.md). Проверка изменённого workflow на GitHub ещё не выполнена.
- Маршрут: [карта эпохи](README.md).
- Depends on: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md).

## Цель

Подтвердить выбранный AdhocWorkspace и writer .cs на публичных Roslyn API без скрытого DTB.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Изолированный spike и его фикстуры; существующие WorkspaceFilePersistence/WorkspaceWriteBoundary использовать как вход, не рефакторить production lifecycle.

## Пакет реализации до dispatch

- Producer: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-01.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Изолированный experiment host и fixture result types; exact names/API назначает E0/task-00. Production types не добавляются.

### Файлы, которые можно менять

- Новые experiment/fixture files, перечисленные packet; существующие production files — только read-only.

### Файлы, которые нельзя менять

- Services/Workspace/SolutionManager.cs
- Services/Workspace/WorkspaceWriteBoundary.cs
- Services/Analyzers/
- Tools/
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Public AdhocWorkspace populate/apply APIs и independent MSBuild open harness: точные версии, declarations и fixture entrypoints в packet task-00.
- Existing writer seam: WorkspaceFilePersistence.WriteTextAsync(WorkspaceInputSession? session, string fullPath, string text, Encoding encoding, CancellationToken cancellationToken) → Task; production здесь не менять.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Повторяемый host experiment: чтение, изменение текста, SDK-glob add/remove/rename, отказ explicit Compile без glob, reconciliation без записи csproj. Записать byte-level и encoding результаты.

## Проверки и evidence

V01/V02/V07; до/после сравнить csproj и saved .cs; следующий semantic call и обычная build. Подтвердить отсутствие OpenSolution/OpenProject на hydrate path.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не выдавать test host за production integration; не добавлять csproj writer или reload-on-write.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
