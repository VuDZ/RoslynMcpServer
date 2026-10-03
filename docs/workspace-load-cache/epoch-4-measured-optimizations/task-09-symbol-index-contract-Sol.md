# E4/task-09 — symbol-index-contract

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **3/5**.
- Риск ошибки: **средний** — Неверный index design может смешать contexts или направить реализацию на недоказанный bottleneck.
- Необходимые способности: `архитектурное_мышление`, `семантика_roslyn_msbuild`, `анализ_зависимостей`, `измерение_производительности`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-00](task-00-direction-gates-Astra.md).

## Цель

Спроектировать 4D только при измеренном symbol-search bottleneck.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Measured bottleneck evidence и текущие symbol search tools; самостоятельный design без index implementation.

## Пакет реализации до dispatch

- Producer: [E4/task-00](task-00-direction-gates-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-09.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Production types не создавать: output — index design/schema и packet для task-10.

### Файлы, которые можно менять

- docs/workspace-load-cache/epoch-4-measured-optimizations/design/task-09-symbol-index-contract.md
- docs/workspace-load-cache/epoch-4-measured-optimizations/design/implementation-packet-task-10.md
- Bottleneck evidence/report files по packet task-00.

### Файлы, которые нельзя менять

- Services/
- Tools/
- Diagnostics/
- Cache admission/schema и generated symbol contract
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Read-only SolutionManager.GetPublishedSolutionAsync(CancellationToken cancellationToken = default) → Task<Solution?>.
- Current NavigationTools symbol-search signatures и stage profiling entrypoints на audited HEAD фиксирует task-00; задача потребляет принятое bottleneck/budget evidence.
- Output packet task-10 обязан дать exact index types/files/lookup/candidate-verification/update/delete signatures и invalid/miss fallback.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

Hint-only index schema: membership/project/TFM/parse options/defines/content hash/schema. Candidate verification в текущей Roslyn solution, delete/project removal и invalid/miss fallback. Generated symbols excluded.

## Проверки и evidence

V26 план: одинаковый text при разных defines/TFM, stale hash, удалённый document/project, candidate rejected Roslyn; указать stage-specific baseline и заранее согласованный budget.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Если bottleneck или budget evidence отсутствует, задача deferred, implementation task-10 не стартует. Не выводить необходимость index из общего slow first-semantic.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Пакеты потребителей

До accepted заполнить exact types/files/signatures по [общим правилам](../task-execution.md).
Producer выдаёт ограниченный пакет каждому потребителю; незаполненный packet
не переносится как design-работа на Sol/Luna исполнителя.

- E4/task-10: `epoch-4-measured-optimizations/design/implementation-packet-task-10.md`.
