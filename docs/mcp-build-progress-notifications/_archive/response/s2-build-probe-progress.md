# Карточки ответа: `s2-build-probe-progress.md`

---

ID: S2-01
Verdict: ACCEPT

Критика верна: приёмка «на многошаговом probe видны отдельные шаги» была
tautology — интеграционный тест собирал успешный проект, где
`ShouldRunMoreDiagnostics` не эскалирует, и проверял подстроку `dotnet build`.
Поломка репорта 2-го и последующих шагов осталась бы зелёной.

Требования:
S2 / «Приёмка»: «На многошаговом probe в логе/тестовом sink видны отдельные
шаги»; S2 / «Работа»: «Репорт на смене шага».

Что менять:
Новый оракул `RoslynMcpServer.Tests/DotNetBuildProbeTests.cs` /
`RunAsync_reports_every_escalated_step_label_to_the_progress_reporter`:
временный проект с `Error` в таск-хуке `BeforeTargets="Build"` (код-лесс
`error :`, парсер не видит диагностики) заставляет probe эскалировать; fake
`ICliProgressReporter` ассертит **набор** label'ов (`-v:minimal`,
`restore -v:minimal`, `-v:normal`) и что каждая граница идёт без elapsed, с
`previous step exit` предыдущего шага.

Последствия:
Тесты: +1 процессный тест, ~10–20 с (4 вызова `dotnet`), бюджет probe в тесте
ограничен (240 с / шаг 90 с). Прочая приёмка S2 не менялась.

Шире finding:
нет.

---

ID: S2-02
Verdict: ACCEPT

Критика верна: `File not found` — это ветка `BuildTools` до `DotNetBuildProbe`,
поэтому тест «без token» не исполнял ни watch, ни heartbeat и не покрывал
MUST «успешный build, как до S2».

Требования:
S2 / «Приёмка»: «Хост без progress token: успешный build, как до S2».

Что менять:
`BuildProgressIntegrationTests.Run_dotnet_build_live_build_matches_the_with_progress_result_when_no_token_is_sent`
теперь гоняет **живой** `dotnet build` без аргумента `IProgress` и ассертит
обычный отчёт (`Build succeeded`, `Steps:`, `dotnet build -v:minimal`).
File-not-found из этой приёмки убран. Плюс прямой тест
`TryCreate_treats_the_sdk_no_op_progress_instance_as_no_progress` фиксирует,
что token-less вызов вообще не заводит watch (S1-01).

Последствия:
Тесты: +1 живой build (3–5 с). Отрицательный путь missing-file остаётся
покрыт валидацией `BuildTools` вне этой приёмки.

Шире finding:
нет.

---

ID: S2-03
Verdict: ACCEPT

Критика верна: `SlowProject` всегда использовал `ping -n 3 127.0.0.1`, что на
Linux не count, а числовой вывод; на остальных RID тест упирался в CTS 180 с.

Требования:
Shipped-инвариант репозитория: процессные тесты разводятся по OS
(`RoslynMcpServer.Tests/DotNetCliRunnerHangTests.cs`,
`CliProgressTests.WriteHangProject`); README: self-contained publish для
win/osx/linux RID.

Что менять:
`BuildProgressIntegrationTests.SlowProject()` выбирает
`ping -n 3 127.0.0.1` (Windows) / `sleep 2` (Unix) — как в hang-тестах runner.

Последствия:
Тесты: переносимость win/osx/linux; на Unix шаг короче, окно heartbeat 5 с
всё равно покрывается границей шага.

Шире finding:
нет.
