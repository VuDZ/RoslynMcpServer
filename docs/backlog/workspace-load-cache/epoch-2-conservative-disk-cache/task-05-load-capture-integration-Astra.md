# E2/task-05 — load-capture-integration

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Неверная load/capture интеграция обходит admission либо публикует поколение после отмены.
- Необходимые способности: `архитектурное_мышление`, `анализ_конкурентности`, `согласованность_компонентов`, `управление_ресурсами`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E2/task-03](task-03-strict-validation-Sol.md), [E2/task-04](task-04-generation-store-Astra.md), [E1/task-07](../epoch-1-workspace-lifecycle/task-07-lifecycle-acceptance-Astra.md).

## Цель

Интегрировать disk lookup/hydrate и синхронный capture в production load.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

SolutionManager, WorkspaceTools/load responses и новые cache services; эксклюзивное владение общим manager.

## Конкретный результат

forceReload обход RAM/disk; useDiskCache opt-in default=false; same-key RAM сохраняет sync/stale/sticky. Positive disk candidate проходит validation/lease/prepare/gate/publication. Full stable ordinary load пишет pointer до ответа одной попыткой; load success отдельно от write failure.

## Проверки и evidence

V01–V09/V11d/V23 в отдельном PID; zero full-DTB на настоящем positive hit. false→true unverified RAM не запускает скрытый capture. Hash drift/cancel/reset/shutdown не публикуют неполный pointer.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не принимать hydrate failure partial graph, не bypass admission и не записывать поколения после каждого edit либо после load response.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.
