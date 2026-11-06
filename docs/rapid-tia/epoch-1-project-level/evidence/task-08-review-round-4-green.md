# Task-08 R3-001 — focused GREEN для раунда 4

- Дата: **2026-11-06**, Windows, Release; база `1f11b47`, поверх незакоммиченных исправлений R2-001.
- Исполнитель: Codex, тот же `/root/task08_implementer`; точный runtime model ID средой не предоставлен.
- Владелец явно разрешил исправление и дополнительный раунд: «Исправить и проверить ещё раз».
- Версия **1.6.1** сохранена: это доводка ещё не поставленной итерации, без нового version bump.
- [R3-001](task-08-review-round-3.md), [достоверный RED координатора — 3/3](task-08-review-round-4-red.md).

Специализированный Roslyn MCP `run_specific_test`, проект `E:/Devel/RoslynMcpServer-rapid-tia/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`, `configuration=Release`, `noRestore=true`, `includeFullOutput=true`:

1. `className=EstimateAffectedTestsNonSourceReloadTests`, `noBuild=false`, `timeoutSeconds=180`: **3 total / 3 passed / 0 failed**.
2. `className=EstimateAffectedTests`, `noBuild=true`, `timeoutSeconds=240`: **53 total / 53 passed / 0 failed**.
3. `className=VstestTestInventoryTests`, `noBuild=true`, `timeoutSeconds=120`: **15 total / 15 passed / 0 failed**.
4. `className=Baseline`, `noBuild=true`, `timeoutSeconds=300`: **349 total / 349 passed / 0 failed**.
5. `className=SourceStructure`, `noBuild=true`, `timeoutSeconds=240`: **123 total / 123 passed / 0 failed**.

Всего новых cases task-08: **68** (прежние 65 плюс три R3-001). Прежние assertions и inventories SourceStructure не менялись. Runner не сообщил failures, skips или timeouts; zero-test pass не заявлялся.

Два native/MSBuild cases начинают с `AdditionalFiles Include="Data/*.cs"`, `EnableDefaultCompileItems=false`, исходного Debug и Additional-only `Payload.cs`. Один сразу удаляет файл; второй до удаления делает настоящий Release reload, где тот же путь становится также compile source по условию неизменённого проекта. После native delete оба до reload выбирают App целиком. Следующий настоящий reload с неизменным `.csproj` удаляет globbed AdditionalDocument из текущей карты, но публичный план сохраняет App и причину с путём.

Третий case записывает неизвестный путь через настоящий `EditingTools.WriteFile`, проверяет journal и исходный `Not covered`, затем удаляет файл и выполняет настоящий configuration reload. Неизвестный путь остаётся в `Not covered`. Первоначальная попытка ждать native event вне watcher coverage была исправлена до production: тот timeout не считается RED-доказательством. Финальный RED всех трёх cases падает только в ожидаемых assertions публичного ответа.

Исправление сохраняет immutable proof ранее загруженных source paths из существующего `_rescanPaths` и известные original non-source paths из доступных solution metadata. Нового content cache, файлового чтения в provenance-сборе или общей history framework нет. Source/context/Unknown guards, mapped-role priority, Fresh rollback, разделение настоящего gap и ordinary reload остаются зелёными.

Это focused validation исполнителя. Новая финальная сборка, main CI, opted-in AnalyzerLifecycle, живое повторение R3-001 и независимый раунд 4 принадлежат координатору; принятие задачи этим документом не объявляется.

## Test-isolation correction после первого полного main

Первый main координатора на R3-001 исправлении: **1980 total / 1979 passed / 1 failed**. Новая NonSourceReload regression упала на неизменённой проверке `Assert.False(Baseline.HasObservationGap)` (строка 64); focused 3/3 и live probes до этого проходили. Этот main не считается зелёным, AnalyzerLifecycle после него ещё не запускался.

Подтверждён общий механизм конфликтующей fixture: `BaselineObservationGapTests` задаёт process-wide `WorkspaceDiskWatcherStarter.FailNextStartForTests`, который потребляет следующий watcher startup. Существующая `BaselineObservationGapCollection` с `DisableParallelization=true` прямо предназначена для изоляции этого флага. Два новых native-класса до correction в неё не входили. Исходный assertion не содержит отдельного trace, доказывающего источник конкретного неожиданного gap; никакой новый production-дефект этим не объявляется.

Минимальная коррекция — только `[Collection("BaselineObservationGap")]` у `EstimateAffectedTestsReloadTests` и `EstimateAffectedTestsNonSourceReloadTests`. Native watcher, реальные reload, NoGap и остальные assertions сохранены полностью; production и 1.6.1 не менялись. После этих двух metadata-строк C# снова заморожен. Повторные affected tests, Release build, main и opted-in lifecycle выполняет координатор; прежний focused GREEN не подменяет их.
