# Task-09 — RED

Дата: 2026-11-06. Исполнитель: Codex (точный runtime model ID средой не предоставлен).

До реализации написаны все сценарные тесты task-09: `DotNetTestExecutorTests` (11), `EstimateAffectedTestsRunTests` (6), `EstimateAffectedTestsCancellationTests` (1), обновлён контракт поверхности `EstimateAffectedTestsCatalogTests` (2). Production до RED содержит только пустой контракт исполнителя с `NotImplementedException`, тип исхода/результата и параметры `run`/`timeoutSeconds`, которые обработчик ещё игнорирует.

Проверки: независимые исходы build/test (включая timeout/cancel обеих стадий), отдельный timeout процесса, уже отменённый вызов; настоящий MSBuild workspace, совпадение фильтра с `run`, старые DLL и новый failing source, продолжение после failed/build failed/timeout, whole-project и пустой план; настоящая отмена `tools/call` через MCP client/server transport, PID testhost/дочернего процесса, последующие проекты и однострочный server log.

Roslyn MCP `run_test_by_filter`, Release, `noBuild:false`, timeout 600. Сборка успешна, обнаружено 20 тестов; **0 passed / 20 failed / 0 skipped**. Ожидаемые причины: пустой исполнитель (`NotImplementedException`), прежний estimate-only ответ без исходов, процесс не стартует при `run`, прежний read-only descriptor. Ошибки компиляции на предварительной настройке fixture устранены до этого запуска и RED не считаются. Ручной фильтр в первом интеграционном тесте до падающего assert реально исполнил First1/First2/FirstExtra и не Other.

````text
## Filtered test run

**Filter:** `FullyQualifiedName~DotNetTestExecutorTests|FullyQualifiedName~EstimateAffectedTestsRunTests|FullyQualifiedName~EstimateAffectedTestsCancellationTests|FullyQualifiedName~EstimateAffectedTestsCatalogTests`
**Match:** Caller-supplied VSTest filter

❌ 20 Tests Failed.

Total: **20** · Passed: **0** · Failed: **20**

1. **TestName:** `RoslynMcpServer.Tests.TestRunner.DotNetTestExecutorTests.Execution_returns_an_unambiguous_outcome`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Build.DotNetTestExecutor.ExecuteAsync(CliPlan plan, String workingDirectory, Nullable'1 timeout, Boolean timeoutPerProcess, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Build\DotNetTestExecutor.cs:line 15    at RoslynMcpServer.Tests.TestRunner.DotNetTestExecutorTests.Execution_returns_an_unambiguous_outcome(String scenario, Object expected, Int32 calls) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\TestRunner\DotNetTestExecutorTests.cs:line 40 --- End of stack trace from previous location ---   Failed RoslynMcpServer.Tests.TestRunner.DotNetTestExecutorTests.Execution_returns_an_unambiguous_outcome(scenario: "failed", expected: Failed, calls: 2) [< 1 ms]   Error Message:    System.NotImplementedException : The method or operation is not implemented.   Stack Trace:      at RoslynMcpServer.Services.Build.DotNetTestExecutor.ExecuteAsync(CliPlan plan, String workingDirectory, Nullable'1 timeout, Boolean timeoutPerProcess, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Build\DotNetTestExecutor.cs:line 15    at RoslynMcpServer.Tests.TestRunner.DotNetTestExecutor...

2. **TestName:** `RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests.Estimate_is_a_core_process_tool_in_both_profiles_with_minimal_run_parameters`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests.Estimate_is_a_core_process_tool_in_both_profiles_with_minimal_run_parameters(String profile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Hosting\EstimateAffectedTestsCatalogTests.cs:line 24    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)

3. **TestName:** `RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests.Estimate_is_a_core_process_tool_in_both_profiles_with_minimal_run_parameters`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests.Estimate_is_a_core_process_tool_in_both_profiles_with_minimal_run_parameters(String profile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Hosting\EstimateAffectedTestsCatalogTests.cs:line 24    at InvokeStub_EstimateAffectedTestsCatalogTests.Estimate_is_a_core_process_tool_in_both_profiles_with_minimal_run_parameters(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithOneArg(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture) [xUnit.net 00:00:04.71]       The selected test did not start its process tree. [xUnit.net 00:00:04.71]       Stack Trace: [xUnit.net 00:00:04.71]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\TestImpactRunFixture.cs(103,0): at RoslynMcpServer.Tests.Testing.TestImpactRunFixture.WaitForProcessesAsync(Task operation) [xUnit.net 00:00:04.71]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsCancellationTests.cs(54,0): at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsCancellationTests.Tools_call_cancellation_kills_the_process_tree_skips_later_...

4. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsCancellationTests.Tools_call_cancellation_kills_the_process_tree_skips_later_projects_and_logs_all_outcomes`
   **Error:** The selected test did not start its process tree.
   **Stack:** at RoslynMcpServer.Tests.Testing.TestImpactRunFixture.WaitForProcessesAsync(Task operation) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\TestImpactRunFixture.cs:line 103    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsCancellationTests.Tools_call_cancellation_kills_the_process_tree_skips_later_projects_and_logs_all_outcomes() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsCancellationTests.cs:line 54    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsCancellationTests.Tools_call_cancellation_kills_the_process_tree_skips_later_projects_and_logs_all_outcomes() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsCancellationTests.cs:line 91    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsCancellationTests.Tools_call_cancellation_kills_the_process_tree_skips_later_projects_and_logs_all_outcomes() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsCancellationTests.cs:line 91    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsCancellationTests.Tools_call_cancellation_kills_the_process_tree_skips_later_projects_an...

5. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsRunTests.Real_filter_and_run_execute_the_same_expected_tests_with_only_name_substring_extras`
   **Error:**

```text
Assert.Contains() Failure: Sub-string not found
String:    "Approximate result (rapid TIA): not a sub"···
Not found: "passed"
```
   **Stack:** at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsRunTests.AssertRun(String response, String outcome) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsRunTests.cs:line 125    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsRunTests.Real_filter_and_run_execute_the_same_expected_tests_with_only_name_substring_extras() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsRunTests.cs:line 25    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsRunTests.Real_filter_and_run_execute_the_same_expected_tests_with_only_name_substring_extras() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsRunTests.cs:line 28 --- End of stack trace from previous location --- [xUnit.net 00:00:09.63]       Assert.Contains() Failure: Sub-string not found [xUnit.net 00:00:09.63]       String:    "Approximate result (rapid TIA): not a sub"··· [xUnit.net 00:00:09.63]       Not found: "failed" [xUnit.net 00:00:09.63]       Stack Trace: [xUnit.net 00:00:09.63]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsRunTests.cs(125,0): at R...

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
````
