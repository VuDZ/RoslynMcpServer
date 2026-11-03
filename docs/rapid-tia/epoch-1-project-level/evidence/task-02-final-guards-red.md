# task-02: final bounded guards RED, 2026-11-03

Late guards added after the 81-case GREEN, before their fixes: delegate type attributes must classify as TypeChanged (type-inventory self-review), and generic/static/ordinary Dispose helpers must not be treated as an explicit IDisposable implementation already present in the same type (coordinator's own-root safety review). The delegate-only intermediate run had 82 executed, 81 passed, 1 expected failure. The final pre-fix run below includes all four cases: 85 executed, 81 passed, 4 expected assertion failures.

Roslyn MCP `run_specific_test`, project `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`, class `BaselineDocumentChangeDetectorTests`, Release, noBuild=false, includeFullOutput=true. Actual output:

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentChangeDetectorTests`
**Match:** Class name contains `BaselineDocumentChangeDetectorTests`

❌ 4 Tests Failed.

Total: **85** · Passed: **81** · Failed: **4**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Type_surface_changes_expand_only_the_type`
   **Error:**

```text
Assert.Equal() Failure: Collections differ
↓ (pos 0)
Expected: [BaselineDocumentChange { Kind = TypeChanged, Reason = TypeSurface, Key = BaselineMemberKey { Kind = DelegateDeclaration, ContainingTypePath = , Name = D, Arity = 0, Parameters = 8200:1:(;8201:1:);, ExplicitInterface =  }, IsTestMethod = False, IsOwnTestClassRoot = False }]
Actual:   [BaselineDocumentChange { Kind = MemberModified, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = DelegateDeclaration, ContainingTypePath = , Name = D, Arity = 0, Parameters = 8200:1:(;8201:1:);, ExplicitInterface =  }, IsTestMethod = False, IsOwnTestClassRoot = False }]
↑ (pos 0)
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.AssertChanges(BaselineDocumentSnapshot before, BaselineDocumentSnapshot after, BaselineDocumentChange[] expected) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 368    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Type_surface_changes_expand_only_the_type(String beforeSource, String afterSource) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 127    at InvokeStub_BaselineDocumentChangeDetectorTests.Type_surface_changes_expand_only_the_type(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Type_surface_changes_expand_only_the_type(beforeSource: "class C : I {}", afterSource: "class C : I, J {}") [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Type_surface_changes_expand_only_the_type(beforeSource: "class C {}", afterSource: "[A] cl...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Disposal_helpers_do_not_replace_an_explicit_lifecycle_implementation`
   **Error:**

```text
Assert.Equal() Failure: Collections differ
↓ (pos 0)
Expected: [BaselineDocumentChange { Kind = MemberModified, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = MethodDeclaration, ContainingTypePath = C`0, Name = Dispose, Arity = 0, Parameters = 8200:1:(;8201:1:);, ExplicitInterface =  }, IsTestMethod = False, IsOwnTestClassRoot = False }]
Actual:   [BaselineDocumentChange { Kind = MemberModified, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = MethodDeclaration, ContainingTypePath = C`0, Name = Dispose, Arity = 0, Parameters = 8200:1:(;8201:1:);, ExplicitInterface =  }, IsTestMethod = False, IsOwnTestClassRoot = True }]
↑ (pos 0)
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.AssertChanges(BaselineDocumentSnapshot before, BaselineDocumentSnapshot after, BaselineDocumentChange[] expected) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 368    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Disposal_helpers_do_not_replace_an_explicit_lifecycle_implementation(String helper) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 349    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)   Failed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Disposal_helpers_do_not_replace_an_explicit_lifecycle_implementation(helper: "void Dispose() { Work(1); }") [< 1 ms]   Error Message:    Assert.Equal() Failure: Collections differ            ↓ (pos 0) Expected: [BaselineDocumentChange { Kind = MemberModified, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = MethodD...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Type_surface_changes_expand_only_the_type`

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Disposal_helpers_do_not_replace_an_explicit_lifecycle_implementation`

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Disposal_helpers_do_not_replace_an_explicit_lifecycle_implementation`

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
