# Review: implementation vs mcp-build-progress-notifications

Каталог: замечания к **поставленной реализации** S1–S3 (v1.3.25) относительно
канона, не к тону документов. S4 не ревьюился как shipped.

Сверка: `Services/CliProgress.cs`, `Services/DotNetCliRunner.cs`,
`Services/DotNetBuildProbe.cs`, `Tools/McpToolProgressReporter.cs`,
`Tools/BuildTools.cs`, `Hosting/McpToolHelpFormatter.cs`,
`RoslynMcpServer.Tests/CliProgressTests.cs`,
`RoslynMcpServer.Tests/BuildProgressIntegrationTests.cs`,
`RoslynMcpServer.Tests/McpToolCatalogTests.cs`; SDK
`ModelContextProtocol.NullProgress` / `TokenProgress` (пакет 1.3.0);
runtime `get_mcp_server_info` = **RoslynMcpServer v1.3.25.0**, 63 tools,
`full`. Не v1.3.19 из шапки плана.

Цель ревью — опровергнуть заявление «S1–S3 выполнено» там, где код, oracle
или runtime расходятся с MUST результата.

| ID | Sev | Spec |
| --- | --- | --- |
| R-01 | High | README.md |
| G-01 | Medium | README.md + S2 adapter |
| S1-01 | Medium | s1-cli-runner-progress-seam.md |
| S1-02 | Medium | s1-cli-runner-progress-seam.md |
| S2-01 | High | s2-build-probe-progress.md |
| S2-02 | High | s2-build-probe-progress.md |
| S2-03 | Medium | s2-build-probe-progress.md |

s3-docs-and-claims.md: карточек нет (EN/RU не обещают лечение `-32001`;
catalog 63 / 45 868 и 19 / 18 282 совпадает с тестом
`Surface_sizes_match_recorded_release_numbers`; `AGENTS.md.sample` без
progress). s4-test-and-run.md: статус «не выполнено» подтверждён
(`TestTools` вызывает `RunWithMetadataAsync` без `CliProgressWatch`).

---

ID: R-01
Severity: High
Category: Measurement

Target:
README.md / «Результат каталога» / абзац «Что репортится»;
s1-cli-runner-progress-seam.md / `CliProgressUpdate.Elapsed`;
s2-build-probe-progress.md / `ReportStepStarted`

Claim:
Текст progress несёт elapsed **текущего шага** (S1: «Wall-clock time since
the step started»). На границе шага и в heartbeat это одни и те же часы.

Evidence:
`CliProgressUpdate` документирует elapsed как время с начала шага
(`Services/CliProgress.cs`). Heartbeat в `DotNetCliRunner.StartHeartbeat`
заводит **новый** `Stopwatch` на каждый `RunWithMetadataAsync`.
`DotNetBuildProbe.RunStepAsync` на границе шага вызывает
`ReportStepStarted(..., sw.Elapsed, ...)`, где `sw` — stopwatch **всего
probe**, заведённый в `RunAsync`. Второй и следующие шаги (restore /
`-v:normal` / `-v:detailed`) стартуют с сообщением «still running (Ns
elapsed)», где N — длительность предыдущих шагов, затем через 5 с
heartbeat пишет уже секунды **этого** процесса.

Failure scenario:
1. Большой `.sln`: `dotnet build -v:minimal` молчит ~3 мин и эскалирует.
2. Граница restore сразу шлёт `dotnet restore -v:minimal: still running (180s elapsed); previous step exit 1`.
3. Через 5 с то же поле: `(5s elapsed)`. Агент читает, что restore уже шёл
   3 мин, затем время отъехало назад. Приёмка S2 («агенту полезно видеть
   текущий шаг») зелёная, потому что строка содержит `dotnet restore`.

Suggested change:
На границе шага передавать elapsed шага (`TimeSpan.Zero` или тот же
stopwatch, что у heartbeat). Если нужен wall-clock всего probe — отдельное
поле, не `CliProgressUpdate.Elapsed`. Пока часы смешаны, гарантия
«elapsed = текущий шаг» ложна.

Confidence:
High

---

ID: G-01
Severity: Medium
Category: Observability

Target:
README.md / «Результат каталога» / транспорт `IProgress<ProgressNotificationValue>`;
`Tools/McpToolProgressReporter.Report`

Claim:
Клиенту уходит heartbeat (шаг, elapsed, previous exit). Числовой
`Progress` — служебная монотонность SDK, не процент готовности сборки.

Evidence:
`McpToolProgressReporter.Report` ставит `Progress = Interlocked.Increment`
с 1 и не задаёт `Total`. XML SDK (`ProgressNotificationValue.Progress`):
типично 0–100 как процент либо счётчик вместе с `Total`. Хост, который
рисует бар только по `Progress`, увидит 1, 2, 3… на одном 10-минутном
шаге. Сообщение при этом честное; число — нет.

Failure scenario:
1. Cursor/другой хост показывает progress bar из `notifications/progress`.
2. После 12 heartbeat'ов бар = «12%», хотя probe на первом шаге.
3. Агент или пользователь считает сборку почти на старте и дублирует
   `tools/call` либо наоборот ждёт «100». Текст message это не чинит, если
   UI его режет.

Suggested change:
Либо `Progress` = целые секунды elapsed (монотонно, без намёка на 0–100)
при `Total = null`, либо явно в результате S2: числовой `Progress` не
семантика готовности, хостам смотреть только `Message`. Не оставлять
инкремент 1,2,3 как единственный numeric.

Confidence:
High
