# E0/task-07 — независимое ревью выполнения, round 2

Дата: **2026-10-09**, Europe/Moscow. Рецензент: **Codex (GPT-6; точный runtime model ID недоступен)**,
та же независимая от исполнителя сессия, без subagents. Исполнитель: Grok 4.7.
Проверенная версия: HEAD `09cb8ea2f8a82119894384774578cd5b11d3f0f2` плюс исправленный
незакоммиченный task-07 runner/harness/docs. Точная версия проверенных файлов:
[source-identity.json](task-07-review-round-2/source-identity.json).
Посторонние WorkspaceLoadCache/tooling изменения в verdict не входят.

Итог: **ACCEPT**. T07-R1-001/002/003 **закрыты**; открытых must-fix findings: **0**.
Подтверждённых исправлений после ревью: **3**. Новых блокирующих замечаний нет.
Принятый scope: bounded SamplePlugin ordinary/isolated restore benchmark runner,
его child admission, раздельные timings/memory/raw evidence и предложение E1.
Production readiness, production cache activation и приёмка E0 не объявлены.

## Перепроверка findings

### T07-R1-001 — CLOSED

Python до warmup отказывает query/full name/file вне принятого SamplePlugin control.
Ожидаемое объявление передаётся restore-процессу, C# проверяет accepted control,
а Python дополнительно сверяет structured declaration до `success`/median admission.
Расширение поддержанного профиля не выполнено и для закрытия замечания не требуется.

Повторена команда прежнего воспроизведения с `IRoslynMcpPlugin`: **exitCode=1**,
отказ до создания output-каталога. Несовпадающие full name/file в structured
declaration не принимаются. Новый полный положительный прогон содержит пять
ordinary и пять restore measured successes с ожидаемым `SamplePlugin` и одним
compare key; все возвращённые имена и абсолютные файлы сверены.
[Negative checks](task-07-review-round-2/checks.json),
[stderr](task-07-review-round-2/wrong-query-stderr.txt),
[positive checks](task-07-review-round-2/positive-checks.json).

### T07-R1-002 — CLOSED

Перед запуском удаляются прежние role stages/PID/TRX; новый UUID run id передаётся
child и записывается C# в receipt. Приёмка проверяет exit code, timeout, TRX
total=1/passed=1/failed=0/notExecuted=0, role/run id и согласованный receipt PID.
Exit/timeout/TRX/run id сохранены в restore attempt и large-target JSON.

В новый handoff заранее помещены реальные старые capture stages/PID/TRX, затем
запущен zero-test child: exitCode **0**, TRX total **0**, stages и PID **null**,
outcome **error**. Прежний false success не воспроизводится.
Отдельно проверены nonzero exit, timeout, отсутствующий TRX, failed/skipped test,
чужой run id/role/PID: outcomes **error** или **timeout**, success не принимается.
В реальном положительном прогоне все 12 child receipts имеют разные run id,
согласованные PID/role и успешные TRX; large child тоже имеет валидный TRX.
[Checks](task-07-review-round-2/checks.json),
[zero-test TRX](task-07-review-round-2/zero-test/capture.trx),
[воспроизводящий скрипт](task-07-review-round-2/recheck.py).

### T07-R1-003 — CLOSED

Раннер пишет `run.txt`. Исходный журнал сохранён под этим именем, байты совпадают
с локальным `run.log`: SHA-256
`46ec55ebe61529c8e6bb4e07026e38ef6f8a58eeabfc4a59efc66feea15c8a9b`.
`git check-ignore` для `task-07/run.txt` возвращает **1** (файл не игнорируется).
Benchmark report ссылается на существующий `run.txt`. Новый положительный прогон
также сохраняет журнал под неигнорируемым именем.
[Исходный журнал](task-07/run.txt), [новый журнал](task-07-review-round-2/positive/run.txt).

## Положительный прогон исправленного раннера

Контроль и expected query те же: `samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj`,
`SamplePlugin`, `RoslynMcpPlugin.SamplePlugin`, исходный абсолютный файл.
Release publish уже выполнен; в attempt timers не включён. OS file cache не сбрасывался.
Исходный task-07 summary и его числа не переписаны. Новый прогон хранится отдельно:
[summary](task-07-review-round-2/positive/summary.json),
[проверки](task-07-review-round-2/positive-checks.json).

- Один warmup на ветку исключён; ordinary **5/5**, restore **5/5** successes,
  errors/timeouts **0**, **partial=false**.
- Compare key `e91ceca993df22cf067315714b2a41a94e455914d80a475fe9a6125196cf09df`
  одинаков у обеих веток; ключ нового прогона учитывает изменившийся dirty inventory.
- Все 18 ordinary/capture/restore PID различны. Все 12 child TRX:
  total=1, passed=1, failed=0, notExecuted=0; свежие UUID, PID/role и declarations сверены.
- Median/min/max основных timings пересчитаны из raw. Ordinary useful median
  **2294.3 ms**, isolated restore useful median **1364.7 ms**. Capture раскрыт
  отдельно; эти числа не объявляются production cache hit или E1 speedup.
- BTCPay снова отдельно **unsupported**, `generator-dependent-scope-unsupported`,
  PID **64068**, exit code **0**, TRX **1/1/0/0**, в медиану контроля не входит.
- Все attempt/stages/TRX/text outputs сохранены в `positive/`; binary envelopes
  остаются в `TestResults/task-07-review-round-2-positive/` и в review-копию не включены.
  H-001/H-002, memory method, not-measured stages и отсутствие p95 сохраняются.

## Validation и статус

- После последней C# правки исполнителя повторно выполнен MCP `run_dotnet_build`
  **решения**, Release: **passed**. Существующие warnings CS8603, CS8601, xUnit1031.
- MCP `run_test_by_filter`, main project, Release, `Category!=AnalyzerLifecycle`,
  `noBuild=true`, `noRestore=true`, timeout 1200 с: **1359 total / 1359 passed / 0 failed**;
  skips отсутствуют (Total=Passed). SourceStructure включён в main suite.
  [Build/test outputs](task-07-review-round-2/validation.json).
- AnalyzerLifecycle **not-run / not required**: production lifecycle/load/prepare/
  publication, lifecycle host и его config этой задачей не изменены. Exact structural
  limit inventory не менялся. Рецензент production/test C# не редактировал.
- Shell child checks выполнены вне sandbox с разрешением auto-review, поскольку
  ранее sandbox препятствовал локальным named pipes. Отказа auto-review не было.
- SHA-256 проверенных runner/harness/style файлов после validation совпадает с
  сохранённой identity. Документальные links/ignore/status checks и `git diff --check`
  выполнены после bookkeeping.

Task-07 **accepted, round 2, 2026-10-09** для указанного bounded scope.
Задача, README эпохи, сводки и benchmark report синхронизированы. E0 как целая
эпоха остаётся без отдельной приёмки; S-002 и production activation не присваиваются.
