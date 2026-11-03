# task-03 — GREEN исправлений раунда 2

Дата: 2026-11-03. Windows, Release, специализированный Roslyn MCP `run_test_by_filter`; filter `FullyQualifiedName~Baseline|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`, `noBuild=false`.

Итог: **364/364**, 0 падений: 241 baseline case (включая 13 новых real-MSBuild регрессий), все 123 SourceStructure. Код и тесты заморожены после этого прогона. Полная solution build, main suite и opted-in AnalyzerLifecycle — у координатора, в этот focused результат не включены.

Инвентарь сохранён строгим: 16 точных identities, каждого kind и source path. Новые `BeforeDiskWatcherStartForTests`, `AfterDiskWatcherStartForTests` — `TargetOutsideTheScope` в SolutionManager.cs; `onObservationGap`, достигнутый через `_onObservationGap`, — `TargetOutsideTheScope` в WorkspaceInputSession.cs. Две границы позволяют удержать запуск и доставить событие сразу после него; третья — синхронный callback, принимаемый закрываемой watcher-сессией. Проверка `Production_reachability_records_only_the_known_delegate_limits` в SourceSetAnalysisTests.cs, count — `Assert.Equal(expectedMembers.Length, reachability.Limits.Count)`.

Ниже реальный вывод: первый поведенческий GREEN имел единственное расхождение inventory13→16; диагностический прогон выявил identities/kinds; итоговый повтор прошёл. Временный диагностический Assert.True возвращён к исходному строгому Assert.Equal. Incremental финальный build напечатал одно прежнее xUnit1031; предыдущий focused build напечатал все три прежних предупреждения. Новых предупреждений не заявлено.

## Позднее исправление изоляции тестов — R3-001

После приведённого ниже GREEN независимое ревью выявило общий process-wide FailNextStartForTests: параллельная MSBuild-загрузка могла потребить или сбросить флаг. BaselineObservationGapTests включён в отдельную xUnit collection с DisableParallelization=true (новый BaselineObservationGapCollection.cs), по существующему шаблону репозитория. Production и assertions не изменены. Нестабильное RED-воспроизведение гонки не выполнялось и не заявляется. Это позднее исправление scheduling metadata; приведённые результаты 364/364 относятся к коду до него. Финальную сборку и проверки после повторной заморозки выполняет координатор.

## Итоговый GREEN до исправления изоляции

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **364** · Passed: **364** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes', 'RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary', 'RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing`

### Build warnings

- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## До обновления exact inventory

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

❌ 1 Tests Failed.

Total: **364** · Passed: **363** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits`
   **Error:**

```text
Assert.Equal() Failure: Values differ
Expected: 13
Actual:   16
```
   **Stack:** at RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\SourceStructure\SourceSetAnalysisTests.cs:line 1064    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)   Passed RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Delegate_self_read_terminates_and_the_assigned_target_is_reached [3 ms]   Passed RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Delegate_invoke_method_group_of_a_scope_member_is_followed [2 ms]   Passed RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Compilation_error_is_an_input_diagnostic_and_the_analysis_is_not_usable [2 ms]   Passed RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Ambiguous_overload_is_a_diagnostic_instead_of_a_pick [2 ms]   Passed RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Same_named_types_in_different_namespaces_stay_distinct [2 ms]   Passed RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests....

2. **TestName:** `RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits`

### Execution context
- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.402` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.402\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.402\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **WorkingDirectory:** `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests`
- **DOTNET_CLI_UI_LANGUAGE:** `en-US`
- **Configuration:** Release
- **Platform:** (SDK/solution default)
- **BuildArgs:** (none)
- **PreTestBuild:** yes (`dotnet build` then `dotnet test --no-build`)
- **TestOutput:** StdOut 100000 chars, StdErr 100000 chars (`includeFullOutput`)

### Build warnings

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Фактические identities/kinds из анализа

## Filtered test run

**Filter:** `FullyQualifiedName~.SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits`
**Match:** Name suffix `.SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits`

❌ 1 Tests Failed.

Total: **1** · Passed: **0** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits`
   **Error:**

```text
TargetOutsideTheScope: `DuringDiskFlushForTests` invoked at E:\Devel\RoslynMcpServer-rapid-tia\Services\Workspace\SolutionManager.cs:2353 has no assignment inside the declared scope; targets registered outside the scope are not followed
TargetOutsideTheScope: `AfterPhysicalLoadBeforePrepareAsync`, reached from the invoked delegate `boundarySeam` at E:\Devel\RoslynMcpServer-rapid-tia\Services\Workspace\SolutionManager.cs:999, has no assignment inside the declared scope; targets registered outside the scope are not followed
TargetOutsideTheScope: `BeforeDiskWatcherStartForTests` invoked at E:\Devel\RoslynMcpServer-rapid-tia\Services\Workspace\SolutionManager.cs:2724 has no assignment inside the declared scope; targets registered outside the scope are not followed
TargetOutsideTheScope: `AfterDiskWatcherStartForTests` invoked at E:\Devel\RoslynMcpServer-rapid-tia\Services\Workspace\SolutionManager.cs:2734 has no assignment inside the declared scope; targets registered outside the scope ar

...[MIDDLE LOG TRUNCATED]...

e: `AfterDiskWatcherChangeForTests` invoked at E:\Devel\RoslynMcpServer-rapid-tia\Services\Workspace\SolutionManager.cs:2783 has no assignment inside the declared scope; targets registered outside the scope are not followed
TargetOutsideTheScope: `beforeProject` invoked at E:\Devel\RoslynMcpServer-rapid-tia\Services\Navigation\SearchIndexPrewarm.cs:70 has no assignment inside the declared scope; targets registered outside the scope are not followed
TargetOutsideTheScope: `beforeRead`, reached from the invoked delegate `BeforeRead` at E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineSession.cs:261, has no assignment inside the declared scope; targets registered outside the scope are not followed
TargetOutsideTheScope: `beforeCommit`, reached from the invoked delegate `BeforeCommit` at E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineSession.cs:289, has no assignment inside the declared scope; targets registered outside the scope are not followed
TargetOutsideTheScope: `update` invoked at E:\Devel\RoslynMcpServer-rapid-tia\Services\Models\WorkspaceInputSession.cs:580 has no assignment inside the declared scope; targets registered outside the scope are not followed
TargetOutsideTheScope: `onObservationGap`, reached from the invoked delegate `_onObservationGap` at E:\Devel\RoslynMcpServer-rapid-tia\Services\Models\WorkspaceInputSession.cs:228, has no assignment inside the declared scope; targets registered outside the scope are not followed
```
   **Stack:** at RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\SourceStructure\SourceSetAnalysisTests.cs:line 1064    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr) Total tests: 1      Failed: 1  Total time: 3.3286 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).      1>_VSTestConsole:          MSB4181: The "VSTestTask" task returned false but did not log an error.      1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED. Build FAILED.     0 Warning(s)     0 Error(s) Time Elapsed 00:00:03.70

2. **TestName:** `RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits`

### Execution context
- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.402` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.402\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.402\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **WorkingDirectory:** `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests`
- **DOTNET_CLI_UI_LANGUAGE:** `en-US`
- **Configuration:** Release
- **Platform:** (SDK/solution default)
- **BuildArgs:** (none)
- **PreTestBuild:** yes (`dotnet build` then `dotnet test --no-build`)
- **TestOutput:** StdOut 100000 chars, StdErr 100000 chars (`includeFullOutput`)
Full report: pass reportCursor=`5b203e3dacac414d9f42584030c70967` to this same tool.
