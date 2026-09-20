# S4 — тот же шов для test/run (опционально)

Статус: **выполнено (v1.3.27)**. Зависимость: [S3](s3-docs-and-claims.md).
Результат шага: `run_dotnet_test` / `run_specific_test` / `run_test_by_filter` /
`run_dotnet_run` репортят progress тем же `ICliProgressReporter` /
`CliProgressWatch`, без нового канала и без смены публичной схемы тулов.

## Основание

S5-сессия показала, что class-level lifecycle test занимает минуты, не
десятки. Боль на больших app-репах — `dotnet test` 10+ мин и
`run_dotnet_run`. Делать только после shipped S1–S3, иначе раздувается
первая поставка.

Не смешивать с VSTest parser и pre-test build budget: progress не
удлиняет `timeoutSeconds` и не отменяет split build-then-test.

`run_test_by_filter` — тот же `TestTools` private runner, что
`run_dotnet_test` / `run_specific_test`. В первой формулировке S4 его не
было; это дыра, не отдельный продукт. Default `noBuild=true` не исключает
тул: длинный VSTest — как раз его случай.

## Что это не делает

- Не гоняет pre-test compile через `DotNetBuildProbe` (pitfall 15:
  probe эскалирует verbosity; pre-test — один incremental `dotnet build`,
  затем `dotnet test --no-build --no-restore`).
- Не шлёт каждую строку VSTest / stdout `dotnet run`.
- Не меняет `timeoutSeconds` / `RemainingTimeout` (общий бюджет на
  pre-test + test; `0` по-прежнему без лимита).
- Не трогает parser, `Status: partial`, filter matching, StdOut/StdErr
  excerpts.
- Не подключает `execute_dotnet_command` и прочие CLI-тулы вне четырёх
  имён выше.
- Не лечит хост-таймаут (`-32001`, Cursor ACP ~60 с) — то же, что S3.
- Не закрывает NEW-D-01 / NEW-D-02: S4 только переиспользует адаптер.

## Шов

`RunWithMetadataAsync` уже принимает `CliProgressWatch?`. Test-тулы
вызывают его **без** watch — это единственная дыра на test-пути.

`run_dotnet_run` идёт через `RunSeparatedAsync` → `RunSeparatedCoreAsync`.
Watch там нет. S4 **добавляет** тот же optional `CliProgressWatch?`
последним параметром `RunSeparatedAsync` и обёртку heartbeat как у
`RunWithMetadataAsync` (без watch — байт-в-байт в `RunSeparatedCoreAsync`).
Новый progress-канал не вводить. Контракт `RunWithMetadataAsync` не
менять.

Адаптер — существующий `McpToolProgressReporter.TryCreate` (no-op SDK →
`null`, таймера нет; `Report` глотает транспорт).

## Работа

На каждый живой CLI-шаг: граница сразу (`Elapsed = TimeSpan.Zero` →
`starting`, `LastExitCode` предыдущего шага этого же вызова тула) +
heartbeat из runner, пока процесс жив.

Интервал — `CliProgressWatch.DefaultHeartbeatInterval` (**5 с**), тот же,
что у build. «Реже, чем probe» в старой формулировке значило **меньше
границ** (1–2 шага, не эскалация `-v:minimal` → restore → `-v:normal`),
а не второй cadence и не «молчать дольше». Не слать каждую строку VSTest.

Стабильные метки **без путей, фильтров, `--` args, `-c` / `-t`**:

| Шаг тула | Когда | Метка |
| --- | --- | --- |
| Pre-test compile | `noBuild=false`: `plan.PreTestBuildArguments` или sln `-t` при `binariesPath` | `dotnet build` |
| Test process | всегда, если дошли до VSTest | `dotnet test` |
| Run | `run_dotnet_run` | `dotnet run` |

`noBuild=true` (в т.ч. default у `run_test_by_filter`): только
`dotnet test`. После успешного pre-test граница `dotnet test` несёт
exit сборки. Если pre-test timed out / budget exhausted — test-шага нет,
как сейчас.

Публичная схема: `IProgress<ProgressNotificationValue>?` на четырёх
методах тулов, `ExcludeFromSchema`, как у `run_dotnet_build`. Новых
агентских параметров нет. `[Description]` не раздувать.

## Приёмка

- Существующие VSTest-тесты (parser, `Status: partial`, filter, StdOut
  excerpts) зелёные без правок контракта.
- `DotNetTestArguments.RemainingTimeout` и split build-then-test без
  изменений: общий `timeoutSeconds`, при исчерпании budget test не
  стартует.
- Хост без token: `TryCreate` → `null` → нет watch → тот же отчёт тула.
- `RunSeparatedAsync` без watch: текущие тесты `run_dotnet_run` / runner
  не требуют правок контракта; с тестовым callback на искусственно
  длинном процессе — ≥1 heartbeat с меткой `dotnet run`, без stdout.
- Fake reporter на test-оркестрации: `noBuild=false` видит метки
  `dotnet build` затем `dotnet test` (первая граница без elapsed и без
  previous exit, вторая — `starting` + exit pre-test); `noBuild=true` —
  только `dotnet test`. В `Describe()` нет путей temp-проекта и нет
  строк VSTest.
- Не требовать живой 10-минутный test и не требовать зелёный Cursor ACP
  на 60 с.
- Ряд README «Agent tools by version» + одна фраза в ARCHITECTURE: UX
  heartbeat на этих тулах, **не** лечение п.1. `AGENTS.md.sample` не
  трогать. Catalog size — без изменения, если Description не меняли.

## Результат

Выполнено (**v1.3.27**). `CliProgressStep` (`Services/CliProgress.cs`) —
метки `dotnet build` / `dotnet test` / `dotnet run`, граница
`ReportStarted` (`Elapsed = Zero`) и `Watch` в runner. `TestTools` три
публичных метода + `ExecuteDotnetTestAsync` принимают schema-excluded
`IProgress<ProgressNotificationValue>?`; pre-test идёт через
`CliProgressStep.RunWithMetadataAsync` (не probe), test-шаг несёт exit
сборки. `RunSeparatedAsync` получил optional `CliProgressWatch?`;
`run_dotnet_run` репортит `dotnet run`. `execute_dotnet_command` без
watch. Публичная схема и catalog size без изменения.

Тесты (`CliProgressTests`): `RunSeparatedAsync` без watch по-прежнему
timeout+kill; с watch — heartbeat метки `dotnet run` без stdout; два
шага `--info` дают `dotnet build` затем `dotnet test` (`starting` +
previous exit); без pre-test — только `dotnet test`.

Корневой README ряд **v1.3.27**; `docs/ARCHITECTURE.md` — одна фраза.
`AGENTS.md.sample` не менялся.
