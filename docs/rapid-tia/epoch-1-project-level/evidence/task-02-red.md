# task-02: RED, 2026-11-03

All 75 initial cases were written before production comparison or snapshot-role logic. Compilable contract: `Compare(BaselineDocumentSnapshot baseline, BaselineDocumentSnapshot current) => throw new NotImplementedException();`. Actual focused RED: 75 executed, 0 passed, 75 expected NotImplementedException failures.

An earlier fixture build attempt failed CS0051 because a public theory accepted an internal enum. That fixture signature was corrected before this run; the failed build is not counted as RED. Snapshot builder/model behavior was still unchanged during this RED.

Roslyn MCP `run_specific_test`, project `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`, class `BaselineDocumentChangeDetectorTests`, Release, noBuild=false, includeFullOutput=true. Actual output follows (the runner displays the first five failures):

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentChangeDetectorTests`
**Match:** Class name contains `BaselineDocumentChangeDetectorTests`

❌ 75 Tests Failed.

Total: **75** · Passed: **0** · Failed: **75**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_setup_and_teardown_methods_are_test_class_roots`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineDocumentChangeDetector.Compare(BaselineDocumentSnapshot baseline, BaselineDocumentSnapshot current) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineDocumentChangeDetector.cs:line 10    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.AssertChanges(BaselineDocumentSnapshot before, BaselineDocumentSnapshot after, BaselineDocumentChange[] expected) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 317    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_setup_and_teardown_methods_are_test_class_roots(String attribute) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 258    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)   Failed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_setup_and_teardown_methods_are_test_class_roots(attribute: "OneTimeTearDow...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Signature_changes_are_old_key_removal_and_new_key_addition`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineDocumentChangeDetector.Compare(BaselineDocumentSnapshot baseline, BaselineDocumentSnapshot current) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineDocumentChangeDetector.cs:line 10    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.AssertChanges(BaselineDocumentSnapshot before, BaselineDocumentSnapshot after, BaselineDocumentChange[] expected) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 317    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Signature_changes_are_old_key_removal_and_new_key_addition(String beforeMember, String afterMember) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 39    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr) [xUnit.net 00:00:00.43]       System.NotImplementedException : The method or operation is not implemented. [xUnit.net 00:00...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Signature_changes_are_old_key_removal_and_new_key_addition`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineDocumentChangeDetector.Compare(BaselineDocumentSnapshot baseline, BaselineDocumentSnapshot current) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineDocumentChangeDetector.cs:line 10    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.AssertChanges(BaselineDocumentSnapshot before, BaselineDocumentSnapshot after, BaselineDocumentChange[] expected) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 317    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Signature_changes_are_old_key_removal_and_new_key_addition(String beforeMember, String afterMember) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 39    at InvokeStub_BaselineDocumentChangeDetectorTests.Signature_changes_are_old_key_removal_and_new_key_addition(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Failed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeD...

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Global_imports_and_assembly_module_attributes_expand_the_project`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineDocumentChangeDetector.Compare(BaselineDocumentSnapshot baseline, BaselineDocumentSnapshot current) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineDocumentChangeDetector.cs:line 10    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.AssertChanges(BaselineDocumentSnapshot before, BaselineDocumentSnapshot after, BaselineDocumentChange[] expected) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 317    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Global_imports_and_assembly_module_attributes_expand_the_project(String beforeSource, String afterSource, Int32 reason) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 92    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)   Failed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Global_imports_and_assembly_m...

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_constructor_and_initializer_changes_are_test_class_roots`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.Baseline.BaselineDocumentChangeDetector.Compare(BaselineDocumentSnapshot baseline, BaselineDocumentSnapshot current) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\Baseline\BaselineDocumentChangeDetector.cs:line 10    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.AssertChanges(BaselineDocumentSnapshot before, BaselineDocumentSnapshot after, BaselineDocumentChange[] expected) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 317    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_constructor_and_initializer_changes_are_test_class_roots(String beforeMember, String afterMember, String name) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 242    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)

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
