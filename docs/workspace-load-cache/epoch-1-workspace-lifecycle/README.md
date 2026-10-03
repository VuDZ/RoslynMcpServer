# Epoch 1 — Production lifecycle и эквивалентная гидрация — карта выполнения

[Комплект v2](../README.md) · [Каноническая спецификация](spec.md) ·
[Общие правила задач](../task-execution.md)

Примерная сложность эпохи: **5/5**. Все задания ниже — план работ,
а не утверждение о shipped behavior. Модель в имени — рекомендация; итоговая
приёмка выполняется по всей спецификации.

## Маршрут и границы

После implementation verdict Epoch 0 принять общий lifecycle contract. DTO и fixtures можно готовить отдельно; production hydrate, writer, overlay и publication интегрировать последовательно. SolutionManager принадлежит одному активному исполнителю.

## Пул задач

- [task-00 — lifecycle-contract](task-00-lifecycle-contract-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E0/task-06](../epoch-0-feasibility/task-06-feasibility-handoff-Astra.md).
  Спроектировать один production lifecycle для ordinary load и hydrate.
- [task-01 — semantic-dto](task-01-semantic-dto-Sol.md) — **Sol / medium, 3/5**; риск: высокий; planned.
  Depends on: [E1/task-00](task-00-lifecycle-contract-Astra.md).
  Реализовать semantic DTO и public API mapping по принятому lifecycle contract.
- [task-02 — decoding-fixtures](task-02-decoding-fixtures-Luna.md) — **Luna / medium, 2/5**; риск: средний; planned.
  Depends on: [E1/task-00](task-00-lifecycle-contract-Astra.md), [E1/task-01](task-01-semantic-dto-Sol.md).
  Закрепить decoding и round-trip контракт на небольших однозначных fixtures.
- [task-03 — production-hydrate](task-03-production-hydrate-Sol.md) — **Sol / high, 4/5**; риск: высокий; planned.
  Depends on: [E1/task-01](task-01-semantic-dto-Sol.md), [E1/task-02](task-02-decoding-fixtures-Luna.md), [E1/task-00](task-00-lifecycle-contract-Astra.md).
  Встроить AdhocWorkspace candidate в настоящий load/prepare путь без disk lookup.
- [task-04 — write-capabilities](task-04-write-capabilities-Sol.md) — **Sol / high, 4/5**; риск: высокий; planned.
  Depends on: [E1/task-03](task-03-production-hydrate-Sol.md), [E1/task-00](task-00-lifecycle-contract-Astra.md).
  Реализовать .cs persistence и document capabilities выбранного host с A-WRITE.
- [task-05 — overlay-admission](task-05-overlay-admission-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E1/task-03](task-03-production-hydrate-Sol.md).
  Обеспечить base/overlay separation и новый session-bound admission при hydrate.
- [task-06 — atomic-publication](task-06-atomic-publication-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E1/task-04](task-04-write-capabilities-Sol.md), [E1/task-05](task-05-overlay-admission-Astra.md).
  Завершить атомарную publication и lifetime/lock integration общего lifecycle.
- [task-07 — lifecycle-acceptance](task-07-lifecycle-acceptance-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E1/task-06](task-06-atomic-publication-Astra.md).
  Роль: independent-acceptance; отдельная fresh session, не автор design этой эпохи/проверяемой реализации.
  Принять общий production lifecycle и выдать handoff для disk cache.

## Приёмка эпохи

Спецификация [spec.md](spec.md) остаётся источником обязательств. Design producers
не accepted до заполнения implementation packets потребителей. Приёмочные задачи
запускаются отдельными fresh sessions, которые не писали design этой эпохи и
проверяемую реализацию; Astra в имени не означает того же исполнителя.
Координатор проверяет author/reviewer session IDs и final code validation по
[общим правилам](../task-execution.md), фиксирует пять verdicts [handoff](../handoff-template.md).
Каждый недоказанный пункт имеет failed/not-run/deferred outcome и причину.
Закрытие отдельных задач не означает закрытие эпохи или public activation.
