# task-03 — validation до поздних исправлений тестовых предусловий

- Дата: 2026-11-03.
- База31cc8d8, первый CODE FROZEN восьми C# файлов, условный ACCEPT [раунда6](task-03-review-round-6.md).
- Обе Release solution build успешны (Windows SDK10.0.402, Linux SDK10.0.113); прежние3warnings.
- Координатор Windows focused385/385 прошёл. Точный SourceStructure inventory16 не менялся.
- Windows main: **1687 total /1686 passed /1 failed**. Новая регрессия порядка не дождалась текущего directory rename; точная причина и последующая правка должны быть подтверждены исполнителем.
- Linux main: **1683 total /1682 passed /1 failed /0 skipped**. Startup_gap_survives_first_publication(missingDirectory:false) завершился Unknown для Other/A.cs и Other/B.cs после загрузки другого проекта. [Полный лог](task-03-review-round-5-linux/r6-main-run-1-failed.log), [TRX summary](task-03-review-round-5-linux/r6-main-run-1-failed-trx-summary.xml).
- Linux batch1793737548207 остановлен на первом падении, прогоны2/3 не запускались. Эти результаты не считаются тремя успешными Linux-прогонами.
- Runtime ошибки возвращены тому же исполнителю как продолжение R5-001/R5-002; дополнительных исправленных finding ID сами эти попытки не создают.
- Приёмка задачи не объявлена. После исправления обязательные checks повторяются на окончательном коде.

## Фактический Windows main output

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

❌ 1 Tests Failed.

Total: **1687** · Passed: **1686** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceInputWatcherDeliveryTests.Pre_read_directory_rename_then_semantic_sync_preserves_bom_and_project_files`
   **Error:**

```text
Assert.Contains() Failure: Sub-string not found
String:    "public class Program { public int BomFree"···
Not found: "AfterRenameRead"
```
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceInputWatcherDeliveryTests.Directory_rename_inside_a_membership_region_stays_unknown_after_re_read() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceInputWatcherDeliveryTests.cs:line 177    at RoslynMcpServer.Tests.Workspace.WorkspaceInputWatcherDeliveryTests.Pre_read_directory_rename_then_semantic_sync_preserves_bom_and_project_files() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceInputWatcherDeliveryTests.cs:line 273 --- End of stack trace from previous location ---   Passed RoslynMcpServer.Tests.WorkspaceStateCache.IsolatedWorkspaceHydratorTests.Analyzer_identity_does_not_authorize_assembly_loading [146 ms]   Passed RoslynMcpServer.Tests.WorkspaceLoadCache.OracleFixtureBuilderTests.RestoreAsync_returns_exact_original_bytes_after_mutation(scenarioName: "Encodings") [161 ms]   Passed RoslynMcpServer.Tests.Build.BuildProgressIntegrationTests.Run_dotnet_build_reports_protocol_progress_for_a_live_build [5 s]   Passed RoslynMcpServer.Tests.Build.BuildProgressIntegrationTests.Report_emits_strictly_increasing_seconds_and_resets_the_message_on_a_step_boundary [4 ms]   Pass...

2. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceInputWatcherDeliveryTests.Pre_read_directory_rename_then_semantic_sync_preserves_bom_and_project_files`

### Execution context
- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.402` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.402\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.402\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **SDK from MSBuild log paths:** `10.0.402`
- **WorkingDirectory:** `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests`
- **DOTNET_CLI_UI_LANGUAGE:** `en-US`
- **Configuration:** Release
- **Platform:** (SDK/solution default)
- **BuildArgs:** (none)
- **PreTestBuild:** skipped (`noBuild=true`)
- **TestOutput:** StdOut 100000 chars, StdErr 100000 chars (`includeFullOutput`)
