# BuildHost/task-05 — Настройка форка: отчёт

Задача: [task-05](../task-05-buildhost-tuning-med.md). Решения владельца 2026-10-27: пик памяти одной B и помеха
`fork-ab` закрыты без замеров, порог памяти GC9 — +15 %.

Отправная точка: сервер 1.5.17 выпущен 2026-10-27 с A + B + C по умолчанию (4 потока B) — опорная сторона
`fork-abc` этой задачи.

## Этап 0 — стенд, 2026-10-27

Изменения `spikes/BuildHostFork`:
- `Invoke-BuildHostForkMatrix.ps1`:
  - флаг `gc` в таблице вариантов: GC-копия host выбирается по нему, а не по имени `fork-ab-gc*`;
  - варианты `fork-abc-gc` (A + B + C на копии host с server GC и ConserveMemory=9), `fork-ac` и `fork-ac-trace`
    (A + C без evaluation наперёд);
  - параметр `-EvaluateAheadWorkers <int[]>`: по варианту `fork-abc-w<N>` на каждое N в тех же раундах. Стороны
    прогона — отдельный список: `-Variants` сохраняет `ValidateSet` при каждом присваивании;
  - `environment.json` записывает `evaluateAheadWorkers`.
- `MatrixSummary`: без сторон `fork` и `fork-ab` отношения считаются к `fork-abc`.

Проверка: Linux, 2 vCPU, SDK 10.0.113, `RoslynMcpServer.sln`, прогрев + один раунд; стороны original, `fork-abc`,
`fork-abc-gc`, `fork-ac`, `fork-ac-trace`, `fork-abc-w2`.
- Все варианты identical по task-00; опорная сторона в `matrix.md` — `fork-abc`.
- Полные RPC всех пяти вариантов совпадают с original с учётом порядка glob-наборов: 0 отличий.
- `fork-ac-trace/trace-summary.md` построен: часы сверены на 20 операциях, 0 вне своей операции.
- Время на этой машине о порогах не говорит (один раунд, 5 проектов).

На Linux список вариантов передаётся через `pwsh -Command`: при `pwsh -File` запись `a,b` приходит одной строкой и
не проходит `ValidateSet`. В PowerShell на Windows команды задачи работают как есть.

Этап 0 выполнен. Дальше — замеры владельца, этапы 1–3 (команды — в задаче, раздел «Запуск»).
