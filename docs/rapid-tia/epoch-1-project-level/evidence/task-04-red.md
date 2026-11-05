# task-04 — TDD RED

- Дата: 2026-11-05 (Europe/Moscow).
- Координатор запустил Roslyn MCP `run_test_by_filter` в Release с `noBuild=false`.
- Фильтр: `FullyQualifiedName~ProjectImpactMapper`.
- До production-реализации: `ProjectImpactMapper.MapAsync` содержит только `throw new NotImplementedException()`; результат и тестовые фикстуры компилируются.
- Итог: 37 выполненных тестов, 0 passed, 37 failed; ниже исходный ответ MCP. Компиляция и реальные сборки фикстур завершились до ожидаемых ошибок mapper.
- Дополнительный `execute_dotnet_command` с тем же фильтром, `--no-build --no-restore` и TRX logger: 37 total/executed/failed, 0 passed/notExecuted/error/timeout/aborted. Во всех 37 ErrorInfo присутствует `NotImplementedException`; ошибок фикстур нет. TRX: ignored `TestResults/task-04-red/red.trx`. Специализированный фильтр не принимает настройки logger и выводит только первые пять failures, поэтому использован raw MCP runner.

+## Filtered test run

**Filter:** `FullyQualifiedName~ProjectImpactMapper`
**Match:** Caller-supplied VSTest filter

❌ 37 Tests Failed.

Total: **37** · Passed: **0** · Failed: **37**

1. **TestName:** `RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.ProjectImpactMapper.MapAsync(Solution solution, WorkspaceInputMap inputMap, IEnumerable'1 changedPaths, String loadedWorkspacePath, Boolean projectGraphStale, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\ProjectImpactMapper.cs:line 16    at RoslynMcpServer.Tests.Testing.ProjectImpactFixture.MapWithStateAsync(Boolean stale, String[] paths) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactFixture.cs:line 58    at RoslynMcpServer.Tests.Testing.ProjectImpactFixture.MapAsync(String[] paths) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactFixture.cs:line 55    at RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped(String name) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactMapperTests.cs:line 170 --- End of stack trace from previous location ---

2. **TestName:** `RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.ProjectImpactMapper.MapAsync(Solution solution, WorkspaceInputMap inputMap, IEnumerable'1 changedPaths, String loadedWorkspacePath, Boolean projectGraphStale, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\ProjectImpactMapper.cs:line 16    at RoslynMcpServer.Tests.Testing.ProjectImpactFixture.MapWithStateAsync(Boolean stale, String[] paths) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactFixture.cs:line 58    at RoslynMcpServer.Tests.Testing.ProjectImpactFixture.MapAsync(String[] paths) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactFixture.cs:line 55    at RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped(String name) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactMapperTests.cs:line 170 --- End of stack trace from previous location ---

3. **TestName:** `RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.ProjectImpactMapper.MapAsync(Solution solution, WorkspaceInputMap inputMap, IEnumerable'1 changedPaths, String loadedWorkspacePath, Boolean projectGraphStale, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\ProjectImpactMapper.cs:line 16    at RoslynMcpServer.Tests.Testing.ProjectImpactFixture.MapWithStateAsync(Boolean stale, String[] paths) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactFixture.cs:line 58    at RoslynMcpServer.Tests.Testing.ProjectImpactFixture.MapAsync(String[] paths) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactFixture.cs:line 55    at RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped(String name) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactMapperTests.cs:line 170 --- End of stack trace from previous location ---

4. **TestName:** `RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Unmapped_ignored_output_is_neither_expanded_nor_uncovered`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.ProjectImpactMapper.MapAsync(Solution solution, WorkspaceInputMap inputMap, IEnumerable'1 changedPaths, String loadedWorkspacePath, Boolean projectGraphStale, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\ProjectImpactMapper.cs:line 16    at RoslynMcpServer.Tests.Testing.ProjectImpactFixture.MapWithStateAsync(Boolean stale, String[] paths) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactFixture.cs:line 58    at RoslynMcpServer.Tests.Testing.ProjectImpactFixture.MapAsync(String[] paths) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactFixture.cs:line 55    at RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Unmapped_ignored_output_is_neither_expanded_nor_uncovered(String directory, String name) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactMapperTests.cs:line 125 --- End of stack trace from previous location ---

5. **TestName:** `RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Unmapped_ignored_output_is_neither_expanded_nor_uncovered`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.ProjectImpactMapper.MapAsync(Solution solution, WorkspaceInputMap inputMap, IEnumerable'1 changedPaths, String loadedWorkspacePath, Boolean projectGraphStale, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\ProjectImpactMapper.cs:line 16    at RoslynMcpServer.Tests.Testing.ProjectImpactFixture.MapWithStateAsync(Boolean stale, String[] paths) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactFixture.cs:line 58    at RoslynMcpServer.Tests.Testing.ProjectImpactFixture.MapAsync(String[] paths) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactFixture.cs:line 55    at RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Unmapped_ignored_output_is_neither_expanded_nor_uncovered(String directory, String name) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\ProjectImpactMapperTests.cs:line 125 --- End of stack trace from previous location ---

[!] Showing first 5 failures only.

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
