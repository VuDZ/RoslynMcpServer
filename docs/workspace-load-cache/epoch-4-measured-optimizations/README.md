# Epoch 4 — Measured optional directions — карта выполнения

[Комплект v2](../README.md) · [Каноническая спецификация](spec.md) ·
[Общие правила задач](../task-execution.md)

Примерная сложность эпохи: **5/5**. Все задания ниже — план работ,
а не утверждение о shipped behavior. Модель в имени — рекомендация; итоговая
приёмка выполняется по всей спецификации.

## Маршрут и границы

Ветки 4A, 4B, 4C, 4D независимы; результаты и activation verdicts ведутся отдельно. 4C — выбранный в пуле маршрут обязательного O5; иной механизм требует отдельного доказательства. 4A/4B/4D не являются зависимостями завершения 4C. Symbol index нельзя реализовывать до доказанного bottleneck. Финальный U-ARB-03 workload/budget gate выполняется после технической реализации, но до получения cache-hit performance results.

## Пул задач

- [task-00 — direction-gates](task-00-direction-gates-Astra.md) — **Astra / medium, 4/5**; риск: средний; planned.
  Depends on: [E2/task-08](../epoch-2-conservative-disk-cache/task-08-cache-checkpoint-acceptance-Astra.md).
  Определить доказательства и design gates отдельно для 4A–4D.
- [task-01 — partial-metadata-contract](task-01-partial-metadata-contract-Astra.md) — **Astra / high, 4/5**; риск: высокий; planned.
  Depends on: [E4/task-00](task-00-direction-gates-Astra.md), [E1/task-07](../epoch-1-workspace-lifecycle/task-07-lifecycle-acceptance-Astra.md).
  Определить canonical scope и честную tool coverage для 4A.
- [task-02 — partial-metadata-implementation](task-02-partial-metadata-implementation-Sol.md) — **Sol / high, 4/5**; риск: высокий; planned.
  Depends on: [E4/task-01](task-01-partial-metadata-contract-Astra.md), [E2/task-08](../epoch-2-conservative-disk-cache/task-08-cache-checkpoint-acceptance-Astra.md).
  Реализовать 4A по принятому scope/tool contract.
- [task-03 — validation-policy-contract](task-03-validation-policy-contract-Astra.md) — **Astra / high, 4/5**; риск: высокий; planned.
  Depends on: [E4/task-00](task-00-direction-gates-Astra.md).
  Определить producer evidence → reader policy для 4B без изменения live read policy.
- [task-04 — validation-modes](task-04-validation-modes-Sol.md) — **Sol / high, 4/5**; риск: высокий; planned.
  Depends on: [E4/task-03](task-03-validation-policy-contract-Astra.md), [E2/task-08](../epoch-2-conservative-disk-cache/task-08-cache-checkpoint-acceptance-Astra.md).
  Реализовать 4B compatibility и distinct observability по принятой matrix.
- [task-05 — closed-subset-reuse-contract](task-05-closed-subset-reuse-contract-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E4/task-00](task-00-direction-gates-Astra.md), [E3/task-06](../epoch-3-live-consistency/task-06-live-consistency-acceptance-Astra.md).
  Доказать closed subset для O5 и задать отдельные content/membership rules.
- [task-06 — content-reuse](task-06-content-reuse-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E4/task-05](task-05-closed-subset-reuse-contract-Astra.md).
  Реализовать без полного DTB только доказанный content-change маршрут.
- [task-07 — membership-reuse](task-07-membership-reuse-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E4/task-06](task-06-content-reuse-Astra.md).
  Реализовать closed-subset membership reuse и точную instance invalidation.
- [task-08 — reuse-oracle-matrix](task-08-reuse-oracle-matrix-Sol.md) — **Sol / high, 4/5**; риск: высокий; planned.
  Depends on: [E4/task-07](task-07-membership-reuse-Astra.md), [E4/task-05](task-05-closed-subset-reuse-contract-Astra.md).
  Независимо проверить correctness нового O5 profile на content и membership изменениях.
- [task-09 — symbol-index-contract](task-09-symbol-index-contract-Sol.md) — **Sol / high, 3/5**; риск: средний; deferred — до bottleneck/budget и принятой реализации 4D.
  Depends on: [E4/task-00](task-00-direction-gates-Astra.md).
  Спроектировать 4D только при измеренном symbol-search bottleneck.
- [task-10 — symbol-index-implementation](task-10-symbol-index-implementation-Sol.md) — **Sol / medium, 3/5**; риск: средний; deferred — до bottleneck/budget и принятой реализации 4D.
  Depends on: [E4/task-09](task-09-symbol-index-contract-Sol.md).
  Реализовать 4D index как проверяемый hint по принятому design.
- [task-11 — o5-reuse-acceptance](task-11-o5-reuse-acceptance-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E4/task-08](task-08-reuse-oracle-matrix-Sol.md).
  Роль: independent-acceptance; отдельная fresh session, не автор design этой эпохи/проверяемой реализации.
  Независимо принять техническое закрытие O5 без зависимости от optional 4A/4B/4D.
- [task-12 — partial-metadata-acceptance](task-12-partial-metadata-acceptance-Astra.md) — **Astra / high, 4/5**; риск: высокий; planned.
  Depends on: [E4/task-02](task-02-partial-metadata-implementation-Sol.md).
  Роль: independent-acceptance; отдельная fresh session, не автор design этой эпохи/проверяемой реализации.
  Независимо принять реализацию 4A после принятого task-02.
- [task-13 — workload-budget-and-series-gate](task-13-workload-budget-and-series-gate-Astra.md) — **Astra / high, 5/5**; риск: высокий; deferred — после технической реализации; budget approval до замеров.
  Depends on: [E4/task-16](task-16-workload-budget-and-performance-Sol.md), [E4/task-11](task-11-o5-reuse-acceptance-Astra.md), [E3/task-06](../epoch-3-live-consistency/task-06-live-consistency-acceptance-Astra.md).
  Роль: independent-acceptance; отдельная fresh session, не автор design этой эпохи/проверяемой реализации.
  Независимо проверить U-ARB-03 evidence и готовность серии/activation после workload/performance task-16.
- [task-14 — validation-modes-acceptance](task-14-validation-modes-acceptance-Astra.md) — **Astra / high, 4/5**; риск: высокий; planned.
  Depends on: [E4/task-04](task-04-validation-modes-Sol.md).
  Роль: independent-acceptance; отдельная fresh session, не автор design этой эпохи/проверяемой реализации.
  Независимо принять реализацию 4B после принятого task-04.
- [task-15 — symbol-index-acceptance](task-15-symbol-index-acceptance-Astra.md) — **Astra / high, 4/5**; риск: средний; deferred — до bottleneck/budget и принятой реализации 4D.
  Depends on: [E4/task-10](task-10-symbol-index-implementation-Sol.md).
  Роль: independent-acceptance; отдельная fresh session, не автор design этой эпохи/проверяемой реализации.
  Независимо принять реализацию 4D после принятого task-10.
- [task-16 — workload-budget-and-performance](task-16-workload-budget-and-performance-Sol.md) — **Sol / high, 4/5**; риск: средний; deferred — после технической реализации; budget approval до замеров.
  Depends on: [E4/task-11](task-11-o5-reuse-acceptance-Astra.md), [E3/task-06](../epoch-3-live-consistency/task-06-live-consistency-acceptance-Astra.md), [E4/task-00](task-00-direction-gates-Astra.md).
  После технической реализации подготовить workload/budget для U-ARB-03 и провести измерения; передать отчёт отдельной приёмочной сессии task-13.

Ветки имеют обычные рёбра: 4A implementation task-02 → acceptance task-12;
4B task-04 → task-14; 4D task-10 → task-15. Приёмка 4C — task-08 → task-11.
Workload/budget/performance делает task-16; основной series gate task-13 принимает
его outputs отдельной сессией. Task-13 scope — E0–E3 и 4C; optional verdicts отдельно.

Статусы направлений: 4A — planned; 4B — planned; 4C — planned; 4D — deferred
(нет принятого bottleneck/budget evidence). Каждая приёмка меняет только статус
своей ветки и её отдельный handoff.

## Приёмка эпохи

Спецификация [spec.md](spec.md) остаётся источником обязательств. Design producers
не accepted до заполнения implementation packets потребителей. Приёмочные задачи
запускаются отдельными fresh sessions, которые не писали design этой эпохи и
проверяемую реализацию; Astra в имени не означает того же исполнителя.
Координатор проверяет author/reviewer session IDs и final code validation по
[общим правилам](../task-execution.md), фиксирует пять verdicts [handoff](../handoff-template.md).
Каждый недоказанный пункт имеет failed/not-run/deferred outcome и причину.
Закрытие отдельных задач не означает закрытие эпохи или public activation.
