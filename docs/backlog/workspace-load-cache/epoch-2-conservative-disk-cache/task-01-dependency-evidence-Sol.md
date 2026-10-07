# E2/task-01 — dependency-evidence

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Потеря positive/absent/region evidence делает whole-request admission недостоверным.
- Необходимые способности: `реализация_по_контракту`, `анализ_зависимостей`, `семантика_roslyn_msbuild`, `проверка_целостности_данных`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E2/task-00](task-00-cache-protocol-Astra.md).

## Цель

Реализовать sdk-project-v1 evidence capture и whole-request support detector.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Новые dependency helpers и evaluation/binlog integration; существующий AnalyzerProvenanceCaptureService не считать полным detector.

## Пакет реализации до dispatch

- Producer: [E2/task-00](task-00-cache-protocol-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-01.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Evidence records и whole-request support detector/capture types по task-00; analyzer snapshot schema не подменять dependency completeness.

### Файлы, которые можно менять

- Services/Analyzers/AnalyzerProvenanceCaptureService.cs — только agreed extraction seam, без изменения admission gate.
- Новые evidence/detector files и admission/evaluation fixtures по packet.

### Файлы, которые нельзя менять

- Services/Workspace/SolutionManager.cs
- Services/Analyzers/AnalyzerExecutionGate.cs
- Services/Analyzers/InProcessAnalyzerAssemblyLoader.cs
- Services/Models/WorkspaceInputSession.cs
- Tools/
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Existing OpenAndCaptureAsync declaration получить из AnalyzerProvenanceCaptureService на audited HEAD; exact extension/extraction seam зафиксировать в task-00.
- Capture whole request → evidence/completeness; Detect(evidence, current toolset) → supported/unsupported/unknown: actual declarations, inner-instance keys и required evidence categories полностью задаёт packet.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Positive/known-absent/region/toolset/target evidence, completeness и supported/unsupported/unknown; SDK/Razor/Web правила, linked/external/obj inputs и portable analyzer hashes. Allowlist properties без секретов.

## Проверки и evidence

V03–V06/V12–V14; unknown custom target/неограничимый glob/external asset отвергают весь request, отсутствующий import становится present; совпадение с Epoch 0 evidence.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не делать detector documents-only, не ограничивать evidence только TaskOutput Analyzer и не трактовать non-SDK NuGet targets BTCPayServer как supported без решения.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
