# Epoch 0 — Feasibility, profile и baseline — карта выполнения

Статус исполнения серии: **deferred / backlog, 2026-10-07**.
Незавершённые работы отложены; статусы задач ниже сохранены как история
ранее принятого scope. [Причина и проверка переноса](../README.md).

[Комплект v2](../README.md) · [Каноническая спецификация](spec.md) ·
[Общие правила задач](../task-execution.md)

Примерная сложность эпохи: **4/5**. Статусы ниже отражают выполнение и принятый
scope каждой задачи; приёмка задачи не утверждает shipped behavior. Модель в
имени — рекомендация; итоговая приёмка выполняется по всей спецификации.

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
Task-02 дополнил пакет task-03 observed findings 2026-10-03; положительный dispatch
остаётся hold по [результатам исследования](evidence/task-02-dependency-admission.md).
Заполненные типы и сигнатуры не заменяют недоказанные positive admission/equivalence.
Доработка dependency prerequisite выделена в
[task-02.1](task-02.1-dependency-admission-remediation-Astra.md), **in-progress
(Stage A finite proof, 2026-10-04)**. Владелец утвердил A021-01/02,
строгий source-backed subset D021-03 и negative Razor/Web scope D021-05.
Limited executable L021-1 принят после code review и final validation
(build exit 0, focused 21/21, main 1228/1228); reparse subcase NotRun.
A021-06 typed envelope утверждён; finite proof и полный positive packet
остаются gate до positive implementation. [Evidence и validation](evidence/task-02.1-dependency-admission.md).
Dependency readiness и oracle API readiness остаются отдельными hold для task-04.

## Пул задач

- [task-00 — current-baseline-and-execution-contract](task-00-current-baseline-and-execution-contract-Astra.md) — **Astra / high, 4/5**; риск: высокий; **accepted (docs-only, 2026-10-03)**.
  Depends on: нет.
  Сначала сверить текущий production baseline с историческим основанием spec; затем определить оставшуюся delta и план доказательств.
  Audit/design/четыре packets проверены; independent reviewer `/root/contract_review`,
  must-fix закрыты. [Результаты и отдельные verdicts](task-00-execution-report.md).
- [task-01 — hydrate-host-spike](task-01-hydrate-host-spike-Sol.md) — **Sol / high, 4/5**; риск: средний; **accepted (isolated experiment, 2026-10-03)**.
  Depends on: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md).
  Подтвердить выбранный AdhocWorkspace и writer .cs на публичных Roslyn API без скрытого DTB.
  Изолированный host experiment проверен; independent reviewer `/root/independent_review`,
  must-fix закрыты. [Evidence и отдельные verdicts](evidence/task-01-hydrate-host.md).
  Production lifecycle и public activation не открыты.
- CI prerequisite task-01/task-02: **SDK installation verified on GitHub; harness fix validated locally (2026-10-05)**.
  Child build очищает inherited MSBuild SDK overrides; independent SDK assertion использует fixture CLI resolution.
  Release build, hydrate **54/54**, split-SDK regression **2/2**, main **1265/1265** passed.
  [Причина падения и validation](evidence/task-01-hydrate-host.md); повторный CI после изменения harness ещё не выполнен.
- [task-02 — dependency-admission-spike](task-02-dependency-admission-spike-Astra.md) — **Astra / high, 5/5**; риск: высокий; **accepted (isolated research, 2026-10-03)**.
  Depends on: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md).
  Доказать замкнутость sdk-project-v1 и переносимую analyzer identity на выбранных источниках evidence.
  Приняты воспроизводимые отрицательные findings и supplement task-03; замкнутость
  и новый session-bound overlay не доказаны. Independent reviewer `/root/independent_review` — ACCEPT;
  Release build и main 1132/1132 passed. [Приёмка и ограничения](evidence/task-02-dependency-admission.md#независимая-приёмка).
  Положительные fixtures task-03 остаются hold; production/public activation не открыты.
- [task-02.1 — dependency-admission-remediation](task-02.1-dependency-admission-remediation-Astra.md) — **Astra / high, 5/5**; риск: высокий; **in-progress (Stage A finite proof, 2026-10-04)**.
  Depends on: E0/task-00, task-01, task-02, task-03.
  Закрыть restore/toolset/analyzer closure, exact graph/inner mapping и реальный
  fresh session-bound overlay для доказанного positive control. Workload imports,
  SDK analyzer provenance и Razor coverage сначала требуют design решения.
  Согласованно обновить consumer expectations, сохранить negative cases;
  positive pipeline/API remediation task-04 остаются отдельной работой.
  Выполнены аудит и fresh negative observations; normative owner choices
  A021-01/02/03, D021-04/05 и A021-06 typed envelope приняты.
  Limited L021-1 (семь isolated C# файлов и runner) принят independent reviewer
  и координатором 2026-10-04. Final Release build exit 0; focused **21/21**,
  main **1228/1228 passed**. Reparse subcase **NotRun** (Win32 1314);
  прежние failures и удалённый subprocess fallback сохранены в evidence.
  Accepted scope: graph observations, framework selection, bounded recheck;
  unknown/false/false и closure unproven reasons сохранены. Полный finite proof
  и positive packet в работе; supported control/fresh Complete overlay
  и полная remediation пока не приняты.
  [Evidence, review и handoff](evidence/task-02.1-dependency-admission.md).
  Production/public activation не открыты.
- [task-03 — oracle-fixtures](task-03-oracle-fixtures-Luna.md) — **Luna / medium, 2/5**; риск: средний; **accepted — isolated fixtures, negative expectations only (2026-10-04)**.
  Depends on: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md), [E0/task-01](task-01-hydrate-host-spike-Sol.md), [E0/task-02](task-02-dependency-admission-spike-Astra.md).
  Создать ограниченные фикстуры и ожидаемые данные по уже принятому плану oracle.
  13 фикстур manifest реализованы с honest-unknown admission-ожиданиями и golden-данными
  из независимых наблюдений task-02 (включая полный Razor generated text и байты obj-входов);
  positive-ожидания остаются hold. Принятый scope: пять fixture/model/test файлов,
  13 layouts, restore/cleanup regressions и task/index/evidence bookkeeping.
  Все R03-01..R03-06 закрыты; последние две стилевые правки завершены координатором.
  Final Release build exit 0, focused **48/48**, main **1180/1180 passed**, без
  failures/skips. Fixture-зависимость task-03 для task-04 закрыта; task-04
  выполнил isolated negative harness, positive dispatch остаётся hold по task-02, public activation
  не разрешена. [Финальная validation и приёмка](evidence/task-03-fixtures.md#финальная-coordinator-validation-и-приёмка-2026-10-04).
- [task-04 — fresh-msbuild-equivalence](task-04-fresh-msbuild-equivalence-Sol.md) — **Sol / high, 4/5**; риск: высокий; **blocked for positive equivalence; isolated negative oracle scope accepted (2026-10-04)**.
  Depends on: [E0/task-01](task-01-hydrate-host-spike-Sol.md), [E0/task-02](task-02-dependency-admission-spike-Astra.md), [E0/task-03](task-03-oracle-fixtures-Luna.md), [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md).
  Сравнить candidate host с независимой свежей MSBuild загрузкой.
  Приняты семь C# файлов packet-04, negative harness, comparator/reader scaffold,
  fresh ordinary controls и synthetic capabilities. Independent reviewer
  `/root/independent_review`: R04-01–R04-04 закрыты, ACCEPT ограниченного scope.
  Final Release build exit 0, focused **42/42**, main **1222/1222 passed**, без
  failures/skips. После установки SDK 9.0.314 / reference pack 9.0.16:
  negative attempts **13 Passed / 0 NotRun** (ранее 12 Passed / 1 NotRun);
  positive equivalence: **13 NotRun**. Packet stages 2–7 не реализованы/не
  исполнены до task-02 closure и public observation API remediation.
  Epoch/production/public activation не открыты.
  [Post-install validation и evidence](evidence/task-04-equivalence.md#post-install-validation-2026-10-04).
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
