# S1 — шов progress в `DotNetCliRunner`

Статус: **выполнено (v1.3.25)**.
Результат шага: долгий CLI-процесс может отправить MCP progress, не меняя
публичные параметры тулов.

## Основание

`DotNetCliRunner.RunWithMetadataAsync` — единственное место, где живёт
таймаут процесса и чтение stdout/stderr. Build probe и test/run уже идут
через него. Progress на уровне тула без шва в runner размножит таймеры.

Targets: `DotNetCliRunner`, вызов из MCP tool context (progress token /
`IProgress` SDK, как даст текущий `ModelContextProtocol` пакет).

## Работа

Добавить необязательный reporter в runner: если token/callback есть —
периодически слать короткое обновление, пока процесс не завершился.
Если token нет — поведение байт-в-байт как сейчас.

Не читать analyzer files. Не менять kill-on-timeout / cancel. Не писать
progress в файл лога как замену протоколу.

Имена внутренних типов — выбор реализации. Новых MCP параметров нет.

## Приёмка

- Без token существующие тесты runner/probe не требуют правок контракта.
- С тестовым callback за время искусственно длинного процесса приходит
  ≥1 уведомление с elapsed и без полного stdout.
- Cancel/timeout по-прежнему убивают process tree.

## Результат

Выполнено. `Services/CliProgress.cs`: `CliProgressUpdate` (stage, elapsed, last
exit + `Describe()`), `ICliProgressReporter` (никогда не бросает),
`CliProgressWatch` (`HeartbeatInterval`, default 5 с; `LastExitCode`).
`DotNetCliRunner.RunWithMetadataAsync` получил последний необязательный
параметр `CliProgressWatch? progress = null`; без watch вызов идёт в прежний
`RunCoreAsync` без таймера (поведение байт-в-байт), с watch — linked CTS +
heartbeat-task, который снимается в `finally` (`CancelAsync` + await).
Kill-on-timeout и `ReadRemainingStreamsAsync` не менялись.

`Elapsed` — время **текущего шага** (на границе шага — `TimeSpan.Zero`,
`Describe()` печатает `starting`), а не время всего probe; heartbeat-часы
заводит runner на каждый процесс.

No-token путь достижим на живом туле: SDK всегда биндит
`IProgress<ProgressNotificationValue>` (либо `TokenProgress`, либо внутренний
singleton `NullProgress`). `Tools/McpToolProgressReporter.TryCreate` возвращает
`null` для `NullProgress` (по полному имени типа, с fallback «просто лишний
таймер», если SDK переименует тип), поэтому «без watch» — не только тестовый
путь. `Report` сам глотает исключения, а не полагается на `catch` в probe.

Тесты: `CliProgressTests` (Describe без stdout; heartbeat ≥1 при живом
процессе и интервале 250 мс; без watch — таймаут и kill по-прежнему);
`BuildProgressIntegrationTests.TryCreate_treats_the_sdk_no_op_progress_instance_as_no_progress`
(рефлексия по реальному SDK-типу) и `Report_swallows_a_failing_progress_channel`.
