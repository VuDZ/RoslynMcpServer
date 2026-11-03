# task-01: additional regressions RED, 2026-11-03

Added after initial implementation during self-review: namespace using scope, extern aliases, and preprocessor diagnostic messages. These three cases were run before their production fixes. Roslyn MCP `run_specific_test`, Release, noBuild=false, same project/class as the initial RED run.

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentSnapshotBuilderTests`
**Match:** Class name contains `BaselineDocumentSnapshotBuilderTests`

❌ 3 Tests Failed.

Total: **31** · Passed: **28** · Failed: **3**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Preprocessor_diagnostic_messages_are_covered`
   **Error:**

```text
Assert.NotEqual() Failure: Strings are equal
Expected: Not "F02BAF6EA9826ABA022806B058B9E0D5D0A22CF228AF85EDC9"···
Actual:       "F02BAF6EA9826ABA022806B058B9E0D5D0A22CF228AF85EDC9"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Preprocessor_diagnostic_messages_are_covered() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 216    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Generic_overloads_have_distinct_keys [1 ms] [xUnit.net 00:00:00.29]       Assert.NotEqual() Failure: Strings are equal [xUnit.net 00:00:00.29]       Expected: Not "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA4"··· [xUnit.net 00:00:00.29]       Actual:       "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA4"··· [xUnit.net 00:00:00.29]       Stack Trace: [xUnit.net 00:00:00.29]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs(210,0): at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Import_scope_and_extern_alias_changes_are_covered(String before, String after) [xUnit.net 00:00:00.29]          ...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Preprocessor_diagnostic_messages_are_covered`

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Import_scope_and_extern_alias_changes_are_covered`

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Import_scope_and_extern_alias_changes_are_covered`

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
