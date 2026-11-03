# task-01: RED, 2026-11-03

Command: Roslyn MCP `run_specific_test`, project `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`, class `BaselineDocumentSnapshotBuilderTests`, Release, noBuild=false, includeFullOutput=true.

Production builder at this point: `Build(SyntaxTree tree) => throw new NotImplementedException();`. No implementation preceded this run. All test cases were written before this run. Tool output follows verbatim (the runner displays the first five failures).

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentSnapshotBuilderTests`
**Match:** Class name contains `BaselineDocumentSnapshotBuilderTests`

❌ 28 Tests Failed.

Total: **28** · Passed: **0** · Failed: **28**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineDocumentSnapshotBuilder.Build(SyntaxTree tree) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineDocumentSnapshotBuilder.cs:line 8    at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Snapshot(String source) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 205    at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable(String source) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 180    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)   Failed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable(source: "// empty") [< 1 ms]   Error Message:    System.NotImplementedException : The method or operation is not implemented.   Stack Trace:      at ...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineDocumentSnapshotBuilder.Build(SyntaxTree tree) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineDocumentSnapshotBuilder.cs:line 8    at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Snapshot(String source) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 205    at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes(String beforeText, String afterText, Int32 bucket) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 94    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)   Failed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes(beforeText: "using A;", afterText: "using B;", bucket: 0) [< 1 ms]   Error Message:    System.NotImpleme...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Explicit_interface_and_ordinary_method_have_distinct_keys`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineDocumentSnapshotBuilder.Build(SyntaxTree tree) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineDocumentSnapshotBuilder.cs:line 8    at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Snapshot(String source) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 205    at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Explicit_interface_and_ordinary_method_have_distinct_keys() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 22    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Editing_active_or_inactive_conditional_code_changes_member_hash`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineDocumentSnapshotBuilder.Build(SyntaxTree tree) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineDocumentSnapshotBuilder.cs:line 8    at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Snapshot(String source) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 205    at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Editing_active_or_inactive_conditional_code_changes_member_hash(String condition) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 63    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Editing_active_or_inactive_conditional_code_changes_member_hash`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineDocumentSnapshotBuilder.Build(SyntaxTree tree) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineDocumentSnapshotBuilder.cs:line 8    at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Snapshot(String source) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 205    at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Editing_active_or_inactive_conditional_code_changes_member_hash(String condition) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 63    at InvokeStub_BaselineDocumentSnapshotBuilderTests.Editing_active_or_inactive_conditional_code_changes_member_hash(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithOneArg(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Failed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Parameter_syntax_distinguishes_overloads [< 1 ms]   Error Message:    System.NotImplementedException : The method or operation is not im...

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
