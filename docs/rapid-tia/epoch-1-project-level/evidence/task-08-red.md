# task-08 — RED координатора

- Дата: **2026-11-06**; база `14fe3ee`.
- Исполнитель: `/root/task08_implementer`; Codex, точный runtime model ID средой не предоставлен.
- До production-реализации написаны четыре файла tests; минимальные контракты `TestTools.EstimateAffectedTests` и `VstestTestInventory.BuildAsync` выбрасывают `NotImplementedException`, регистрации ещё нет.
- Координатор выполнил Roslyn MCP `run_test_by_filter`, Release, `noBuild=false`, `noRestore=true`; фильтр `FullyQualifiedName~EstimateAffectedTests|FullyQualifiedName~VstestTestInventoryTests`.
- Компиляция успешна; **49 выполнено, 49 failed, 0 passed**. Для проверки всех причин тот же набор повторён через MCP `execute_dotnet_command` с `--no-build --no-restore --logger "trx;LogFileName=red.trx" --results-directory TestResults/task-08-red`.
- Полный TRX: `TestResults/task-08-red/red.trx` (локальный ignored artifact). XML counters: total=49, executed=49, failed=49, passed=0, error=0, timeout=0, aborted=0, notExecuted=0. Все результаты проверены: **47** содержат ожидаемый `NotImplementedException` (в том числе проверка отмены), **2** — ожидаемую пустую регистрацию каталога. Неожиданных fixture/compile failures нет.
- После этой проверки координатор разрешил production-реализацию; это не acceptance задачи.

## SHA-256 исходных tests

- `Testing/EstimateAffectedTestsTests.cs`: `D386AE35E1EF67A17CB68501D1530C6A84F08E767D5916821704B045E080279B`.
- `Testing/EstimateAffectedTestsAdapterTests.cs`: `0190DC3D844D09A4F51F1DD3D2A7F3340007ECC2F6249CD117EE31CE8D6BC2F1`.
- `Testing/VstestTestInventoryTests.cs`: `EAEE2D09CCFB0BD14FD9A55BF40F347052C433902B88D340989A86059D639331`.
- `Hosting/EstimateAffectedTestsCatalogTests.cs`: `6E2F7466C37F424FE0C59C09CDEA2A88D34043D01E03D9C5B502487238F1C866`.

## Исходный focused-вывод

## Filtered test run

**Filter:** `FullyQualifiedName~EstimateAffectedTests|FullyQualifiedName~VstestTestInventoryTests`
**Match:** Caller-supplied VSTest filter

❌ 49 Tests Failed.

Total: **49** · Passed: **0** · Failed: **49**

1. **TestName:** `RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests.Estimate_is_read_only_core_tool_in_both_profiles_with_only_details_parameter`
   **Error:** Assert.Single() Failure: The collection was empty
   **Stack:** at RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests.Estimate_is_read_only_core_tool_in_both_profiles_with_only_details_parameter(String profile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Hosting\EstimateAffectedTestsCatalogTests.cs:line 16    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)   Failed RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests.Estimate_is_read_only_core_tool_in_both_profiles_with_only_details_parameter(profile: "full") [< 1 ms]   Error Message:    Assert.Single() Failure: The collection was empty   Stack Trace:      at RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests.Estimate_is_read_only_core_tool_in_both_profiles_with_only_details_parameter(String profile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Hosting\EstimateAffectedTestsCatalogTests.cs:line 16    at InvokeStub_EstimateAffectedTestsCatalogTests.Estimate_is_read_only_core_tool_in_both_profiles_with_only_details_parameter(Object, Span'1)    at System...

2. **TestName:** `RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.Cancellation_does_not_publish_a_partial_inventory`
   **Error:**

```text
Assert.ThrowsAny() Failure: Exception type was not compatible
Expected: typeof(System.OperationCanceledException)
Actual:   typeof(System.NotImplementedException)
---- System.NotImplementedException : The method or operation is not implemented.
```
   **Stack:** at RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.Cancellation_does_not_publish_a_partial_inventory() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestTestInventoryTests.cs:line 105 --- End of stack trace from previous location --- ----- Inner Stack Trace -----    at RoslynMcpServer.Services.Testing.VstestTestInventory.BuildAsync(Solution solution, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\VstestTestInventory.cs:line 10    at RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.<>c__DisplayClass5_0.<Cancellation_does_not_publish_a_partial_inventory>b__0() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestTestInventoryTests.cs:line 106

3. **TestName:** `RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.NUnit_generic_fixture_only_uses_measured_closed_form`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.VstestTestInventory.BuildAsync(Solution solution, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\VstestTestInventory.cs:line 10    at RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.NUnit_generic_fixture_only_uses_measured_closed_form(String attributes, String runtimeName, Boolean fixtureArguments, Boolean unresolved) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestTestInventoryTests.cs:line 77 --- End of stack trace from previous location ---

4. **TestName:** `RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.NUnit_generic_fixture_only_uses_measured_closed_form`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.VstestTestInventory.BuildAsync(Solution solution, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\VstestTestInventory.cs:line 10    at RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.NUnit_generic_fixture_only_uses_measured_closed_form(String attributes, String runtimeName, Boolean fixtureArguments, Boolean unresolved) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestTestInventoryTests.cs:line 77 --- End of stack trace from previous location ---

5. **TestName:** `RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.NUnit_generic_fixture_only_uses_measured_closed_form`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.VstestTestInventory.BuildAsync(Solution solution, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\VstestTestInventory.cs:line 10    at RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.NUnit_generic_fixture_only_uses_measured_closed_form(String attributes, String runtimeName, Boolean fixtureArguments, Boolean unresolved) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestTestInventoryTests.cs:line 77 --- End of stack trace from previous location ---

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
