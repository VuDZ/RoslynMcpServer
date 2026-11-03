# task-02: acceptance review round 2 RED, 2026-11-03

R2-001..004 cases were written before behavior changes. New BaselineNarrowingSafetyTests covers inheritance/abstract partial barriers, immutable simple base names from all base lists, all static-access combinations for fields/properties/event fields, static constructors, baseline role transitions, eligible instance/private-static guards, added roots/test types, and unchanged type-expansion rules.

The revised contract replaces previous expectations for static constructors, newly test-marked methods, and introduction of an initializer on an existing non-root member. To compile the new tests, only default-valued contract fields were added at this point: BlocksNarrowing and an empty immutable BaseTypeNames. Builder/detector behavior was still unchanged.

Actual pre-fix focused RED: 133 executed, 96 passed, 37 failed at expected role/barrier/base-name assertions. Specialized Roslyn MCP run_test_by_filter, project RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj, Release, noBuild=false, includeFullOutput=true. Actual output follows (runner displays first five failures):

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentChangeDetectorTests|FullyQualifiedName~BaselineNarrowingSafetyTests`
**Match:** Caller-supplied VSTest filter

❌ 37 Tests Failed.

Total: **133** · Passed: **96** · Failed: **37**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing`
   **Error:**

```text
Assert.Equal() Failure: Values differ
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing(String access, String kind, Boolean root) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNarrowingSafetyTests.cs:line 106    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_setup_and_teardown_methods_are_test_class_roots(attribute: "ClassCleanup") [73 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing(access: "", kind: "property", root: True) [5 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_setup_and_teardown_methods_are_test_class_roots(attribute: "OneTimeTearDown") [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_setup_and_teardown_methods_are_test_class_roots(attribute: "OneTimeSetUp") [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.Ba...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Abstract_and_locally_inherited_type_roots_remain_ordinary`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Abstract_and_locally_inherited_type_roots_remain_ordinary(String source, String name) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNarrowingSafetyTests.cs:line 21    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Global_imports_and_assembly_module_attributes_expand_the_project(beforeSource: "[assembly: InternalsVisibleTo(\"A\")]", afterSource: "[assembly: InternalsVisibleTo(\"B\")]", reason: 4) [2 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Global_imports_and_assembly_module_attributes_expand_the_project(beforeSource: "[module: A]", afterSource: "[module: B]", reason: 4) [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Global_imports_and_assembly_module_attributes_expand_the_project(beforeSource: "", afterSource: "[assembly: InternalsVisibleTo(\"Tests\")]", reason: 4) [< 1 ...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Abstract_and_locally_inherited_type_roots_remain_ordinary`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Abstract_and_locally_inherited_type_roots_remain_ordinary(String source, String name) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNarrowingSafetyTests.cs:line 21    at InvokeStub_BaselineNarrowingSafetyTests.Abstract_and_locally_inherited_type_roots_remain_ordinary(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Test_role_uses_current_syntax_and_custom_attribute_hierarchies_are_not_guessed`
   **Error:**

```text
Assert.Equal() Failure: Collections differ
↓ (pos 0)
Expected: [BaselineDocumentChange { Kind = MemberModified, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = MethodDeclaration, ContainingTypePath = C`0, Name = Run, Arity = 0, Parameters = 8200:1:(;8201:1:);, ExplicitInterface =  }, IsTestMethod = True, IsOwnTestClassRoot = False, BlocksNarrowing = True }]
Actual:   [BaselineDocumentChange { Kind = MemberModified, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = MethodDeclaration, ContainingTypePath = C`0, Name = Run, Arity = 0, Parameters = 8200:1:(;8201:1:);, ExplicitInterface =  }, IsTestMethod = True, IsOwnTestClassRoot = False, BlocksNarrowing = False }]
↑ (pos 0)
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.AssertChanges(BaselineDocumentSnapshot before, BaselineDocumentSnapshot after, BaselineDocumentChange[] expected) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 368    at RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Test_role_uses_current_syntax_and_custom_attribute_hierarchies_are_not_guessed(String beforeMember, String afterMember, Boolean isTest, Boolean blocks) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentChangeDetectorTests.cs:line 221    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)   Failed RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Abstract_and_locally_inherited_type_roots_remain_ordinary(source: "class Base { Base() { Work(1); } [Fact] void Test("···, name: ".ctor") [< 1 ms]   Error Message:    Assert.False() Failure Expected: False Actual:   True   Stack Trace:      at RoslynMcpServer....

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.All_base_lists_contribute_immutable_unqualified_names_without_generic_arity`
   **Error:**

```text
Assert.Equal() Failure: Collections differ
↓ (pos 0)
Expected: string[]                        ["Base", "IBase", "IRecord", "IStruct", "IThing", ···]
Actual:   OrderedIterator<string, string> []
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.All_base_lists_contribute_immutable_unqualified_names_without_generic_arity() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNarrowingSafetyTests.cs:line 64    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Trivia_reverted_text_and_unordered_method_moves_have_no_changes(beforeSource: "class C { void A() {} void B() {} }", afterSource: "class C { void B() {} void A() {} }") [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Modified_test_type_keeps_type_expansion_without_root_flags [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Trivia_reverted_text_and_unordered_method_moves_have_no_changes(beforeSource: "partial class C {} partial class C {}", afterSource: "partial class C { /* trivia */ } partial class C {"···) [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Trivia_reverted_t...

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
