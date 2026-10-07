# E0 — Snapshot round-trip: карта выполнения

- Статус плана: **spec-v2 / revised-for-review, 2026-10-07**.
- Статус исполнения: **task-00 accepted, round 2, 2026-10-07; принятый scope — docs-only inventory seams и выбор real control; остальные задачи planned; приёмка эпохи не выполнена**. [Review/validation](evidence/task-00-review-round-2.md).
- Зависимость эпохи: предшествующей эпохи нет; после принятия плана E0 preliminary inventory/schema sketch допустимы, positive executable scope требует U-001 и successful S-001.
- Результат: Рабочий capture → DTO → новый host → semantic query на реальном проекте; первый benchmark восстановления.
- Канон: [spec.md](spec.md); общий scope — [README серии](../README.md).
- Проверка подготовки: [preparation-report.md](../preparation-report.md).

## Задачи

- [task-00 — Актуальные seams и контролы](task-00-current-seams-and-controls-med.md) — `med`, **accepted / docs-only inventory и выбор контроля, 2026-10-07**; Depends on: нет. Evidence: [implementation](evidence/task-00-controls.md), [review round 2 / validation](evidence/task-00-review-round-2.md).
- [task-01 — Раннер ordinary baseline](task-01-baseline-runner-med.md) — `med`, **planned**; Depends on: [E0/task-00](task-00-current-seams-and-controls-med.md)
- [task-02 — Минимальная переносимая schema](task-02-snapshot-schema-hi.md) — `hi`, **planned**; Depends on: [E0/task-00](task-00-current-seams-and-controls-med.md), [E0/task-08 — S-001](task-08-real-control-capability-hi.md)
- [task-03 — Capture ordinary workspace](task-03-ordinary-capture-med.md) — `med`, **planned**; Depends on: [E0/task-02](task-02-snapshot-schema-hi.md), [E0/task-08 — S-001](task-08-real-control-capability-hi.md)
- [task-04 — Bounded snapshot codec](task-04-snapshot-codec-low.md) — `low`, **planned**; Depends on: [E0/task-02](task-02-snapshot-schema-hi.md), [E0/task-08 — S-001](task-08-real-control-capability-hi.md)
- [task-05 — Восстановление base host](task-05-isolated-hydrate-hi.md) — `hi`, **planned**; Depends on: [E0/task-03](task-03-ordinary-capture-med.md), [E0/task-04](task-04-snapshot-codec-low.md), [E0/task-08 — S-001](task-08-real-control-capability-hi.md)
- [task-06 — Реальный round-trip и .cs edit](task-06-real-cross-process-roundtrip-med.md) — `med`, **planned**; Depends on: [E0/task-05](task-05-isolated-hydrate-hi.md)
- [task-07 — Бенчмарк isolated restore](task-07-roundtrip-benchmark-med.md) — `med`, **planned**; Depends on: [E0/task-01](task-01-baseline-runner-med.md), [E0/task-06](task-06-real-cross-process-roundtrip-med.md)

- [task-08 — S-001 — capability реального контроля](task-08-real-control-capability-hi.md) — `hi`, **planned**; Depends on: [E0/task-00](task-00-current-seams-and-controls-med.md)

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

- Охват: task-00 — round 2, 2026-10-07, accepted для docs-only inventory seams и выбора real control; task-01–task-08 planned.
- Фактические исполнители: task-00 — Grok 4.7.
- Фактические рецензенты: task-00 — Codex (GPT-6; точный runtime model ID недоступен).
- Сумма раундов ревью задач: 2.
- Исправленные замечания задач: 2; task-00 — T00-R1-001 и T00-R1-002 подтверждены recheck.
- Отчёты: [task-00 round 1](evidence/task-00-review-round-1.md), [round 2 / validation](evidence/task-00-review-round-2.md).

## Приёмка реализации эпохи

- Модели, проводившие ревью: —.
- Даты ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Итог: не проверено; preliminary inventory task-00 принят, executable реализация не начата.
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
реализация не начата. [Change ledger](../change-ledger.md) и
[revision report](../preparation-report.md) содержат scope/validation evidence.
Historical review/defense не подменяет arbitration; independent recheck v2 ещё не выполнен.
U-001 решён [H-001/H-002](../human-decisions.md): external XML exclusion и вариант B
для strong-name; [применение и validation](../h-002-report.md), 2026-10-07.
S-001/S-002 учитываются к deadlines и остаются not-run. Closed disputes и optional E3 scope не переоткрываются.

Task-08/S-001 требует U-001; schema task-02 может подготовить sketch до gate,
но final acceptance/capture/codec/hydrate positive scope требует successful S-001.
Task-08 не зависит от полного schema/capture adapter: циклического prerequisite нет.
