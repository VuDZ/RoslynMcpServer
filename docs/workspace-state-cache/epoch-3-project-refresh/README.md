# E3 — Выборочное обновление проектов: карта выполнения

- Статус плана: **ready-for-review, 2026-10-07**.
- Статус исполнения: **planned; реализация не начата**.
- Зависимость эпохи: [E2](../epoch-2-content-refresh/README.md), принятый применимый scope и final validation.
- Результат: Dirty project reload через MSBuild с сохранением остальных проектов, если narrow spike это подтвердит.
- Канон: [spec.md](spec.md); общий scope — [README серии](../README.md).
- Проверка подготовки: [preparation-report.md](../preparation-report.md).

## Задачи

- [task-00 — Узкий spike project reopen](task-00-single-project-reopen-spike-hi.md) — `hi`, **planned**; Depends on: [E2/task-06](../epoch-2-content-refresh/task-06-content-restart-benchmark-med.md)
- [task-01 — Dirty и dependency mapping](task-01-dirty-project-dependency-map-med.md) — `med`, **planned**; Depends on: [E3/task-00](task-00-single-project-reopen-spike-hi.md)
- [task-02 — Adapter selective MSBuild reopen](task-02-project-reopen-adapter-hi.md) — `hi`, **planned**; Depends on: [E3/task-00](task-00-single-project-reopen-spike-hi.md), [E3/task-01](task-01-dirty-project-dependency-map-med.md)
- [task-03 — Create/delete/rename → project reload](task-03-membership-change-routing-med.md) — `med`, **planned**; Depends on: [E3/task-01](task-01-dirty-project-dependency-map-med.md), [E3/task-02](task-02-project-reopen-adapter-hi.md)
- [task-04 — Целая publication selective candidate](task-04-selective-candidate-publication-xhi.md) — `xhi`, **planned**; Depends on: [E3/task-02](task-02-project-reopen-adapter-hi.md), [E3/task-03](task-03-membership-change-routing-med.md)
- [task-05 — Selective integration regressions](task-05-selective-refresh-regressions-med.md) — `med`, **planned**; Depends on: [E3/task-04](task-04-selective-candidate-publication-xhi.md)
- [task-06 — Бенчмарк selective refresh](task-06-selective-refresh-benchmark-med.md) — `med`, **planned**; Depends on: [E3/task-05](task-05-selective-refresh-regressions-med.md)

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

- Охват: task-00–task-06; выполненных/проверенных задач пока нет.
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
