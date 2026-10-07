# Epoch 3 — Live consistency — карта выполнения

Статус исполнения серии: **deferred / backlog, 2026-10-07**.
Незавершённые работы отложены; статусы задач ниже сохранены как история
ранее принятого scope. [Причина и проверка переноса](../README.md).

[Комплект v2](../README.md) · [Каноническая спецификация](spec.md) ·
[Общие правила задач](../task-execution.md)

Примерная сложность эпохи: **5/5**. Все задания ниже — план работ,
а не утверждение о shipped behavior. Модель в имени — рекомендация; итоговая
приёмка выполняется по всей спецификации.

## Маршрут и границы

Принять модель состояний с выбранным watcher read contract. Classifier и coverage helpers можно разрабатывать отдельно при согласованных интерфейсах; reconciliation, own-write и lock/publication integration идут последовательно. Общий input session используется также WPF/build, новый счётчик не заводится.

## Пул задач

- [task-00 — live-state-contract](task-00-live-state-contract-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E2/task-08](../epoch-2-conservative-disk-cache/task-08-cache-checkpoint-acceptance-Astra.md).
  Согласовать cache lifecycle с существующим shared input state и выбранной live read policy.
- [task-01 — role-classifier](task-01-role-classifier-Sol.md) — **Sol / high, 4/5**; риск: высокий; planned.
  Depends on: [E3/task-00](task-00-live-state-contract-Astra.md).
  Реализовать classification по roles/evidence с сохранением всех memberships.
- [task-02 — watch-and-probe-coverage](task-02-watch-and-probe-coverage-Sol.md) — **Sol / high, 4/5**; риск: высокий; planned.
  Depends on: [E3/task-00](task-00-live-state-contract-Astra.md).
  Подключить bounded watch/probe coverage к общему input state.
- [task-03 — live-reconciliation](task-03-live-reconciliation-Sol.md) — **Sol / high, 4/5**; риск: высокий; planned.
  Depends on: [E3/task-01](task-01-role-classifier-Sol.md), [E3/task-02](task-02-watch-and-probe-coverage-Sol.md), [E1/task-04](../epoch-1-workspace-lifecycle/task-04-write-capabilities-Sol.md), [E3/task-00](task-00-live-state-contract-Astra.md).
  Обновлять опубликованный снимок по доставленным событиям в выбранной read policy.
- [task-04 — own-write-revisions](task-04-own-write-revisions-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E3/task-03](task-03-live-reconciliation-Sol.md), [E1/task-04](../epoch-1-workspace-lifecycle/task-04-write-capabilities-Sol.md).
  Совместить persistence acknowledgements с pending events без потери внешних правок.
- [task-05 — flush-publication-concurrency](task-05-flush-publication-concurrency-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E3/task-04](task-04-own-write-revisions-Astra.md).
  Закрыть гонки live flush, writes, reset и cache load на общем acquisition.
- [task-06 — live-consistency-acceptance](task-06-live-consistency-acceptance-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E3/task-05](task-05-flush-publication-concurrency-Astra.md).
  Роль: independent-acceptance; отдельная fresh session, не автор design этой эпохи/проверяемой реализации.
  Принять O4 в scope выбранной watcher policy и зафиксировать её практические ограничения.

## Приёмка эпохи

Спецификация [spec.md](spec.md) остаётся источником обязательств. Design producers
не accepted до заполнения implementation packets потребителей. Приёмочные задачи
запускаются отдельными fresh sessions, которые не писали design этой эпохи и
проверяемую реализацию; Astra в имени не означает того же исполнителя.
Координатор проверяет author/reviewer session IDs и final code validation по
[общим правилам](../task-execution.md), фиксирует пять verdicts [handoff](../handoff-template.md).
Каждый недоказанный пункт имеет failed/not-run/deferred outcome и причину.
Закрытие отдельных задач не означает закрытие эпохи или public activation.
