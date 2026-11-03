# task-03: real MSBuild integration RED, 2026-11-03

All 16 integration cases were written before production lifecycle behavior. Each creates a real SDK project (linked-context case: real two-project .slnx), loads it through SolutionManager/MSBuild, then reaches the compilable BaselineSession.GetSnapshot stub throwing NotImplementedException. No fake workspace substitutes for the required load scenarios. Async before-read/before-commit gates are part of the test contract and will exercise the races once implemented.

Cases include early external/MCP/disk edits, separate timestamp/event guards, mid-commit write admission, separate partial documents, reload identity, stale generation tail, cached load, reset before/after partial capture, different solution, linked contexts, opt-in pre-publication events, and reset watcher-gap rescan. First fixture build needed a missing Microsoft.CodeAnalysis.CSharp using; that compile failure is not counted as RED. Actual RED: 16 executed, 0 passed, 16 expected NotImplementedException failures after successful MSBuild load.

Roslyn MCP run_specific_test, project RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj, class BaselineSessionIntegrationTests, Release, noBuild=false, timeoutSeconds=600, includeFullOutput=true. Actual output (runner displays first five failures):

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineSessionIntegrationTests`
**Match:** Class name contains `BaselineSessionIntegrationTests`

❌ 16 Tests Failed.

Total: **16** · Passed: **0** · Failed: **16**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Write_between_hashing_and_commit_cannot_publish_the_old_version`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineSession.GetSnapshot() in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineSession.cs:line 11    at RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Write_between_hashing_and_commit_cannot_publish_the_old_version() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSessionIntegrationTests.cs:line 105    at RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Write_between_hashing_and_commit_cannot_publish_the_old_version() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSessionIntegrationTests.cs:line 116 --- End of stack trace from previous location --- [xUnit.net 00:00:01.95]       System.NotImplementedException : The method or operation is not implemented. [xUnit.net 00:00:01.95]       Stack Trace: [xUnit.net 00:00:01.95]         E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineSession.cs(11,0): at RoslynMcpServer.Services.Testing.Baseline.BaselineSession.GetSnapshot() [xUnit.net 00:00:01.95]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSessionIntegrationTests.cs(279,0)...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Linked_document_contexts_are_not_collapsed_by_path_or_partial_member_key`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineSession.GetSnapshot() in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineSession.cs:line 11    at RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Linked_document_contexts_are_not_collapsed_by_path_or_partial_member_key() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSessionIntegrationTests.cs:line 279    at RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Linked_document_contexts_are_not_collapsed_by_path_or_partial_member_key() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSessionIntegrationTests.cs:line 287 --- End of stack trace from previous location ---

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Reset_before_first_read_freezes_the_published_inventory_as_unknown`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineSession.GetSnapshot() in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineSession.cs:line 11    at RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Reset_before_first_read_freezes_the_published_inventory_as_unknown() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSessionIntegrationTests.cs:line 239    at RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Reset_before_first_read_freezes_the_published_inventory_as_unknown() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSessionIntegrationTests.cs:line 248 --- End of stack trace from previous location ---

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Partial_declarations_in_distinct_documents_keep_independent_member_snapshots`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineSession.GetSnapshot() in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineSession.cs:line 11    at RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Partial_declarations_in_distinct_documents_keep_independent_member_snapshots() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSessionIntegrationTests.cs:line 124    at RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Partial_declarations_in_distinct_documents_keep_independent_member_snapshots() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSessionIntegrationTests.cs:line 147 --- End of stack trace from previous location --- [xUnit.net 00:00:03.44]       System.NotImplementedException : The method or operation is not implemented. [xUnit.net 00:00:03.44]       Stack Trace: [xUnit.net 00:00:03.44]         E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineSession.cs(11,0): at RoslynMcpServer.Services.Testing.Baseline.BaselineSession.GetSnapshot() [xUnit.net 00:00:03.44]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSession...

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Event_guard_works_when_the_original_mtime_is_preserved`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineSession.GetSnapshot() in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineSession.cs:line 11    at RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Event_guard_works_when_the_original_mtime_is_preserved() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSessionIntegrationTests.cs:line 85    at RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Event_guard_works_when_the_original_mtime_is_preserved() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSessionIntegrationTests.cs:line 95 --- End of stack trace from previous location ---

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
