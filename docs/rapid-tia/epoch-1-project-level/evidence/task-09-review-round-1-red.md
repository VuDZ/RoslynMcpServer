# Task-09 R1-001 — RED

2026-11-06; Codex (runtime model ID средой не предоставлен). До исправления production добавлен guard к настоящему build-failure integration и новый настоящий xUnit failure-output scenario с ITestOutputHelper (6000 символов). Публичная schema не менялась.

Roslyn MCP run_test_by_filter, Release, noBuild=false, timeout300: **2 tests /0 passed /2 expected failed /0 skipped** после успешной сборки. Build failure возвращает неподдерживаемый reportCursor; truncated StdOut сохраняет бюджет2500/head+tail, но предлагает неподдерживаемые includeFullOutput/maxOutputChars. RED достигается после проверки реального failed outcome и границ truncated output; прежние execution assertions сохранены. RED не связан с compile/fixture failure. Логирование полного вложенного ответа из нового теста убрано до записанного прогона: оно заставляло внешний parser смешивать nested summaries; проверки поведения не менялись.

````text
## Filtered test run

**Filter:** `FullyQualifiedName~Build_failure_does_not_start_old_tests_and_the_next_project_runs|FullyQualifiedName~Truncated_failure_output_keeps_its_budget`
**Match:** Caller-supplied VSTest filter

❌ 2 Tests Failed.

Total: **2** · Passed: **0** · Failed: **2**

1. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsRunTests.Truncated_failure_output_keeps_its_budget_without_suggesting_unsupported_parameters`
   **Error:**

```text
Assert.DoesNotContain() Failure: Sub-string found
↓ (pos 3553)
String: ···" (head+tail). Pass `includeFullOutput=tru"···
Found:  "includeFullOutput"
```
   **Stack:** at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsRunTests.Truncated_failure_output_keeps_its_budget_without_suggesting_unsupported_parameters() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsRunTests.cs:line 109    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsRunTests.Truncated_failure_output_keeps_its_budget_without_suggesting_unsupported_parameters() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsRunTests.cs:line 111 --- End of stack trace from previous location --- [xUnit.net 00:00:10.30]       Assert.DoesNotContain() Failure: Sub-string found [xUnit.net 00:00:10.30]                                         ↓ (pos 1449) [xUnit.net 00:00:10.30]       String: ···"\r\nFull report: pass reportCursor='5ea3234"··· [xUnit.net 00:00:10.30]       Found:  "reportCursor" [xUnit.net 00:00:10.30]       Stack Trace: [xUnit.net 00:00:10.30]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsRunTests.cs(89,0): at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsRunTests.Build_failure_does_not_start_old_tests_and_the_next_project_runs() [xUnit....

2. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsRunTests.Build_failure_does_not_start_old_tests_and_the_next_project_runs`
   **Error:**

```text
Assert.DoesNotContain() Failure: Sub-string found
↓ (pos 1449)
String: ···"\r\nFull report: pass reportCursor=`5ea3234"···
Found:  "reportCursor"
```
   **Stack:** at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsRunTests.Build_failure_does_not_start_old_tests_and_the_next_project_runs() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsRunTests.cs:line 89    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsRunTests.Build_failure_does_not_start_old_tests_and_the_next_project_runs() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsRunTests.cs:line 92 --- End of stack trace from previous location --- Total tests: 2      Failed: 2  Total time: 10.6084 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).      1>_VSTestConsole:          MSB4181: The "VSTestTask" task returned false but did not log an error.      1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED. Build FAILED.     0 Warning(s)     0 Error(s) Time Elapsed 00:00:10.98

3. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsRunTests.Truncated_failure_output_keeps_its_budget_without_suggesting_unsupported_parameters`

4. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsRunTests.Build_failure_does_not_start_old_tests_and_the_next_project_runs`

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
````
