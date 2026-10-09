# Ordinary baseline runner

Новый процесс, ordinary `load_workspace` и первый полезный `find_symbol_definition`.
Архивный `docs/backlog/workspace-load-cache/baseline_bench.py` этот раннер не импортирует и не меняет.
Его пины (`SPEC_TOKEN`, SHA, таймауты, состав median, `CORPORA`) остаются как есть.

Протокол чисел — [benchmark.md](../../docs/workspace-state-cache/benchmark.md).
Идентификатор этого клиента: `e0-ordinary-baseline-1`. Это не `baseline-benchmark-4`.

## Аргументы

Обязательны путь workspace, имя query и exe сервера. Контрольный проект в код не зашит.

```powershell
py -3 scripts/workspace-state-cache/ordinary_baseline.py `
  --workspace <path-to.sln-or.csproj> `
  --query <symbolName> `
  --server <path-to-RoslynMcpServer.exe> `
  --expect-full-name <Namespace.Type> `
  --expect-file <relative-or-absolute-declaration-file> `
  --out <raw-attempt-directory> `
  --repo <repository-root> `
  --publish-record <publish.json>
```

| Аргумент | Смысл |
|---|---|
| `--workspace` | Файл для `load_workspace`. `.slnf` отвергается. |
| `--query` | `symbolName` для `find_symbol_definition`. Файл и строка не передаются. |
| `--server` | Уже опубликованный `RoslynMcpServer.exe`. Раннер сам не публикует. |
| `--expect-full-name` | Полное имя объявления, без которого ответ не полезный. |
| `--expect-file` | Файл этого объявления. Промах не входит в медиану. |
| `--out` | Каталог raw JSON. |
| `--repo` | Корень репозитория для git, cwd процесса и `ROSLYN_MCP_WORKSPACE`. |
| `--publish-record` | JSON публикации. В тайминги попыток не прибавляется. |
| `--measured` | Сколько успешных измеряемых попыток собрать. Минимум 5. |
| `--max-measured` | Потолок измеряемых попыток вместе с неуспехами. По умолчанию 8. |

Пример принятого контроля (не значение по умолчанию):

```powershell
py -3 scripts/workspace-state-cache/ordinary_baseline.py `
  --workspace E:\Devel\RoslynMcpServer\samples\RoslynMcpPlugin\RoslynMcpPlugin.csproj `
  --query SamplePlugin `
  --server E:\Devel\RoslynMcpServer\bin\Release\net10.0\win-x64\publish\RoslynMcpServer.exe `
  --expect-full-name RoslynMcpPlugin.SamplePlugin `
  --expect-file samples/RoslynMcpPlugin/SamplePlugin.cs `
  --repo E:\Devel\RoslynMcpServer `
  --out docs\workspace-state-cache\epoch-0-snapshot-roundtrip\evidence\task-01
```

Ожидаемый успех этого примера: объявление `RoslynMcpPlugin.SamplePlugin` в `samples/RoslynMcpPlugin/SamplePlugin.cs`.

## Что внутри таймера

На каждую попытку — новый процесс exe.

- `loadMs` — от отправки `tools/call load_workspace` до ответа. Свой duration инструмент не возвращает.
- `semanticMs` — от отправки `tools/call find_symbol_definition` до ответа.
- `usefulMs` = `loadMs + semanticMs`, только когда load успешен и в ответе есть ожидаемое объявление.
- Первая компиляция сидит внутри этих двух вызовов и не вычитается.

`configuration`, `platform`, `targetFramework`, `buildArgs` и `shadowCopyInSolutionAnalyzers` не передаются.
`briefOutput=true`. Кадр — одна JSON-строка, как у архивного клиента. `initialize` использует `2025-11-25` (handshake текущих хостов этого сервера).

## Что снаружи таймера

- Publish Release-сервера. Старт и конец пишутся отдельно (`publish.json`) до первой попытки.
- Restore корпуса. Этот раннер restore не запускает. Контроль уже на диске.
- `startupMs`: от старта процесса до ответа `initialize`. В load, semantic и useful не входит.
- Сбор compare key и хеш исходников для identity. Это не стадия load.
- Файловый кэш ОС не сбрасывается. Сценарий — new-process/warm-files, не cold-I/O и не RAM-hit.

Один warmup в медиану не входит. Дальше измеряемые попытки. Медиану, min и max считает раннер по успешным measured-попыткам с одним compare key. Любой измеренный неуспех помечает итог `partial` и остаётся в raw JSON. p95 раннер не считает и не обещает.

Стадии input enumeration, stat, text hashing, binary sample/read, decode, hydrate, prepare, publication, capture и write в этом прогоне `not-measured`, не ноль.

Таймауты этого клиента: initialize 120 с, load 600 с, semantic 600 с. Это не пины архивного baseline.

## Isolated restore на том же compare key

`roundtrip_benchmark.py` повторяет ordinary-попытки и добавляет isolated restore.
Оба сценария получают один compare key: тот же сервер, commit, content identity, query и уровни configuration/platform/TFM.
Поле сценария внутри ключа — метка пары, не отдельный ключ на каждую ногу.
Production cache этот раннер не включает.

На каждую restore-попытку — два новых процесса тестовой сборки:

- `capture`: ordinary MSBuild load (`ordinaryLoadMs`), `CaptureAsync` (`captureMs`), encode и запись envelope (`writeMs`). Сумма — `captureOverheadMs`.
- `restore`: другой PID читает envelope (`readMs`), декодирует (`decodeMs`), вызывает hydrator (`hydrateMs`) и проверяет заранее названное объявление (`queryMs`). `usefulMs` — сумма этих четырёх. Capture в неё не входит и скрытой не считается: overhead пишется рядом.

Hydrator внутри `hydrateMs` ещё раз кодирует и декодирует snapshot. Это его принятый путь, не отдельная стадия read/decode.

Принимается только заранее названный контроль: query `SamplePlugin`, полное имя `RoslynMcpPlugin.SamplePlugin`, файл `samples/RoslynMcpPlugin/SamplePlugin.cs`. Другой query отклоняется до warmup и до restore. Restore-процесс получает то же ожидаемое объявление и не сообщает success для другого символа. Перед медианой `declaration` сверяется с этим ожиданием; расхождение — miss.

Каждый child получает новый run id. Перед запуском удаляются прежние `{role}-stages.json`, `{role}.pid` и `{role}.trx` этого каталога. Success требует exit code 0, TRX `total=1`, `passed=1`, `failed=0`, `notExecuted=0` и receipt с тем же run id, ролью и PID. Журнал пишется в `run.txt`.

Большой корпус передаётся `--large-workspace`. Его отказ, отсутствие на диске или неожиданный успех пишутся отдельно и в медиану контроля не входят. По умолчанию это кандидат BTCPay из прежнего backlog; отсутствие файла — `not-present`, не поддержка.

```powershell
py -3 scripts/workspace-state-cache/roundtrip_benchmark.py `
  --workspace <path-to.csproj> `
  --query <symbolName> `
  --server <path-to-RoslynMcpServer.exe> `
  --expect-full-name <Namespace.Type> `
  --expect-file <declaration-file> `
  --repo <repository-root> `
  --test-assembly <RoslynMcpServer.Tests.dll> `
  --out <raw-attempt-directory>
```

Warmup пары в медиану не входит. Дальше минимум пять успешных ordinary и пять успешных restore с тем же ключом. Неуспех любой ноги оставляет raw и помечает итог `partial`. p95 не считается.
