# E1 — Первый disk cache MVP: карта выполнения

- Статус плана: **ready-for-review, 2026-10-07**.
- Статус исполнения: **planned; реализация не начата**.
- Зависимость эпохи: [E0](../epoch-0-snapshot-roundtrip/README.md), принятый применимый scope и final validation.
- Результат: Opt-in disk restore в manager, стартовая проверка, текущие watcher’ы, безопасная правка .cs и fallback.
- Канон: [spec.md](spec.md); общий scope — [README серии](../README.md).
- Проверка подготовки: [preparation-report.md](../preparation-report.md).

## Задачи

- [task-00 — Manifest значимых входов](task-00-input-manifest-hi.md) — `hi`, **planned**; Depends on: [E0/task-07](../epoch-0-snapshot-roundtrip/task-07-roundtrip-benchmark-med.md)
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

- Охват: task-00–task-10; выполненных/проверенных задач пока нет.
- Фактические исполнители: —.
- Фактические рецензенты: —.
- Сумма раундов ревью задач: —.
- Исправленные замечания задач: —.
- Отчёты: —.

## Приёмка реализации эпохи

- Модели, проводившие ревью: —.
- Даты ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Итог: не проверено; реализация не начата.
- Принятый scope и дата: —.
- Финальная validation и benchmark: —.
- Ограничения и deferred scope: —.
- Отчёты: —.

Координатор принимает только результат spec с актуальными tests/benchmark,
синхронизирует tasks/index/reports и делает commit. Принятый план и отдельный
isolated test не означают принятия реализации. Требования следующей эпохи
не являются условием приёмки уже выполненного scope этой эпохи.
