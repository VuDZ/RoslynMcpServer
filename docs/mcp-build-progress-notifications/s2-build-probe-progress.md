# S2 — progress на `run_dotnet_build`

Статус: **выполнено (v1.3.25)**. Зависимость: [S1](s1-cli-runner-progress-seam.md).
Результат шага: живой build probe репортит границы шагов и периодический
heartbeat, пока идёт `dotnet build`.

## Основание

Первая поставка — только build. `DotNetBuildProbe` уже эскалирует
`build -v:minimal` → restore → `build -v:normal`. Агенту полезно видеть
текущий шаг, а не молчание на 5–15 мин большого `.sln`.

Targets: `DotNetBuildProbe`, `BuildTools` / `run_dotnet_build`.

## Работа

Передать reporter из tool method в probe. Репорт на смене шага и, если
шаг долгий, heartbeat из runner. Итог тула (diagnostics, SDK mismatch,
effective exit) не менять.

Не обещать в `[Description]`, что хост не оборвёт вызов. Catalog не
раздувать: Description трогать только если без этого агент начнёт
путать progress с результатом тула.

## Приёмка

- `run_dotnet_build` этого репо по-прежнему парсит ошибки и
  `MCP_MSBUILD_SDK_MISMATCH` как сейчас.
- На многошаговом probe в логе/тестовом sink видны отдельные шаги.
- Хост без progress token: успешный build, как до S2.
- Не требовать зелёный Cursor ACP на 60 с — это вне шага.

## Результат

Выполнено. `DotNetBuildProbe.RunAsync` получил `ICliProgressReporter? progress = null`;
`RunStepAsync` репортит границу шага (`ReportStepStarted`, сразу, с exit
предыдущего шага и без elapsed) и передаёт `CliProgressWatch` в runner для
heartbeat'ов текущего шага.
Итог тула (diagnostics, `MCP_MSBUILD_SDK_MISMATCH`, effective exit, steps)
не менялся. `BuildTools.RunDotNetBuild` получил инжектируемый
`IProgress<ProgressNotificationValue>? progress = null`, обёрнутый в
`Tools/McpToolProgressReporter`. `[Description]` и публичная схема не менялись
(`McpToolHelpFormatter.IsMcpBoundParameter` исключает SDK-bound параметры из
справки и из schema-теста), catalog size тот же: full 63 / 45 868, lite 19 / 18 282.

Тесты:

- `DotNetBuildProbeTests.RunAsync_reports_every_escalated_step_label_to_the_progress_reporter` —
  приёмка «видны отдельные шаги»: временный проект падает без парсимого
  `error CODE` (`Error` в `BeforeTargets="Build"`), probe эскалирует, и fake
  reporter видит разные label'ы (`-v:minimal`, `restore -v:minimal`,
  `-v:normal`), границы без elapsed с exit предыдущего шага.
- `BuildProgressIntegrationTests` — in-process MCP server+client на **живом**
  `dotnet build`: с progress token приходят уведомления с текстом
  `dotnet build …`, без пути временной папки, и результат — `Build succeeded`;
  без progress token тот же живой build даёт обычный отчёт (`Build succeeded`,
  `Steps:`, `dotnet build -v:minimal`). `SlowProject` разведён по OS.
