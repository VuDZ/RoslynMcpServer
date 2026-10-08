# E0 — Snapshot round-trip: карта выполнения

- Статус плана: **spec-v2 / revised-for-review, 2026-10-07**.
- Статус исполнения: **task-00 accepted, round 2, 2026-10-07; принятый scope — docs-only inventory seams и выбор real control; task-01 accepted, round 3, 2026-10-08 — ordinary baseline runner для контроля RoslynMcpPlugin; task-08 accepted, round 2, 2026-10-08 — isolated supported base-query slice; task-02 accepted, round 1, 2026-10-08 — schema v1 и живой isolated ordinary consumer; task-03 accepted, round 1, 2026-10-08 — ordinary capture выбранного контроля; task-04 accepted, round 3, 2026-10-08 — bounded schema-v1 codec в принятом S-001 профиле; task-05 accepted, round 2, 2026-10-08 — isolated schema-v1 Debug base-query hydrate adapter/session owner, H-001/H-002; task-06/07 planned; внешняя приёмка task-04/task-05 подтверждена владельцем 2026-10-08; приёмка эпохи не выполнена**. [Общий отчёт](evidence/task-04-05-external-acceptance.md). [Task-04 codec](evidence/task-04-codec.md), [review round 1 / validation](evidence/task-04-review-round-1.md), [review round 2 / validation](evidence/task-04-review-round-2.md), [review round 3](evidence/task-04-review-round-3.md), [final validation](evidence/task-04-validation.md). [Task-00 review/validation](evidence/task-00-review-round-2.md), [task-01 review/validation](evidence/task-01-review-round-3.md), [task-08 review/validation](evidence/task-08-review-round-2.md), [task-02 review](evidence/task-02-review-round-1.md), [task-02 final validation](evidence/task-02-validation.md), [task-03 review](evidence/task-03-review-round-1.md), [task-03 final validation](evidence/task-03-validation.md), [task-05 review round 2](evidence/task-05-review-round-2.md), [task-05 final validation](evidence/task-05-validation.md).
- Зависимость эпохи: предшествующей эпохи нет; после принятия плана E0 preliminary inventory/schema sketch допустимы, positive executable scope требует U-001 и successful S-001.
- Результат: Рабочий capture → DTO → новый host → semantic query на реальном проекте; первый benchmark восстановления.
- Канон: [spec.md](spec.md); общий scope — [README серии](../README.md).
- Проверка подготовки: [preparation-report.md](../preparation-report.md).

## Задачи

- [task-00 — Актуальные seams и контролы](task-00-current-seams-and-controls-med.md) — `med`, **accepted / docs-only inventory и выбор контроля, 2026-10-07**; Depends on: нет. Evidence: [implementation](evidence/task-00-controls.md), [review round 2 / validation](evidence/task-00-review-round-2.md).
- [task-01 — Раннер ordinary baseline](task-01-baseline-runner-med.md) — `med`, **accepted / ordinary baseline runner для контроля RoslynMcpPlugin, round 3, 2026-10-08**; Depends on: [E0/task-00](task-00-current-seams-and-controls-med.md). Evidence: [baseline](evidence/task-01-baseline.md), [review round 3 / validation](evidence/task-01-review-round-3.md).
- [task-02 — Минимальная переносимая schema](task-02-snapshot-schema-hi.md) — `hi`, **accepted / schema v1 и живой isolated ordinary consumer, round 1, 2026-10-08**; Depends on: [E0/task-00](task-00-current-seams-and-controls-med.md), [E0/task-08 — S-001](task-08-real-control-capability-hi.md). Evidence: [schema](evidence/task-02-schema.md), [review](evidence/task-02-review-round-1.md), [final validation](evidence/task-02-validation.md).
- [task-03 — Capture ordinary workspace](task-03-ordinary-capture-med.md) — `med`, **accepted / ordinary capture выбранного контроля, round 1, 2026-10-08**; Depends on: [E0/task-02](task-02-snapshot-schema-hi.md), [E0/task-08 — S-001](task-08-real-control-capability-hi.md). Evidence: [capture](evidence/task-03-capture.md), [review](evidence/task-03-review-round-1.md), [final validation](evidence/task-03-validation.md).
- [task-04 — Bounded snapshot codec](task-04-snapshot-codec-low.md) — `low`, **accepted / bounded schema-v1 codec в принятом S-001 профиле, round 3, 2026-10-08**; Depends on: [E0/task-02](task-02-snapshot-schema-hi.md), [E0/task-08 — S-001](task-08-real-control-capability-hi.md). Evidence: [codec](evidence/task-04-codec.md), [review round 1 / validation](evidence/task-04-review-round-1.md), [review round 2 / validation](evidence/task-04-review-round-2.md), [review round 3](evidence/task-04-review-round-3.md), [final validation](evidence/task-04-validation.md). Внешняя приёмка passed, 2026-10-08: [общий отчёт](evidence/task-04-05-external-acceptance.md).
- [task-05 — Восстановление base host](task-05-isolated-hydrate-hi.md) — `hi`, **accepted / isolated schema-v1 Debug base-query hydrate adapter/session owner, H-001/H-002, round 2, 2026-10-08**; Depends on: [E0/task-03](task-03-ordinary-capture-med.md), [E0/task-04](task-04-snapshot-codec-low.md), [E0/task-08 — S-001](task-08-real-control-capability-hi.md). Evidence: [hydrate](evidence/task-05-hydrate.md), [review round 2](evidence/task-05-review-round-2.md), [review round 3](evidence/task-05-review-round-3.md), [final validation](evidence/task-05-validation.md). Внешняя приёмка passed, 2026-10-08: [общий отчёт](evidence/task-04-05-external-acceptance.md).
- [task-06 — Реальный round-trip и .cs edit](task-06-real-cross-process-roundtrip-med.md) — `med`, **planned**; Depends on: [E0/task-05](task-05-isolated-hydrate-hi.md)
- [task-07 — Бенчмарк isolated restore](task-07-roundtrip-benchmark-med.md) — `med`, **planned**; Depends on: [E0/task-01](task-01-baseline-runner-med.md), [E0/task-06](task-06-real-cross-process-roundtrip-med.md)

- [task-08 — S-001 — capability реального контроля](task-08-real-control-capability-hi.md) — `hi`, **accepted / isolated base-query slice, round 2, 2026-10-08**; Depends on: [E0/task-00](task-00-current-seams-and-controls-med.md)

ID не задаёт порядок запуска: его задаёт Depends on. В одной задаче остаётся
один проверяемый adapter/helper/scenario; независимые изменения возвращаются
в отдельные задачи. Общие manager/analyzer files одновременно правит один владелец.

## Ревью плана эпохи

- Модели, проводившие ревью: —.
- Даты ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Итог: не проверено.
- Отчёты: —.

Ревью включает spec, задачи, dependencies и benchmark этой эпохи вместе с
общими документами. Отдельные review tasks не требуются. Рецензенты пишут в
[review/](../review/README.md); счётчики обновляются по [общему стандарту](../../README.md#учёт-задач-ревью-и-статистики).

## Статистика ревью задач

- Охват: task-00 — round 2, 2026-10-07, accepted для docs-only inventory seams и выбора real control; task-01 — round 3, 2026-10-08, accepted для ordinary baseline runner контроля RoslynMcpPlugin; task-08 — round 2, 2026-10-08, accepted для isolated supported base-query slice; task-02 — round 1, 2026-10-08, accepted для schema v1 и живого isolated ordinary consumer; task-03 — round 1, 2026-10-08, accepted для ordinary capture выбранного контроля; task-04 — accepted / bounded schema-v1 codec в принятом S-001 профиле, round 3, 2026-10-08; task-05 accepted, round 2, 2026-10-08 — isolated schema-v1 Debug base-query hydrate adapter/session owner, H-001/H-002; task-06/07 planned.
- Фактические исполнители: task-00 — Grok 4.7; task-01 — Grok 4.7; task-08 и task-02 — Codex (GPT-6; точный runtime model ID недоступен); task-03 — Grok 4.7; task-04 — Grok 4.7; task-05 — Codex (GPT-6 Sol), один subagent.
- Фактические рецензенты: task-00 — Codex (GPT-6; точный runtime model ID недоступен); task-01 — Grok 4.7 (три независимые сессии); task-08 — Grok 4.7; task-02 — Codex (GPT-6; точный runtime model ID недоступен), независимая сессия; task-03 — Grok 4.7, независимая сессия; task-04 — Codex (GPT-6; точный runtime model ID последующих сессий недоступен), три независимые сессии; task-05 — Codex (GPT-6 Sol), две независимые сессии, и Grok 4.7, round 3.
- Сумма раундов ревью задач: 15.
- Исправленные замечания задач: 9; task-00 — T00-R1-001 и T00-R1-002 подтверждены recheck; task-01 — T01-R1-001 подтверждён round 2, журнал переименован в `run.txt` и подтверждён round 3; task-08 — T08-R1-001 подтверждён round 2; task-02 — 0, must-fix замечаний нет; task-03 — 0, must-fix замечаний нет; task-04 — 3 исправления: T04-R1-001/002 подтверждены round 2, T04-R1-003 подтверждён round 3; task-05 — 1 исправление T05-R1-001 подтверждено round 2; round 3 новых исправлений не потребовал.
- Отчёты: [task-00 round 1](evidence/task-00-review-round-1.md), [round 2 / validation](evidence/task-00-review-round-2.md), [task-01 baseline](evidence/task-01-baseline.md), [task-01 round 1](evidence/task-01-review-round-1.md), [task-01 round 2](evidence/task-01-review-round-2.md), [task-01 round 3 / validation](evidence/task-01-review-round-3.md), [task-08 capability/validation](evidence/task-08-capability.md), [task-08 round 1](evidence/task-08-review-round-1.md), [task-08 round 2 / validation](evidence/task-08-review-round-2.md), [task-02 schema](evidence/task-02-schema.md), [task-02 review](evidence/task-02-review-round-1.md), [task-02 final validation](evidence/task-02-validation.md), [task-03 capture](evidence/task-03-capture.md), [task-03 review](evidence/task-03-review-round-1.md), [task-03 final validation](evidence/task-03-validation.md), [task-04 codec](evidence/task-04-codec.md), [task-04 review round 1 / validation](evidence/task-04-review-round-1.md), [task-04 review round 2 / validation](evidence/task-04-review-round-2.md), [task-04 review round 3](evidence/task-04-review-round-3.md), [task-04 final validation](evidence/task-04-validation.md), [task-05 hydrate](evidence/task-05-hydrate.md), [task-05 review round 1](evidence/task-05-review-round-1.md), [task-05 review round 2](evidence/task-05-review-round-2.md), [task-05 review round 3](evidence/task-05-review-round-3.md), [task-05 final validation](evidence/task-05-validation.md).

## Приёмка реализации эпохи

- Модели, проводившие ревью: —.
- Даты ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Итог: не проверено; preliminary inventory task-00 принят; task-01 accepted для ordinary baseline runner, round 3, 2026-10-08; isolated task-08 accepted для base-query slice, round 2, 2026-10-08; task-02 accepted для schema v1 и живого isolated ordinary consumer, round 1, 2026-10-08 ([validation](evidence/task-02-validation.md)); task-03 accepted для ordinary capture выбранного контроля, round 1, 2026-10-08 ([validation](evidence/task-03-validation.md)). Task-04 accepted для bounded schema-v1 codec в принятом S-001 профиле, round 3, 2026-10-08 ([review](evidence/task-04-review-round-3.md), [final validation](evidence/task-04-validation.md)); Task-05 accepted для isolated schema-v1 Debug base-query hydrate adapter/session owner, H-001/H-002, round 2, 2026-10-08, подтверждён round 3 без новых must-fix ([review round 2](evidence/task-05-review-round-2.md), [review round 3](evidence/task-05-review-round-3.md), [final validation](evidence/task-05-validation.md)); cross-process round-trip и приёмка эпохи не выполнены.
- Принятый scope и дата: —.
- Финальная validation и benchmark: —.
- Ограничения и deferred scope: —.
- Отчёты: —.

Координатор принимает только результат spec с актуальными tests/benchmark,
синхронизирует tasks/index/reports и делает commit. Принятый план и отдельный
isolated test не означают принятия реализации. Требования следующей эпохи
не являются условием приёмки уже выполненного scope этой эпохи.

## Revision scope и gates

**2026-10-07: spec-v2 revised-for-review**, применение arbitration к тексту,
на момент revision реализация не была начата; текущий execution status указан выше. [Change ledger](../change-ledger.md) и
[revision report](../preparation-report.md) содержат scope/validation evidence.
Historical review/defense не подменяет arbitration; independent recheck v2 ещё не выполнен.
U-001 решён [H-001/H-002](../human-decisions.md): external XML exclusion и вариант B
для strong-name; [применение и validation](../h-002-report.md), 2026-10-07.
S-001 accepted 2026-10-08 для isolated base-query slice, round 2; [report](evidence/task-08-capability.md), [review](evidence/task-08-review-round-2.md). S-002 остаётся not-run. Closed disputes и optional E3 scope не переоткрываются.

Task-08/S-001 требует U-001; schema task-02 может подготовить sketch до gate,
но final acceptance/capture/codec/hydrate positive scope требует successful S-001.
Task-08 не зависит от полного schema/capture adapter: циклического prerequisite нет.
