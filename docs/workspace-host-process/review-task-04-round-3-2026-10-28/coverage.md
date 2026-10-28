# Охват ревью task-04, раунд 3

Рецензент `claude-opus-5-5` (Claude Code), 2026-10-28, коммит `33b1d8d`.

## Прочитано

- `AGENTS.md`, task-04, README эпохи и темы, строка `docs/README.md`, диффы учёта `07224d1..33b1d8d`.
- Отчёт task-04 целиком; `summary.json`, `validation-final.json`, `cleanup-final.json`,
  `linux-access-0777.json`, `codex-mcp-kill.json`; выборочный разбор `linux.json` и `windows.json`
  (события `connected`, `connection-error`, `cancel-received` по сценариям и поколениям).
- Весь новый и изменённый код `spikes/PipeRelay`: `Routing/*`, `Checks/RoutingChecks.cs`,
  `Checks/RoutingFixture.cs`, `Checks/ProcessIdentity.cs`, диффы `Facade.cs`, `Program.cs`,
  `BuildIdentity.cs`, `CounterState.cs`, `Transport/BoundedStream.cs`, `Transport/LocalHandshake.cs`.
- Ревью Codex раундов 1–2 — после формирования своих замечаний; пересечений с F-01–F-08 нет.

## Запущено

| Что | Где | Результат |
|---|---|---|
| `dotnet build spikes/PipeRelay/PipeRelay.csproj -c Release` | Linux native | 0 warnings, 0 errors |
| `routing-checks all` (`TMPDIR=~/tmp/testtmp`) | Ubuntu 26.04.1, .NET 10.0.13, identity `5F6F99DF…` | 32 записи, exit 0; [сводка](linux-native-run.json) |
| `cat /proc/locks` во время серии | то же | `POSIX ADVISORY WRITE` + `FLOCK READ` на lock-файлах (F-04) |
| Проба ревью `routing-checks probe` на копии spike в `~/tmp` с [patch](review-probe.patch) | то же | [вывод](review-probe-output.txt): F-01, F-03 |
| Удаление `/tmp/r4-*` после прогона | то же | 32 каталога, 144 MiB удалены (F-07) |

Проба не меняет механизм: добавлен только режим `probe` в `RoutingChecks.cs`. Первый
сценарий — вызов `slow 1` на A, затем `switch` на B с `LoadBarrier`; второй — kill host
через ~300 мс после cold start и три вызова подряд без паузы.

## Не выполнялось и почему

- Windows и macOS — нет среды у рецензента; результаты Windows приняты по raw автора.
- Release build решения и main suite — production и solution не менялись, spike вне solution;
  принят `validation-final.json` автора (1432/1432).
- `check_unix_access.py` и `codex_lifecycle.py` не перезапускались; приняты по raw.
- Место потери автоматической отмены SDK не трассировалось (F-06 оставляет это task-01).
