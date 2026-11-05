# task-03 — R21-001 RED

Дата:2026-11-05, база1622e41. Все13 новых rows написаны до production:7 counted-list probes,3 native workloads,3 deterministic ignored Deleted guards. Raw TRX/logs/source manifests сохранены до fix. [Measurements и boundaries](task-03-review-round-21-measurements.md).

Windows deterministic RED7/0/7: Paths enumeration1, occurrences50→200 дляnode_modules и100→400 дляobj/artifacts. Linux deterministic RED7/0/7, kernel limits прежние. Counter reset только после ctor/publication разрешает precompute при construction; callback должен читать0 элементов. Native Windows before3/0/3: прямые InternalBufferOverflowException; node50/200 genuine regression. Obj no-gap assertion после1b comparison признана лишним harness contract и scope-corrected, не production workaround. Linux nativebefore3/3PASS — overflow WSL не воспроизвёлся, не выдаётся за RED. Baseline1b Linux3/3PASS; Windows base2/1 с inherited obj overflow. Три deterministic obj/bin/artifacts suppression guards уже PASS до fix.

## Filtered test run

**Filter:** `FullyQualifiedName~.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences`
**Match:** Name suffix `.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences`

❌ 7 Tests Failed.

Total: **7** · Passed: **0** · Failed: **7**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences`
   **Error:**

```text
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(Int32 sourceCount, String directoryName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDeletedPathCostTests.cs:line 68    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)
   **StdOut:**

```text
Sources=50; directory=obj; pathReads=1; occurrenceReads=100.
Failed RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(sourceCount: 200, directoryName: "node_modules") [< 1 ms]
Error Message:
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
Stack Trace:
at RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(Int32 sourceCount, String directoryName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDeletedPathCostTests.cs:line 68
at InvokeStub_BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(Object, Span`1)
at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
Standard Output Messages:
Sources=200; directory=node_modules; pathReads=1; occurrenceReads=200.
Failed RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(sourceCount: 50, directoryName: "node_modules") [< 1 ms]
Error Message:
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
Stack Trace:
at RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(Int32 sourceCount, String directoryName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDeletedPathCostTests.cs:line 68
at InvokeStub_BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(Object, Span`1)
at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
Standard Output Messages:
Sources=50; directory=node_modules; pathReads=1; occurrenceReads=50.
Failed RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(sourceCount: 50, directoryName: "bin") [< 1 ms]
Error Message:
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
Stack Trace:
at RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(Int32 sourceCount, String directoryName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDeletedPathCostTests.cs:line 68
at InvokeStub_BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(Object, Span`1)
at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
Standard Output Messages:
Sources=50; directory=bin; pathReads=1; occurrenceReads=100.
Failed RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(sourceCount: 200, directoryName: "ordinary") [< 1 ms]
Error Message:
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
Stack Trace:
at RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(Int32 sourceCount, String directoryName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDeletedPathCostTests.cs:line 68
at InvokeStub_BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(Object, Span`1)
at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
Standard Output Messages:
Sources=200; directory=ordinary; pathReads=1; occurrenceReads=200.
Failed RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(sourceCount: 200, directoryName: "artifacts") [< 1 ms]
Error Message:
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
Stack Trace:
at RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(Int32 sourceCount, String directoryName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDeletedPathCostTests.cs:line 68
at InvokeStub_BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(Object, Span`1)
at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
Standard Output Messages:
Sources=200; directory=artifacts; pathReads=1; occurrenceReads=400.
Failed RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(sourceCount: 200, directoryName: "obj") [< 1 ms]
Error Message:
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
Stack Trace:
at RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(Int32 sourceCount, String directoryName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDeletedPathCostTests.cs:line 68
at InvokeStub_BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences(Object, Span`1)
at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
Standard Output Messages:
Sources=200; directory=obj; pathReads=1; occurrenceReads=400.
Total tests: 7
Failed: 7
Total time: 0.8164 Seconds
1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).
1>_VSTestConsole:
MSB4181: The "VSTestTask" task returned false but did not log an error.
1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED.
```

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences`

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences`

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences`

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDeletedPathCostTests.Empty_deleted_path_does_not_enumerate_paths_or_occurrences`

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

### Build warnings

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]
