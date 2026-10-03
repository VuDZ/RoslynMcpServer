# E1/task-00 — lifecycle-contract

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Ошибка lifecycle contract затрагивает publication, ownership и отказоустойчивость всех load paths.
- Необходимые способности: `архитектурное_мышление`, `согласованность_компонентов`, `анализ_конкурентности`, `управление_ресурсами`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E0/task-06](../epoch-0-feasibility/task-06-feasibility-handoff-Astra.md).

## Цель

Спроектировать один production lifecycle для ordinary load и hydrate.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Services/Workspace/SolutionManager.cs, WorkspaceLoadPreparationResult и analyzer prepare/gate; документ решения, без disk lookup.

Обязательный named input — [current-baseline-audit.md](../epoch-0-feasibility/current-baseline-audit.md),
актуализированный E0/task-00. Перед design сверить HEAD/API с audit и последними
handoffs: existing A-LOAD/A-WRITE, watcher, additional/config, wpftmp и BOM paths
переиспользуются либо имеют явно объяснённую delta. Исторический 1.3.21 не описывает
текущее дерево автоматически.

## Конкретный результат

План internal under-lock APIs, ownership candidate/old session, transition table same/different key success/failure/cancel, immutable published base и overlay. Точная DTO schema и context selection policy для следующих заданий.

## Проверки и evidence

Сопоставить каждый переход с V08/V09; отдельно two candidates, gate failure, retained old другого key, pending events и process-lifetime DLL side effects.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не делать recursive acquire, raw publication мимо gate, rollback CLR effects или новый public selector без compatibility решения.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Пакеты потребителей

До accepted заполнить exact types/files/signatures по [общим правилам](../task-execution.md).
Producer выдаёт ограниченный пакет каждому потребителю; незаполненный packet
не переносится как design-работа на Sol/Luna исполнителя.

- E1/task-01: `epoch-1-workspace-lifecycle/design/implementation-packet-task-01.md`.
- E1/task-02: `epoch-1-workspace-lifecycle/design/implementation-packet-task-02.md`.
- E1/task-03: `epoch-1-workspace-lifecycle/design/implementation-packet-task-03.md`.
- E1/task-04: `epoch-1-workspace-lifecycle/design/implementation-packet-task-04.md`.
