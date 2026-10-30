# Эпоха 1 — уровень 0 и инструмент `estimate_affected_tests`

- Статус плана: **задачи заведены 2026-10-30; ревью плана эпохи не проводилось**.
- Статус исполнения: **planned, код не начат**.
- Зависимость эпохи: нет.
- Результат: подсказка о затронутых тестах на уровне проектов с момента загрузки,
  baseline-снимок с фиксацией текстов, состав ответа с новыми и изменёнными тестами,
  инструмент в `core` с флагом `run` для VSTest-проектов. Граф вызовов (уровень 1) в
  эту эпоху не входит.
- MTP (2026-10-30): в проектах владельца не используется, поэтому эпоха 1 не ждёт
  раннер `mtp` программы [test-runner-selection](../../test-runner-selection/README.md),
  код которой не начат. План и исполнение MTP-проекта — отложенная
  [task-10](task-10-run-mtp-plan-med.md), вне приёмки эпохи.
- Канон: [программа rapid TIA](../README.md) — решения R-01…R-15, разделы «Дизайн»,
  «Поверхность инструментов», «Ответ инструмента», «Проверка». Задачи не меняют
  требований программы; новый существенный вопрос возвращается в программу точечной
  правкой.

## Задачи

Принцип нарезки: одна задача — одно проверяемое поведение со своими тестами.
Промежуточные задачи делают внутренние сервисы; инструмент появляется в `core` одной
задачей, когда уже даёт ответ.

- [task-01 — Модель baseline-снимка](task-01-baseline-snapshot-model-med.md) — `med`, **planned**; Depends on: —
- [task-02 — Сравнение с baseline-снимком](task-02-change-detection-med.md) — `med`, **planned**; Depends on: [task-01](task-01-baseline-snapshot-model-med.md)
- [task-03 — Жизненный цикл baseline в сессии](task-03-baseline-lifecycle-hi.md) — `hi`, **planned**; Depends on: [task-01](task-01-baseline-snapshot-model-med.md)
- [task-04 — Уровень 0: путь → проекты](task-04-level0-path-mapping-med.md) — `med`, **planned**; Depends on: —
- [task-05 — Состав ответа](task-05-response-composition-med.md) — `med`, **planned**; Depends on: [task-02](task-02-change-detection-med.md), [task-04](task-04-level0-path-mapping-med.md)
- [task-06 — Проба имён тестов в адаптерах VSTest](task-06-adapter-test-names-probe-low.md) — `low`, **planned**; Depends on: —
- [task-07 — План запуска по тестовому проекту](task-07-run-plan-med.md) — `med`, **planned**; Depends on: [task-05](task-05-response-composition-med.md), [task-06](task-06-adapter-test-names-probe-low.md)
- [task-08 — Инструмент `estimate_affected_tests`](task-08-estimate-tool-hi.md) — `hi`, **planned**; Depends on: [task-03](task-03-baseline-lifecycle-hi.md), [task-05](task-05-response-composition-med.md), [task-07](task-07-run-plan-med.md)
- [task-09 — Флаг `run`: сборка, VSTest и исходы](task-09-run-vstest-hi.md) — `hi`, **planned**; Depends on: [task-08](task-08-estimate-tool-hi.md)
- [task-10 — Флаг `run`: план MTP и замер `S`](task-10-run-mtp-plan-med.md) — `med`, **deferred, вне приёмки эпохи**; Depends on: [task-09](task-09-run-vstest-hi.md), эпоха 1 [test-runner-selection](../../test-runner-selection/README.md)
- [task-11 — Сквозные проверки и документация](task-11-integration-and-docs-med.md) — `med`, **planned**; Depends on: [task-08](task-08-estimate-tool-hi.md), [task-09](task-09-run-vstest-hi.md)

ID не задаёт порядок запуска: его задаёт Depends on. Задачи 01, 04 и 06 независимы и
могут идти параллельно; 03, 08 и 09 — цепочка. Приёмка эпохи — задачи 01–09 и 11.
До task-10 MTP-проекты отдельно не распознаются и запускаются тем же путём, что сейчас
у `run_*`; это существующее ограничение раннеров, а не новое.
`SolutionManager` и `TestTools` одновременно правит один исполнитель.

## Ревью плана эпохи

- Модели, проводившие ревью: —.
- Даты ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Итог: не проверено.
- Отчёты: —.

Ревью включает этот README, задачи и их зависимости вместе с программой. Отдельные
задачи на ревью не заводятся. Счётчики — по
[общему стандарту](../../README.md#учёт-задач-ревью-и-статистики).

## Статистика ревью задач

- Охват: —.
- Сумма раундов ревью задач: —.
- Исправленные замечания задач: —.
- Отчёты: —.

## Приёмка реализации эпохи

- Модели, проводившие ревью: —.
- Даты ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Итог: не проверено.
- Принятый scope и дата: —.
- Финальная validation: —.
