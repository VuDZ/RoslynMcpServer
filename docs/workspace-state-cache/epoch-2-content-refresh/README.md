# E2 — Обновление существующих входов: карта выполнения

- Статус плана: **ready-for-review, 2026-10-07**.
- Статус исполнения: **planned; реализация не начата**.
- Зависимость эпохи: [E1](../epoch-1-disk-cache-mvp/README.md), принятый применимый scope и final validation.
- Результат: Restart после content edits без DTB; graph/membership изменения сохраняют fallback.
- Канон: [spec.md](spec.md); общий scope — [README серии](../README.md).
- Проверка подготовки: [preparation-report.md](../preparation-report.md).

## Задачи

- [task-00 — Роли стартового diff и dirty owners](task-00-startup-diff-roles-hi.md) — `hi`, **planned**; Depends on: [E1/task-10](../epoch-1-disk-cache-mvp/task-10-mvp-benchmark-med.md)
- [task-01 — Обновление existing .cs при restart](task-01-existing-source-refresh-med.md) — `med`, **planned**; Depends on: [E2/task-00](task-00-startup-diff-roles-hi.md)
- [task-02 — Обновление additional/config texts](task-02-additional-and-config-refresh-med.md) — `med`, **planned**; Depends on: [E2/task-00](task-00-startup-diff-roles-hi.md)
- [task-03 — Свежие metadata references](task-03-fresh-metadata-references-med.md) — `med`, **planned**; Depends on: [E2/task-00](task-00-startup-diff-roles-hi.md)
- [task-04 — Capture после startup refresh](task-04-refreshed-envelope-capture-med.md) — `med`, **planned**; Depends on: [E2/task-01](task-01-existing-source-refresh-med.md), [E2/task-02](task-02-additional-and-config-refresh-med.md), [E2/task-03](task-03-fresh-metadata-references-med.md)
- [task-05 — Mutation matrix для content reuse](task-05-content-and-graph-mutations-med.md) — `med`, **planned**; Depends on: [E2/task-04](task-04-refreshed-envelope-capture-med.md)
- [task-06 — Бенчмарк edit/build → restart](task-06-content-restart-benchmark-med.md) — `med`, **planned**; Depends on: [E2/task-05](task-05-content-and-graph-mutations-med.md)

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
