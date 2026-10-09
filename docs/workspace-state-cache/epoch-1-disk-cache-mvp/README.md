# E1 — Первый disk cache MVP: карта выполнения

- Статус плана: **spec-v2 / revised-for-review, 2026-10-07**.
- Статус исполнения: **task-11 accepted, round 3, 2026-10-09 — bounded test-only S-002 partial experiment; task-00 blocked по insufficient manifest evidence; production реализация и приёмка эпохи не выполнены**. [Review/validation](evidence/task-11-recheck-validation.md).
- Зависимость эпохи: [E0](../epoch-0-snapshot-roundtrip/README.md), принятый применимый scope и final validation.
- Результат: Opt-in disk restore в manager, стартовая проверка, текущие watcher’ы, безопасная правка .cs и fallback.
- Канон: [spec.md](spec.md); общий scope — [README серии](../README.md).
- Проверка подготовки: [preparation-report.md](../preparation-report.md).

## Задачи

- [task-00 — Manifest значимых входов](task-00-input-manifest-hi.md) — `hi`, **blocked, 2026-10-09 / S-002 insufficient all-instance import/restore evidence; DTO/adapter не реализован**; Depends on: [E0/task-07](../epoch-0-snapshot-roundtrip/task-07-roundtrip-benchmark-med.md), [E1/task-11 — S-002](task-11-raw-import-restore-evidence-hi.md). [Dependency evidence/validation](evidence/task-11-recheck-validation.md).
- [task-01 — Проверка текстовых входов и каталогов](task-01-text-and-directory-probe-med.md) — `med`, **planned**; Depends on: [E1/task-00](task-00-input-manifest-hi.md)
- [task-02 — Облегчённая проверка бинарей](task-02-binary-fast-probe-med.md) — `med`, **planned**; Depends on: [E1/task-00](task-00-input-manifest-hi.md)
- [task-03 — Минимальный атомарный store](task-03-atomic-envelope-store-med.md) — `med`, **planned**; Depends on: [E1/task-00](task-00-input-manifest-hi.md)
- [task-04 — Минимальный host ownership seam](task-04-manager-host-ownership-hi.md) — `hi`, **planned**; Depends on: [E0/task-07](../epoch-0-snapshot-roundtrip/task-07-roundtrip-benchmark-med.md)
- [task-05 — Preparation новой hydrated сессии](task-05-fresh-session-preparation-hi.md) — `hi`, **planned**; Depends on: [E1/task-00](task-00-input-manifest-hi.md), [E1/task-04](task-04-manager-host-ownership-hi.md)
- [task-06 — Disk lookup и capture в load](task-06-load-save-and-restore-hi.md) — `hi`, **planned**; Depends on: [E1/task-01](task-01-text-and-directory-probe-med.md), [E1/task-02](task-02-binary-fast-probe-med.md), [E1/task-03](task-03-atomic-envelope-store-med.md), [E1/task-04](task-04-manager-host-ownership-hi.md), [E1/task-05](task-05-fresh-session-preparation-hi.md)
- [task-07 — Watcher attachment и publication](task-07-existing-watcher-attachment-hi.md) — `hi`, **planned**; Depends on: [E1/task-06](task-06-load-save-and-restore-hi.md)
- [task-08 — Правка .cs на hydrated host](task-08-hydrated-csharp-write-hi.md) — `hi`, **planned**; Depends on: [E1/task-07](task-07-existing-watcher-attachment-hi.md)
- [task-09 — Production restart/failure проверки](task-09-restart-and-failure-integration-med.md) — `med`, **planned**; Depends on: [E1/task-07](task-07-existing-watcher-attachment-hi.md), [E1/task-08](task-08-hydrated-csharp-write-hi.md)
- [task-10 — Бенчмарк первого MVP](task-10-mvp-benchmark-med.md) — `med`, **planned**; Depends on: [E1/task-09](task-09-restart-and-failure-integration-med.md), [E0/task-01](../epoch-0-snapshot-roundtrip/task-01-baseline-runner-med.md)

- [task-11 — S-002 — actual import/restore evidence](task-11-raw-import-restore-evidence-hi.md) — `hi`, **accepted, round 3, 2026-10-09 / bounded partial experiment; supported real manifest readiness blocked**; Depends on: [E0/task-07](../epoch-0-snapshot-roundtrip/task-07-roundtrip-benchmark-med.md). [Evidence](evidence/task-11-raw-evidence.md), [review/validation](evidence/task-11-recheck-validation.md).

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

- Охват: task-11 accepted для bounded partial experiment; task-00 blocked, остальные task-01–task-10 planned.
- Фактические исполнители: Codex implementer subagents; точный runtime model ID/reasoning не подтверждены инструментами.
- Фактические рецензенты: Codex independent reviewer subagents; точный runtime model ID/reasoning не подтверждены инструментами.
- Сумма раундов ревью задач: 3 (task-11).
- Исправленные замечания задач: 2 (T11-R1-001, T11-R2-001), открытых must-fix task-11: 0.
- Отчёты: [source](evidence/task-11-raw-evidence.md), [round 2](evidence/task-11-review-round-2.md), [round 3](evidence/task-11-review-round-3.md), [final validation](evidence/task-11-recheck-validation.md).

## Приёмка реализации эпохи

- Модели, проводившие ревью: —.
- Даты ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Итог: эпоха не принята; выполнен только isolated task-11 partial experiment, production реализация не начата; task-00 blocked.
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
S-001 accepted для isolated base-query slice; S-002 experiment accepted, round 3,
2026-10-09, с partial/insufficient outcome и manifest readiness blocked
([review/validation](evidence/task-11-recheck-validation.md)). Closed disputes и optional E3 scope не переоткрываются.

Task-11/S-002 подтверждает evidence source до принятия task-00 manifest adapter.
Negative/insufficient report не открывает reusable envelope; при отсутствии
supported real positive E1 остаётся blocked по manifest scope.
