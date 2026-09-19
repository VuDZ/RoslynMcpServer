ID: S2-01
Severity: High
Category: Oracle

Target:
s2-build-probe-progress.md / «Приёмка» / «На многошаговом probe в
логе/тестовом sink видны отдельные шаги»; «Результат» / in-process тест

Claim:
Многошаговый probe показывает отдельные стадии (`restore` / `build
-v:normal` / …) в тестовом sink. Первая поставка этим закрыта.

Evidence:
`BuildProgressIntegrationTests.Run_dotnet_build_reports_protocol_progress_for_a_live_build`
собирает успешный `Slow.csproj`: `ping -n 3` + обычный `dotnet build`.
`ShouldRunMoreDiagnostics` при 0 ошибок и без `FAILED` **не** эскалирует.
В sink проверяется только `Contains("dotnet build")` и отсутствие пути
temp. Цепочка из README (`minimal → pinned restore → restore -v:minimal →
restore -v:detailed → build -v:normal → build -v:detailed`) в тесте не
появляется. `DotNetBuildProbeTests` не передаёт `ICliProgressReporter`.

Failure scenario:
1. Исполнитель ломает `ReportStepStarted` для 2-го+ шага (забыл
   `completedStepExit`, перетёр `label`).
2. Первый successful `dotnet build -v:minimal` по-прежнему даёт ≥1
   notification с текстом `dotnet build`.
3. S2 помечен shipped. На реальном эскалационном probe агент не видит
   смену шага — ровно тот UX, ради которого шаг S2 существует.

Suggested change:
Отдельный тест: fake `ICliProgressReporter` + принудительно многошаговый
probe (первый шаг с ненулевым exit / `-- FAILED` без parsed error) и
assert набора label'ов, не подстроки `dotnet build`. Пока его нет, приёмка
«отдельные шаги» — tautology на одном successful build.

Confidence:
High

---

ID: S2-02
Severity: High
Category: Oracle

Target:
s2-build-probe-progress.md / «Приёмка» / «Хост без progress token:
успешный build, как до S2»; «Результат» / «клиент без progress token
получает обычный результат (File not found)»

Claim:
Вызов без progress token даёт тот же **build**, что до S2. Результат шага
зафиксировал File-not-found как достаточный oracle.

Evidence:
`Run_dotnet_build_works_when_the_client_sends_no_progress_token` передаёт
`definitely-missing.csproj` и ждёт `File not found`. Это ветка
`BuildTools` до `DotNetBuildProbe.RunAsync` / `TryCreate` / heartbeat.
Живой `dotnet build` без token (NullProgress + watch + таймер) не
выполняется. S1 «без token байт-в-байт» этим тестом не проверяется.

Failure scenario:
1. `TryCreate`+watch начинает бросать/висеть только после `process.Start`
   (Send на stdio, `StopHeartbeatAsync`).
2. Тест без token остаётся зелёным: файла нет, probe не звался.
3. S2 «успешный build без token» принят. Реальный хост без token на
   живой сборке получает регресс, которого oracle не видит.

Suggested change:
Убрать File-not-found из приёмки no-token. Нужен живой build без
`IProgress` на `CallToolAsync` (или явный NullProgress) с тем же
результатом, что и с token, и без уведомлений. Иначе MUST «как до S2»
не имеет инструмента.

Confidence:
High

---

ID: S2-03
Severity: Medium
Category: Oracle

Target:
s2-build-probe-progress.md / «Результат» / Slow live `dotnet build`;
`BuildProgressIntegrationTests.SlowProject`

Claim:
In-process MCP client видит ≥1 notification на живом `dotnet build`.
Тест переносим как остальные hang-тесты runner.

Evidence:
`CliProgressTests.WriteHangProject` ветвит Windows `ping -n 60` /
Unix `sleep 60`. `SlowProject` в интеграционном тесте **всегда**
`<Exec Command="ping -n 3 127.0.0.1" />`. На Linux `ping -n` — numeric
output, не count; процесс не ограничен тремя ICMP и упирается в CTS
180 с. Зелёный S2 на Windows не воспроизводит заявленный «живой build»
на остальных RID publish.

Failure scenario:
1. CI/агент гоняет `dotnet test` на linux-x64.
2. `Run_dotnet_build_reports_protocol_progress_for_a_live_build` висит до
   180 с или падает по cancel.
3. Либо job красный при рабочей фиче, либо Linux-поставку принимают по
   Windows-only oracle.

Suggested change:
Тот же OS-split, что в `CliProgressTests` / `DotNetCliRunnerHangTests`.
Пока `SlowProject` Windows-only, приёмка «живой build» не покрывает
заявленные RID.

Confidence:
High
