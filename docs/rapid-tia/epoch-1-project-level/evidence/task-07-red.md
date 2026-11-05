# task-07 — TDD RED

- Дата: **2026-11-06** (Europe/Moscow); база `1bd701f`.
- До production-поведения: `VstestRunPlanner.Plan` содержит только `throw new NotImplementedException()`; две DTO и тестовый класс компилируются.
- Первым написан `VstestRunPlannerTests`: 16 replay-проб сохранённого каталога task-06 и 22 boundary/regression cases, всего **38** фактически выполненных tests. Предварительная ручная оценка исполнителя 39 исправлена по фактическому discovery.
- Roslyn MCP `run_test_by_filter`, Release `noBuild=false`; дополнительный `execute_dotnet_command` с тем же фильтром и `--no-build --no-restore --logger trx` нужен для полного списка ошибок/счётчиков, которого не выражает специализированный runner (выводит первые пять failures).
- TRX: ignored `TestResults/task-07-red/red.trx`. Проверено total=executed=failed=38; passed/error/timeout/aborted/notExecuted=0. Во всех 38 ErrorInfo присутствует `NotImplementedException` (в cancellation-тесте — inner exception); ошибок компиляции, fixtures или чтения offline-каталога нет.
- SHA-256 первоначального тестового файла: `25AB0649527040144545C5843921CF209D87EFE6B02CD333B1856090010A7677`; до окончания RED не менялся. Поведение разрешено реализовывать только после проверки координатором.

## Проверенные TRX counters

```json
{
  "Total": 38,
  "Executed": 38,
  "Passed": 0,
  "Failed": 38,
  "Error": 0,
  "Timeout": 0,
  "Aborted": 0,
  "NotExecuted": 0,
  "UnexpectedFailureReasons": 0,
  "TestFileSha256": "25AB0649527040144545C5843921CF209D87EFE6B02CD333B1856090010A7677"
}
```

## Исходный ответ специализированного MCP

## Filtered test run

**Filter:** `FullyQualifiedName~VstestRunPlannerTests`
**Match:** Caller-supplied VSTest filter

❌ 38 Tests Failed.

Total: **38** · Passed: **0** · Failed: **38**

1. **TestName:** `RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.NUnit_generic_type_arguments_alone_do_not_mean_fixture_constructor_arguments`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.VstestRunPlanner.Plan(TestImpactResult impact, IReadOnlyList'1 currentClasses, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\VstestRunPlanner.cs:line 16    at RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Plan(TestImpactResult impact, VstestTestClass[] classes) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestRunPlannerTests.cs:line 299    at RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.NUnit_generic_type_arguments_alone_do_not_mean_fixture_constructor_arguments() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestRunPlannerTests.cs:line 225    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)   Failed RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Unresolved_runtime_identity_runs_the_whole_project_with_the_supplied_reason(reason: "Ambiguous adapter runtime identity.") [< 1 ms]   Error Message:    System.NotImplementedException : The method or operation is not implemented.   Stack Trace:    ...

2. **TestName:** `RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Class_collapse_happens_before_applying_the_filter_budget`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.VstestRunPlanner.Plan(TestImpactResult impact, IReadOnlyList'1 currentClasses, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\VstestRunPlanner.cs:line 16    at RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Plan(TestImpactResult impact, VstestTestClass[] classes) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestRunPlannerTests.cs:line 299    at RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Class_collapse_happens_before_applying_the_filter_budget() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestRunPlannerTests.cs:line 277    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

3. **TestName:** `RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Saved_adapter_names_select_every_measured_case`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.VstestRunPlanner.Plan(TestImpactResult impact, IReadOnlyList'1 currentClasses, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\VstestRunPlanner.cs:line 16    at RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Plan(TestImpactResult impact, VstestTestClass[] classes) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestRunPlannerTests.cs:line 299    at RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Saved_adapter_names_select_every_measured_case(String adapterName, String probe) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestRunPlannerTests.cs:line 54    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr) [xUnit.net 00:00:00.52]       System.NotImplementedException : The method or operation is not implemented. [xUnit.net 00:00:00.52]       Stack Trace: [xUnit.net 00:00:00.52]         E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\VstestRunPlanner.cs(16,0): at RoslynMcpServe...

4. **TestName:** `RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Cancellation_is_observed`
   **Error:**

```text
Assert.ThrowsAny() Failure: Exception type was not compatible
Expected: typeof(System.OperationCanceledException)
Actual:   typeof(System.NotImplementedException)
---- System.NotImplementedException : The method or operation is not implemented.
```
   **Stack:** at RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Cancellation_is_observed() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestRunPlannerTests.cs:line 295    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr) ----- Inner Stack Trace -----    at RoslynMcpServer.Services.Testing.VstestRunPlanner.Plan(TestImpactResult impact, IReadOnlyList'1 currentClasses, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\VstestRunPlanner.cs:line 16    at RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.<>c__DisplayClass16_0.<Cancellation_is_observed>b__0() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestRunPlannerTests.cs:line 295 Total tests: 38      Failed: 38  Total time: 1.0059 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).      1>_VSTestConsole:          MSB4181: The "VSTestTask" task returned false but did not log an error.      1>Done Building Project "E:...

5. **TestName:** `RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.NUnit_generic_type_arguments_alone_do_not_mean_fixture_constructor_arguments`

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
