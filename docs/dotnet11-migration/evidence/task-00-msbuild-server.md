# Этап 0. Windows-наблюдение MSBuild Server для G-01

Дата — 2026-11-18. Windows x64, SDK `11.0.100` в отдельной копии
`artifacts/dotnet11-task00/msbuild-server/sdk-home`, MSBuild file version `18.12.2.53018`.
Ближайший `global.json` — `artifacts/dotnet11-task00/msbuild-server/fixture/global.json`,
рабочий каталог — `fixture` в том же артефакте. Pin точный, `rollForward=disable`,
`allowPrerelease=false`. Тестовый проект — `net11.0` с одной xUnit-проверкой.

Сценарии вызвали `DotNetCliRunner.RunWithMetadataAsync` из Release DLL baseline 1.5.25.
DLL SHA-256 — `611256862da54dc46d008006637741e3003448766fb30c2df57b115237c4f51a`.
Сборка загружена в отдельный PowerShell-процесс, метод вызван через reflection. В семи
сценариях runner ставит своему дочернему процессу `MSBUILDDISABLENODEREUSE=1`.
В шести сценариях обе server-переменные окружения отсутствовали. В седьмом наблюдалось
унаследованное `MSBUILDUSESERVER=1` при том же `MSBUILDDISABLENODEREUSE=1`.
Проба опрашивает CIM по точному пути SDK каждые 80 мс плюс время запроса,
а также снимает состояние сразу после возврата runner, через 2,2 с и после выхода
PowerShell-процесса. Переменные менялись только в окружении тестового процесса и его потомков.
[Сырые наблюдения](task-00-msbuild-server-raw.json) ·
[DotNetCliRunner](../../../Services/Build/DotNetCliRunner.cs)

## Результаты текущего runner

- Обычная сборка и повторная сборка завершились с кодом 0.
- `dotnet test` завершился с кодом 0 и выполнил 1 тест, 0 failed, 0 skipped.
- Намеренно неуспешная сборка вернула код 1 и `PROBE1000`.
- В сценарии отмены запрос отправлен после того, как целевая MSBuild-задача записала
  маркер начала ожидания. Runner вернул `OperationCanceledException`.
- Сценарий timeout дошёл до той же задачи. Через 10 с runner зафиксировал
  `TimedOut=true`, `ProcessKilled=true`, exit code -1.
- Сборка с унаследованным `MSBUILDUSESERVER=1` завершилась с кодом 0.

Ни одна выборка семи сценариев не зафиксировала собственного процесса `/nodemode:8`.
Снимок после выхода вызывающего процесса также пуст. На этих Windows-сценариях
существующего флага достаточно, чтобы MSBuild Server не оставался после команды.
Новый opt-out и `/nr:false` не требовались для наблюдавшихся сценариев.

## Положительный контроль и идентификатор процесса

Из той же директории прямой `dotnet build` с `MSBUILDUSESERVER=1`, но без
`MSBUILDDISABLENODEREUSE`, создал сервер. После первого и повторного вызова виден один
и тот же PID `63840`, parent PID `69416`, начало `2026-11-18T20:21:04.498729Z`.
Командная строка — `"E:\Devel\dotnet11-migration-plan\artifacts\dotnet11-task00\msbuild-server\sdk-home\sdk\11.0.100\MSBuild.exe" /nologo /nodemode:8`.
Отдельный диагностический повтор зарегистрировал такое же переиспользование сервера
PID `45516` в выводе MSBuild. Проба завершает только процессы `/nodemode:8` с
путём своей отдельной копии SDK после регистрации наблюдения.

На SDK 11 в этой Windows-среде сервер запускается через нативный `MSBuild.exe`.
Первый sampler, который искал только `dotnet … MSBuild.dll`, пропустил
положительный контроль. Приведённые результаты runner сняты повторно с учётом
обеих форм. Worker `/nodemode:1`, compiler server и Roslyn Workspace BuildHost
из этой выборки исключены. Глобальный `dotnet build-server shutdown` не вызывался.
[Заметки SDK 11](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/sdk#msbuild-server-enabled-by-default) ·
[Описание MSBuild Server](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-server)

Результат G-01 подтверждён для этой Windows-конфигурации. Linux, macOS, отдельный
MCP transport и другие формы вызова не проверялись этим опытом. Повтор возможен
командой `pwsh -NoProfile -File artifacts/dotnet11-task00/msbuild-server/Observe-MSBuildServer.ps1 -ProductAssembly <Release DLL>`
из checkout с указанной копией SDK и fixture. Нужен CIM-доступ к командным строкам
собственных процессов.
