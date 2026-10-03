# Epoch 0 — Feasibility, profile и baseline — карта выполнения

[Комплект v2](../README.md) · [Каноническая спецификация](spec.md) ·
[Общие правила задач](../task-execution.md)

Примерная сложность эпохи: **4/5**. Все задания ниже — план работ,
а не утверждение о shipped behavior. Модель в имени — рекомендация; итоговая
приёмка выполняется по всей спецификации.

## Маршрут и границы

Сначала уточнить входы и план доказательств; затем проверить host и admission, собрать фикстуры и независимый oracle. Уже выбранные host/profile/overlay не выбирать заново. U-ARB-03 отложен до реализации кеша; handoff перечисляет отсутствующее evidence и выдаёт отдельное разрешение на следующий шаг.

Обязательный первый named input/output — [current-baseline-audit.md](current-baseline-audit.md):
текущий code inventory 1.5.4, historical pin 1.3.21 и remaining delta.
Task-00 проверяет и дополняет audit перед последующими contracts.

Артефакты исполнения task-00 (2026-10-03):
[аудит](current-baseline-audit.md), [execution contract](design.md),
[отчёт и ограничения validation](task-00-execution-report.md),
[результат baseline tests](task-00-test-run.md).
Пакеты потребителей: [task-01](design/implementation-packet-task-01.md),
[task-03](design/implementation-packet-task-03.md),
[task-04](design/implementation-packet-task-04.md),
[task-05](design/implementation-packet-task-05.md).
Перед dispatch task-03 автор task-02 обязан дополнить его пакет observed findings;
заполненные типы и сигнатуры не заменяют ещё не выполненные эксперименты.

## Пул задач

- [task-00 — current-baseline-and-execution-contract](task-00-current-baseline-and-execution-contract-Astra.md) — **Astra / high, 4/5**; риск: высокий; **accepted (docs-only, 2026-10-03)**.
  Depends on: нет.
  Сначала сверить текущий production baseline с историческим основанием spec; затем определить оставшуюся delta и план доказательств.
  Audit/design/четыре packets проверены; independent reviewer `/root/contract_review`,
  must-fix закрыты. [Результаты и отдельные verdicts](task-00-execution-report.md).
- [task-01 — hydrate-host-spike](task-01-hydrate-host-spike-Sol.md) — **Sol / high, 4/5**; риск: средний; planned.
  Depends on: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md).
  Подтвердить выбранный AdhocWorkspace и writer .cs на публичных Roslyn API без скрытого DTB.
- [task-02 — dependency-admission-spike](task-02-dependency-admission-spike-Astra.md) — **Astra / high, 5/5**; риск: высокий; planned.
  Depends on: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md).
  Доказать замкнутость sdk-project-v1 и переносимую analyzer identity на выбранных источниках evidence.
- [task-03 — oracle-fixtures](task-03-oracle-fixtures-Luna.md) — **Luna / medium, 2/5**; риск: средний; planned.
  Depends on: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md), [E0/task-01](task-01-hydrate-host-spike-Sol.md), [E0/task-02](task-02-dependency-admission-spike-Astra.md).
  Создать ограниченные фикстуры и ожидаемые данные по уже принятому плану oracle.
- [task-04 — fresh-msbuild-equivalence](task-04-fresh-msbuild-equivalence-Sol.md) — **Sol / high, 4/5**; риск: высокий; planned.
  Depends on: [E0/task-01](task-01-hydrate-host-spike-Sol.md), [E0/task-02](task-02-dependency-admission-spike-Astra.md), [E0/task-03](task-03-oracle-fixtures-Luna.md), [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md).
  Сравнить candidate host с независимой свежей MSBuild загрузкой.
- [task-05 — metadata-comparator](task-05-metadata-comparator-Sol.md) — **Sol / medium, 3/5**; риск: средний; planned.
  Depends on: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md), [E0/task-04](task-04-fresh-msbuild-equivalence-Sol.md).
  Проверить metadata fast-open как отдельный O7 comparator.
- [task-06 — feasibility-handoff](task-06-feasibility-handoff-Astra.md) — **Astra / high, 4/5**; риск: высокий; planned.
  Depends on: [E0/task-04](task-04-fresh-msbuild-equivalence-Sol.md), [E0/task-05](task-05-metadata-comparator-Sol.md).
  Роль: independent-acceptance; отдельная fresh session, не автор design этой эпохи/проверяемой реализации.
  Независимо принять технические результаты feasibility и определить допустимый следующий шаг.

## Приёмка эпохи

Спецификация [spec.md](spec.md) остаётся источником обязательств. Design producers
не accepted до заполнения implementation packets потребителей. Приёмочные задачи
запускаются отдельными fresh sessions, которые не писали design этой эпохи и
проверяемую реализацию; Astra в имени не означает того же исполнителя.
Координатор проверяет author/reviewer session IDs и final code validation по
[общим правилам](../task-execution.md), фиксирует пять verdicts [handoff](../handoff-template.md).
Каждый недоказанный пункт имеет failed/not-run/deferred outcome и причину.
Закрытие отдельных задач не означает закрытие эпохи или public activation.
