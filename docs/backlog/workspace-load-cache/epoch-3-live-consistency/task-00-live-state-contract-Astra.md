# E3/task-00 — live-state-contract

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Смешение live freshness, disk validity и capture нарушает shared session и read/write policy.
- Необходимые способности: `архитектурное_мышление`, `согласованность_компонентов`, `анализ_конкурентности`, `анализ_зависимостей`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E2/task-08](../epoch-2-conservative-disk-cache/task-08-cache-checkpoint-acceptance-Astra.md).

## Цель

Согласовать cache lifecycle с существующим shared input state и выбранной live read policy.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Spec, pull input-state contract, WorkspaceInputSession/Snapshot, build freshness и disk watcher/reconciler; документ и точные интерфейсы downstream задач.

Обязательный named input — [current-baseline-audit.md](../epoch-0-feasibility/current-baseline-audit.md),
актуализированный E0/task-00. Перед design сверить HEAD/API с audit и последними
handoffs: existing A-LOAD/A-WRITE, watcher, additional/config, wpftmp и BOM paths
переиспользуются либо имеют явно объяснённую delta. Исторический 1.3.21 не описывает
текущее дерево автоматически.

## Конкретный результат

Отдельные RAM/index freshness, disk validity и capture cadence; shared session token/revisions/coverage, role priority и transition/event matrix. Разграничить standing coverage unknown и stale published base для A-WRITE.

## Проверки и evidence

Привязать V07–V09/V19–V23, repeated non-consuming pull, delivered/quietly lost events, graph stale reads и failed refresh; запреты writes проверяются по выбранному contract.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не вводить strict hash на каждом semantic call, periodic confirmation, blanket refuse/unknown label для standing coverage gaps или отдельный input index/session counter.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Пакеты потребителей

До accepted заполнить exact types/files/signatures по [общим правилам](../task-execution.md).
Producer выдаёт ограниченный пакет каждому потребителю; незаполненный packet
не переносится как design-работа на Sol/Luna исполнителя.

- E3/task-01: `epoch-3-live-consistency/design/implementation-packet-task-01.md`.
- E3/task-02: `epoch-3-live-consistency/design/implementation-packet-task-02.md`.
- E3/task-03: `epoch-3-live-consistency/design/implementation-packet-task-03.md`.
