# Epoch 2 — Conservative disk cache: unchanged-restart checkpoint — карта выполнения

Статус исполнения серии: **deferred / backlog, 2026-10-07**.
Незавершённые работы отложены; статусы задач ниже сохранены как история
ранее принятого scope. [Причина и проверка переноса](../README.md).

[Комплект v2](../README.md) · [Каноническая спецификация](spec.md) ·
[Общие правила задач](../task-execution.md)

Примерная сложность эпохи: **4/5**. Все задания ниже — план работ,
а не утверждение о shipped behavior. Модель в имени — рекомендация; итоговая
приёмка выполняется по всей спецификации.

## Маршрут и границы

После production lifecycle принять cache protocol. Evidence, serialization и store готовятся по фиксированным интерфейсам; load/capture wiring начинается после их принятия. Финальная приёмка разделяет store checkpoint, unchanged reuse и public activation. U-ARB-03 не закрывать до полноценной реализации.

## Пул задач

- [task-00 — cache-protocol](task-00-cache-protocol-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E1/task-07](../epoch-1-workspace-lifecycle/task-07-lifecycle-acceptance-Astra.md).
  Зафиксировать contracts disk generation, strict validation, reader ownership и capture integration.
- [task-01 — dependency-evidence](task-01-dependency-evidence-Sol.md) — **Sol / high, 4/5**; риск: высокий; planned.
  Depends on: [E2/task-00](task-00-cache-protocol-Astra.md).
  Реализовать sdk-project-v1 evidence capture и whole-request support detector.
- [task-02 — payload-serialization](task-02-payload-serialization-Luna.md) — **Luna / medium, 2/5**; риск: высокий; planned.
  Depends on: [E2/task-00](task-00-cache-protocol-Astra.md), [E1/task-01](../epoch-1-workspace-lifecycle/task-01-semantic-dto-Sol.md).
  Сделать bounded DTO serialization/checksum по фиксированной schema.
- [task-03 — strict-validation](task-03-strict-validation-Sol.md) — **Sol / high, 4/5**; риск: высокий; planned.
  Depends on: [E2/task-01](task-01-dependency-evidence-Sol.md), [E2/task-02](task-02-payload-serialization-Luna.md), [E2/task-00](task-00-cache-protocol-Astra.md).
  Проверить неизменность полного bounded dependency cone до hydrate.
- [task-04 — generation-store](task-04-generation-store-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E2/task-00](task-00-cache-protocol-Astra.md), [E2/task-02](task-02-payload-serialization-Luna.md).
  Реализовать immutable generation store и безопасное multiprocess ownership/cleanup.
- [task-05 — load-capture-integration](task-05-load-capture-integration-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E2/task-03](task-03-strict-validation-Sol.md), [E2/task-04](task-04-generation-store-Astra.md), [E1/task-07](../epoch-1-workspace-lifecycle/task-07-lifecycle-acceptance-Astra.md).
  Интегрировать disk lookup/hydrate и синхронный capture в production load.
- [task-06 — fault-and-restart-matrix](task-06-fault-and-restart-matrix-Sol.md) — **Sol / high, 3/5**; риск: высокий; planned.
  Depends on: [E2/task-05](task-05-load-capture-integration-Astra.md), [E2/task-00](task-00-cache-protocol-Astra.md).
  Проверить реальные store/load сценарии независимо от unit happy path.
- [task-07 — attempt-observability](task-07-attempt-observability-Luna.md) — **Luna / medium, 2/5**; риск: средний; planned.
  Depends on: [E2/task-00](task-00-cache-protocol-Astra.md), [E2/task-05](task-05-load-capture-integration-Astra.md).
  Добавить согласованные observability поля и проверку их заполнения.
- [task-08 — cache-checkpoint-acceptance](task-08-cache-checkpoint-acceptance-Astra.md) — **Astra / high, 4/5**; риск: высокий; planned.
  Depends on: [E2/task-06](task-06-fault-and-restart-matrix-Sol.md), [E2/task-07](task-07-attempt-observability-Luna.md).
  Роль: independent-acceptance; отдельная fresh session, не автор design этой эпохи/проверяемой реализации.
  Принять unchanged checkpoint и отдельно оценить store, reuse и activation.

## Приёмка эпохи

Спецификация [spec.md](spec.md) остаётся источником обязательств. Design producers
не accepted до заполнения implementation packets потребителей. Приёмочные задачи
запускаются отдельными fresh sessions, которые не писали design этой эпохи и
проверяемую реализацию; Astra в имени не означает того же исполнителя.
Координатор проверяет author/reviewer session IDs и final code validation по
[общим правилам](../task-execution.md), фиксирует пять verdicts [handoff](../handoff-template.md).
Каждый недоказанный пункт имеет failed/not-run/deferred outcome и причину.
Закрытие отдельных задач не означает закрытие эпохи или public activation.
