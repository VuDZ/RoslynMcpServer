# task-02: additional regressions RED, 2026-11-03

Added after the initial implementation/75-case GREEN, before their fixes: field/property initializer introduction in an existing test class (self-review), changed const value must remain ordinary (coordinator's bounded contract guard), and three guards preserving new-test entries alongside imports/ambiguous/malformed File fallbacks. An intermediate 80-case run had 78 passed and 2 initializer-introduction failures; the final pre-fix 81-case run below also reproduces the const misclassification.

These late tests are recorded separately because the initial stub RED did not include them. Actual RED: 81 executed, 78 passed, 3 expected root-flag assertion failures. All three File fallback guards already passed.

Roslyn MCP `run_specific_test`, project `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`, class `BaselineDocumentChangeDetectorTests`, Release, noBuild=false, includeFullOutput=true. Actual output:

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentChangeDetectorTests`
**Match:** Class name contains `BaselineDocumentChangeDetectorTests`

❌ 3 Tests Failed.

Total: **81** · Passed: **78** · Failed: **3**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Const_value_changes_are_ordinary_members_even_inside_a_test_class`
   **Error:**

```text
Assert.Equal() Failure: Collections differ
↓ (pos 0)
Expected: [BaselineDocumentChange { Kind = MemberModified, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = FieldDeclaration, ContainingTypePath = C`0, Name = Value, Arity = 0, Parameters = , ExplicitInterface =  }, IsTestMethod = False, IsOwnTestClassRoot = False }]
Actual:   [BaselineDocumentChange { Kind = MemberModified, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = FieldDeclaration, ContainingTypePath = C`0, Name = Value, Arity = 0, Parameters = , ExplicitInterface =  }, IsTestMethod = False, IsOwnTestClassRoot = True }]
↑ (pos 0)
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.AssertChanges(BaselineDocumentSnapshot before, BaselineDocumentSnapshot after, BaselineDocumentChange[] expected) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 353    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Const_value_changes_are_ordinary_members_even_inside_a_test_class() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 334    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Overloads_explicit_implementations_and_partial_members_match_exactly(beforeSource: "class C { int I.Run() => 1; int Run() => 0; }", afterSource: "class C { int I.Run() => 2; int Run() => 0; }", methodIndex: 0) [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Overloads_explicit_implementations_and_partial_members_match_exactly(beforeSour...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Const_value_changes_are_ordinary_members_even_inside_a_test_class`

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Introducing_an_own_initializer_marks_the_new_root`

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Introducing_an_own_initializer_marks_the_new_root`

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
