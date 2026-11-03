# task-03: unavailable-publication regression RED, 2026-11-03

Added after initial implementation during final lifecycle audit: an opt-in load with discarded provenance withholds its semantic publication and must not expose its provisional write journal as a baseline identity. Real MSBuild load completes; no capture task starts. Pre-fix GetSnapshot incorrectly returned the provisional workspace path. Actual focused RED before the getter fix: 1 executed, 0 passed, 1 expected Assert.Null failure.

This is a late test, recorded separately. The other late linked-context guard was added before the pending-by-path optimization and passed 1/1 against the previous scan implementation; it is equivalence coverage, not a claimed behavior RED.

Roslyn MCP run_specific_test, Release, noBuild=false, project RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj. Actual output:

## Filtered test run

**Filter:** `FullyQualifiedName~.BaselineSessionIntegrationTests.Unavailable_opt_in_publication_does_not_create_a_baseline_from_its_provisional_journal`
**Match:** Name suffix `.BaselineSessionIntegrationTests.Unavailable_opt_in_publication_does_not_create_a_baseline_from_its_provisional_journal`

❌ 1 Tests Failed.

Total: **1** · Passed: **0** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Unavailable_opt_in_publication_does_not_create_a_baseline_from_its_provisional_journal`
   **Error:**

```text
Assert.Null() Failure: Value is not null
Expected: null
Actual:   "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Unavailable_opt_in_publication_does_not_create_a_baseline_from_its_provisional_journal() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSessionIntegrationTests.cs:line 372    at RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Unavailable_opt_in_publication_does_not_create_a_baseline_from_its_provisional_journal() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineSessionIntegrationTests.cs:line 374 --- End of stack trace from previous location --- Total tests: 1      Failed: 1  Total time: 1.6267 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).      1>_VSTestConsole:          MSB4181: The "VSTestTask" task returned false but did not log an error.      1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED. Build FAILED.     0 Warning(s)     0 Error(s) Time Elapsed 00:00:01.99

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests.Unavailable_opt_in_publication_does_not_create_a_baseline_from_its_provisional_journal`

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
