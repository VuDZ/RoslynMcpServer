# E4/task-16 — workload-budget-and-performance

- Рекомендуемая модель: **Sol**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **средний** — Ошибочный workload/scenario или учёт attempts искажает hit-rate, overhead и activation evidence.
- Необходимые способности: `измерение_производительности`, `проектирование_проверок`, `диагностика_и_наблюдаемость`, `реализация_по_контракту`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E4/task-11](task-11-o5-reuse-acceptance-Astra.md), [E3/task-06](../epoch-3-live-consistency/task-06-live-consistency-acceptance-Astra.md), [E4/task-00](task-00-direction-gates-Astra.md).

## Цель

После технической реализации подготовить workload/budget для U-ARB-03 и провести
измерения; передать отчёт отдельной приёмочной сессии task-13.

## Обязательные входы и границы правок

[Спецификация](spec.md), [общие правила](../task-execution.md), основные handoffs,
[baseline runbook](../baseline-benchmark.md), [BTCPayServer candidate](../btcpay-candidate.md)
и [verification](../verification.md). До технической реализации задача deferred.

## Пакет реализации до dispatch

- Producer: [E4/task-00](task-00-direction-gates-Astra.md).
- Обязательный output producer: `design/implementation-packet-task-16.md` в каталоге этой эпохи.
- Readiness: producer не accepted без заполненных exact types/files/signatures;
  текущие списки задают срез и existing seams, не заменяют окончательный packet.

### Новые типы

- Только benchmark report/schema/scenario runner types из packet; production types не добавляются.

### Файлы, которые можно менять

- docs/workspace-load-cache/baseline_bench.py
- docs/workspace-load-cache/baseline-benchmark.md
- Новые benchmark/scenario scripts и workload-budget.md/performance-report.md по packet.
- External benchmark outputs — только в явно назначенном roslyn-mcp-bench, отдельно от server source tree.

### Файлы, которые нельзя менять

- Services/
- Tools/
- Diagnostics/
- RoslynMcpServer.csproj
- docs/workspace-load-cache/cache-contract.md
- Любой existing/new file вне окончательного allowlist packet.

### Потребляемые сигнатуры

- Точная CLI/scenario entrypoint schema existing baseline_bench.py и семь scenarios verification — packet task-00; не предполагать, что baseline-only runner уже умеет capture/hit/edit/live.
- Cache load response observation schema Epoch 2, reuse outcomes 4C и baseline corpus pin/schema потреблять read-only.
- Budget proposal fields/owner approval evidence + raw report layout задаёт task-00; numeric budget выбирает владелец до замеров.
- Все proposed declarations с nullability, cancellation, error/ownership и lock
  contract producer записывает полностью; Sol/Luna не достраивает их из spec.

## Конкретный результат

workload-budget.md: конкретные corpus/pin/profile/SDK/TFM/generators/modes и
предложенные latency/hit-rate/miss/resource budgets. Владелец утверждает их
до первых cache-hit performance results. После утверждения — семь сценариев
из verification, raw attempts и performance-report.md для task-13.
Даты утверждения и измерений сохраняются; бюджета за владельца не выбирать.

## Проверки и evidence

Новый PID + first useful semantic, disabled/miss capture/disk hit/RAM/edit restart/
no-op build/edit build restart/live; median/p95, DTB/stages/bytes/memory,
hit-rate и miss overhead. Unsupported required feature — revise/deferred,
не positive reuse. OrchardCore/Roslyn не обязательны; BTCPay — кандидат.
Report не является handoff или самостоятельным activation verdict.

## Недопустимые упрощения

Не выбирать thresholds после результатов, не расширять sdk-project-v1 ради
positive BTCPay hit и не менять production поведение из benchmark harness.
