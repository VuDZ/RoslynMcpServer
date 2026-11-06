# task-08 — RED раунда 4, R3-001

- Дата: **2026-11-06**, база `1f11b47`, незакоммиченная версия **1.6.1** после R2-001.
- Владелец явно разрешил исправить [R3-001](task-08-review-round-3.md) и провести четвёртое ревью ответом «Исправить и проверить ещё раз». Это точечное исключение из трёхраундового лимита R-18.
- Исполнитель — прежний `/root/task08_implementer`, Codex (runtime model ID средой не предоставлен); production до этого RED не менялся.
- Тесты написаны первыми: `EstimateAffectedTestsNonSourceReloadTests`, **3 cases**. Две реализации первого сценария начинают с исходного `AdditionalFiles Include="Data/*.cs"`, `EnableDefaultCompileItems=false`, `Payload.cs` существует до baseline как AdditionalDocument и не является C# Document. В варианте true тот же неизменный project включает `Compile Data/*.cs` только в Release: промежуточный reload делает Payload source, затем удаление и новый reload обязаны сохранять исходную non-source дельту. Project XML после baseline не меняется. Удаление доставлено настоящим native watcher.
- Третий сценарий использует настоящий `EditingTools.WriteFile`, чтобы зарегистрировать неизвестный путь вне проектных watcher roots. До удаления конкретный путь виден в Not covered; после удаления и настоящего configuration reload он должен остаться видимым.
- Каждый reload проверяет `LastLoadReopenedGraph=true`, `LastLoadWasCacheHit=false`, рост generation и отсутствие настоящего observation gap. После удаления AdditionalFile текущие AdditionalDocuments/map действительно больше не содержат Payload, journal всё ещё содержит путь.
- Первая попытка третьего теста ошибочно ждала native event вне watcher coverage и упала на fixture precondition. Она **не считается RED**; setup заменён настоящей MCP-записью, без synthetic callbacks и без production changes.
- Координатор затем выполнил Roslyn MCP `execute_dotnet_command`: Release `dotnet test` с build, `--no-restore --filter "FullyQualifiedName~EstimateAffectedTestsNonSourceReloadTests" --logger "trx;LogFileName=red.trx" --results-directory TestResults/task-08-round-4-red`. Специализированный test tool не выражает TRX logger, поэтому используется raw MCP runner.
- Полный локальный `TestResults/task-08-round-4-red/red.trx` проверен: **total=3, executed=3, failed=3, passed=0, error=0, timeout=0, aborted=0, notExecuted=0**. Все три failures — в конечных проверках ответа, после успешных compile/native/MSBuild preconditions. Неожиданных fixture failures нет.
- SHA-256 первоначального тестового файла после исправления fixture, до production fix: `CD1E9155F86A734D1E099CC0DDFBC82A335FC6088447CABA7034C4732E72D407`.

## Все исходные outcomes

### RoslynMcpServer.Tests.Testing.EstimateAffectedTestsNonSourceReloadTests.Deleted_original_globbed_AdditionalFile_with_cs_extension_remains_selected_after_real_reload(compileOnIntermediateReload: False)

```text
Approximate result (rapid TIA): not a substitute for the full test suite or CI.
Changed since load: 0 members in 0 files. Directly selected: 0 tests. Run plan: 0 projects.
Not covered:
- Reload/reopen observation window: non-C# changes may have been missed. C# sources are rescanned; this non-C# limitation persists for this baseline.
State: project-level result only.
```

### RoslynMcpServer.Tests.Testing.EstimateAffectedTestsNonSourceReloadTests.Mcp_recorded_unknown_journal_path_stays_not_covered_after_real_reload

```text
Approximate result (rapid TIA): not a substitute for the full test suite or CI.
Changed since load: 0 members in 0 files. Directly selected: 0 tests. Run plan: 0 projects.
Not covered:
- Reload/reopen observation window: non-C# changes may have been missed. C# sources are rescanned; this non-C# limitation persists for this baseline.
State: project-level result only.
```

### RoslynMcpServer.Tests.Testing.EstimateAffectedTestsNonSourceReloadTests.Deleted_original_globbed_AdditionalFile_with_cs_extension_remains_selected_after_real_reload(compileOnIntermediateReload: True)

```text
Approximate result (rapid TIA): not a substitute for the full test suite or CI.
Changed since load: 0 members in 0 files. Directly selected: 0 tests. Run plan: 0 projects.
Not covered:
- Reload/reopen observation window: non-C# changes may have been missed. C# sources are rescanned; this non-C# limitation persists for this baseline.
State: project-level result only.
```


У обоих AdditionalFile cases исчез выбранный App.csproj; у неизвестного пути исчезла конкретная запись Not covered. Общая оговорка о non-C# reload window присутствует, но не заменяет сохранение уже наблюдённой дельты. После проверки RED координатор разрешил исправление прежнему исполнителю; это не acceptance.
