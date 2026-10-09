# E0/task-07 — независимое ревью выполнения, round 1

Исторический результат round 1 сохранён ниже. Исправления T07-R1-001/002/003
подтверждены [round 2, 2026-10-09](task-07-review-round-2.md): ACCEPT, открытых findings 0.

Дата: **2026-10-09**, Europe/Moscow. Рецензент: **Codex (GPT-6; точный runtime model ID недоступен)**,
отдельная от исполнителя сессия, без subagents. Исполнитель: Grok 4.7.
Проверенная версия: HEAD `09cb8ea2f8a82119894384774578cd5b11d3f0f2` плюс незакоммиченные
task-07 runner/harness/docs/evidence. Посторонние WorkspaceLoadCache/tooling изменения
в verdict не входят.

Итог: **требуются исправления**. Открытых must-fix findings: **3**.
Исправлений после ревью: **0**. Задача остаётся **review**; принятый scope task-07: **—**.
Приёмка эпохи и production readiness не объявлены. Исходный прогон SamplePlugin
подтверждён как наблюдение; надёжность поставляемого раннера пока не принята.

## Проверенный scope

Прочитаны задача, spec/README E0, общие README/benchmark/execution, outputs task-01
и task-06, code-style целиком. Проверены `roundtrip_benchmark.py`, изменения README
раннера, `IsolatedRestoreAttempt.cs`, `IsolatedRestoreBenchmarkTests.cs`, вызываемые
`OrdinaryWorkspaceCapture`/codec/hydrator и task-07 raw evidence.
Рецензент не исправлял production/test C# или раннер. Добавлены только отчёт,
воспроизведения и учёт ревью; исходные task-07 JSON не переписаны.

## T07-R1-001 — P1: другой query принимается как успешная сопоставимая restore-попытка

**Место:** [IsolatedRestoreAttempt.cs](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/IsolatedRestoreAttempt.cs),
`Query`, строки 172–202; [roundtrip_benchmark.py](../../../../scripts/workspace-state-cache/roundtrip_benchmark.py),
restore record, строки 369–371 и `stamp`, строка 394.

**Контракт:** benchmark требует заранее выбранного query/expected result, query miss
не считается полезным ответом; обе ветки сравниваются на одном подтверждённом ключе.

**Evidence:** CLI принимает `--query`, `--expect-full-name`, `--expect-file`, передаёт
их ordinary и пишет их в общий ключ. Restore не получает эти параметры: он всегда
ищет `RoslynMcpPlugin.SamplePlugin` и проверяет суффикс `RoslynMcpPlugin/SamplePlugin.cs`.
Python не сверяет возвращённое `declaration` с ожидаемым объявлением.

В полном проверочном прогоне на том же workspace переданы `IRoslynMcpPlugin`,
`RoslynMcpServer.Plugins.IRoslynMcpPlugin`, `Plugins/IRoslynMcpPlugin.cs`.
Все пять ordinary-попыток вернули этот интерфейс; все пять restore-попыток вернули
`RoslynMcpPlugin.SamplePlugin`. У каждой пары один compareKeyId, все restore включены
в медиану, итог **partial=false**, successes **5/5** в каждой ветке.
[Проверка пяти пар](task-07-review-round-1/wrong-query/mismatch-check.json),
[summary](task-07-review-round-1/wrong-query/summary.json),
[ordinary attempt 1](task-07-review-round-1/wrong-query/ordinary/attempt-01.json),
[restore attempt 1](task-07-review-round-1/wrong-query/restore/attempt-01.json).
Все attempt/stages/TRX/text outputs проверочного прогона сохранены рядом; binary
envelope в review-копию не включён. Прогон проверяет admission, его времена не
подменяют исходные числа task-07.

**Поправка:** либо передать и проверить реальный expected query во второй ветке,
либо до любых замеров отказать всем параметрам вне принятого SamplePlugin control.
Сверять structured declaration до `success`/median admission. Расширять E0 profile
ради этого не требуется. Ответ исполнителя: pending; recheck: not-run.

## T07-R1-002 — P1: старый stages-файл превращает zero-test или failed child в success

**Место:** [roundtrip_benchmark.py](../../../../scripts/workspace-state-cache/roundtrip_benchmark.py),
`run_child`, строки 87, 129–138; `child_outcome`, строки 142–151.

**Контракт:** новый PID и actual successful attempt обязательны; zero-test не pass,
failures/timeouts сохраняются и делают итог partial.

**Evidence:** каталог `--out` можно использовать повторно; handoff-пути детерминированы,
`mkdir(exist_ok=True)` не удаляет старые receipts. `run_child` читает любой существующий
`role-stages.json`, не проверяет TRX или связь receipt с текущим запуском.
`child_outcome` возвращает outcome из stages раньше проверки ненулевого exit code.
Исходные `exitCode`/`timedOut` дочерних процессов также не сохраняются в restore attempt JSON.

Воспроизведение: скопирован настоящий прежний capture receipt PID **65072**;
запущен новый `dotnet vstest` с фильтром, не совпадающим ни с одним тестом.
Exit code **0**, TRX **total=0 / executed=0 / passed=0**, stages-файл не изменился,
но `child_outcome` вернул **success** с прежним PID. Отдельный вызов с
`exitCode=1` и `stages.outcome=success` также возвращает **success**.
[checks.json](task-07-review-round-1/checks.json),
[zero-test TRX](task-07-review-round-1/zero-test/capture.trx),
[stdout](task-07-review-round-1/zero-test/capture-stdout.txt),
[воспроизводящий скрипт](task-07-review-round-1/reproduce.py).

**Поправка:** уникальный каталог/идентификатор запуска или строгий отказ повторного
использования старых receipts; обязательные exitCode=0 и child TRX total=1,
passed=1, failed=0, notExecuted=0; свежий receipt с ожидаемой ролью и PID.
Сохранять child exit/timeout/TRX outcome в attempt. Ответ исполнителя: pending;
recheck: not-run. Дефект не означает, что исходные успешные TRX были нулевыми.

## T07-R1-003 — P2: журнал исходного прогона не входит в сохраняемый Git evidence

**Место:** [roundtrip_benchmark.py](../../../../scripts/workspace-state-cache/roundtrip_benchmark.py),
строка 222; [task-07-benchmark.md](task-07-benchmark.md), ссылка на `task-07/run.log`.

**Контракт:** отчёт должен ссылаться на сохранённые raw evidence; наличие локального
файла не обеспечивает его наличие в версии результата.

`git check-ignore -v .../task-07/run.log` возвращает **.gitignore:25:*.log**.
Файл не tracked, в untracked inventory для обычного добавления отсутствует.
При коммите незакоммиченной задачи ссылка потеряет журнал. Та же проблема task-01
уже была закрыта в round 3 сохранением `run.txt`.

**Поправка:** сохранять журнал под неигнорируемым именем, например `run.txt`, и
обновить ссылки либо явно обеспечить его versioning. Ответ исполнителя: pending;
recheck: not-run. Текущий локальный журнал прочитан, его отсутствие на диске не заявляется.

## Подтверждённые результаты и validation

- Исходный compareKeyId пересчитан для всех 12 ordinary/restore attempts и совпал.
  Основные load/semantic/useful и read/decode/hydrate/query median/min/max пересчитаны
  из пяти measured attempts, совпали с summary. Warmup исключён; 18 PID различны;
  все 12 исходных child TRX: total=1, passed=1, failed=0, notExecuted=0.
  [Проверки](task-07-review-round-1/checks.json).
- Capture overhead раскрыт; internal codec round-trip остаётся в hydrate;
  capture исключён из isolated useful и не представлен как production hit.
  Методы измерения памяти и not-measured limits раскрыты. P95 не обещан.
- BTCPay отдельно unsupported (`generator-dependent-scope-unsupported`),
  envelope не записан, в контрольную медиану не входит. E1 proposal сохраняет
  whole-request fallback и не включает defaults. Production cache не активирован.
- Повторный `run_dotnet_build` **решения**, Release: **passed**. Существующие
  warnings CS8603, CS8601, xUnit1031; ошибок нет.
- Повторный `run_test_by_filter`, main project, Release, `Category!=AnalyzerLifecycle`,
  `noBuild=true`, `noRestore=true`, timeout 1200 с: **1359 total / 1359 passed / 0 failed**;
  skips отсутствуют (Total=Passed). [Ответ MCP runner](task-07-review-round-1/main-suite.json).
  SourceStructure включён в main suite; structural limits не менялись.
- AnalyzerLifecycle **not-run / not required**: lifecycle/production paths и их
  host/config в task-07 не менялись. Production C# рецензент не редактировал.
- Первые shell-воспроизведения внутри sandbox не смогли подключить MSBuild pipe,
  zero-test child завершён по timeout; они не являются functional failures
  task-07. Остановленный проверочный benchmark сохранён в
  `TestResults/task-07-review-round-1-wrong-query/`. Повтор вне sandbox разрешён
  auto-review и дал перечисленные доказательства. Отказа auto-review не было.
- `git diff --check`: passed. Документальные статусы синхронизированы как
  **review / требуется исправление**, round 1; код и исходные тайминги не исправлены.

Числа исходного SamplePlugin-прогона не опровергнуты. Приёмка задачи требует
закрытия T07-R1-001/002/003 и повторного review/применимой validation.
