# task-02: acceptance review round 4 RED, 2026-11-03

R4-001/R4-002 tests were written before builder/detector behavior changes. New BaselineAddedRootSafetyTests contains 26 cases: newly introduced roots in old non-test types, baseline TestFixture/TestClass proof without a type root, existing partial proof, new partial/non-partial test classes, the review's nested-access table, all five exact nested syntax kinds, enum/delegate guards, cross-part nested barrier, added outer type barrier, and independent inner constructor/setup roots. Existing 133 expectations were unchanged.

Only a default-valued IsTestType contract property was introduced to compile proof assertions; it remained false and no behavior populated it yet. Actual pre-fix RED: 159 executed, 141 passed, 18 failed at expected proof/root/barrier assertions. Existing cases and positive guards passed.

Roslyn MCP run_test_by_filter, project RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj, Release, noBuild=false, includeFullOutput=true. Actual output (runner displays first five failures):

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentChangeDetectorTests|FullyQualifiedName~BaselineNarrowingSafetyTests|FullyQualifiedName~BaselineAddedRootSafetyTests`
**Match:** Caller-supplied VSTest filter

❌ 18 Tests Failed.

Total: **159** · Passed: **141** · Failed: **18**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary(String source, String name) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineAddedRootSafetyTests.cs:line 75    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)   Failed RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary(source: "class O { static readonly int V = Work(1); [Fact] "···, name: "V") [< 1 ms]   Error Message:    Assert.False() Failure Expected: False Actual:   True   Stack Trace:      at RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary(String source, String name) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineAddedRootSafetyTests.cs:line 75    at InvokeStub_BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinar...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary(String source, String name) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineAddedRootSafetyTests.cs:line 75    at InvokeStub_BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_setup_and_teardown_methods_are_test_class_roots(attribute: "ClassCleanup") [73 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing(access: "internal", kind: "field", root: False) [73 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Inner_test_type_keeps_its_own_modified_constructor_and_setup_roots(declaration: "[SetUp] void Prepare() { Work(1); }", name: "Prepare") [4 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_setup_a...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.New_partial_part_does_not_narrow_type_or_constructor_but_keeps_its_new_test`
   **Error:**

```text
Assert.Equal() Failure: Collections differ
↓ (pos 0)
Expected: [BaselineDocumentChange { Kind = MemberAdded, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = ClassDeclaration, ContainingTypePath = , Name = C, Arity = 0, Parameters = , ExplicitInterface =  }, IsTestMethod = False, IsOwnTestClassRoot = False, BlocksNarrowing = False }, BaselineDocumentChange { Kind = MemberAdded, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = ConstructorDeclaration, ContainingTypePath = C`0, Name = .ctor, Arity = 0, Parameters = 8200:1:(;8201:1:);, ExplicitInterface =  }, IsTestMethod = False, IsOwnTestClassRoot = False, BlocksNarrowing = False }, BaselineDocumentChange { Kind = MemberAdded, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = MethodDeclaration, ContainingTypePath = C`0, Name = T, Arity = 0, Parameters = 8200:1:(;8201:1:);, ExplicitInterface =  }, IsTestMethod = True, IsOwnTestClassRoot = False, BlocksNarrowing = False }]
Actual:   [BaselineDocumentChange { Kind = MemberAdded, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = ClassDeclaration, ContainingTypePath = , Name = C, Arity = 0, Parameters = , ExplicitInterface =  }, IsTestMethod = False, IsOwnTestClassRoot = True, BlocksNarrowing = False }, BaselineDocumentChange { Kind = MemberAdded, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = ConstructorDeclaration, ContainingTypePath = C`0, Name = .ctor, Arity = 0, Parameters = 8200:1:(;8201:1:);, ExplicitInterface =  }, IsTestMethod = False, IsOwnTestClassRoot = True, BlocksNarrowing = False }, BaselineDocumentChange { Kind = MemberAdded, Reason = MemberSyntax, Key = BaselineMemberKey { Kind = MethodDeclaration, ContainingTypePath = C`0, Name = T, Arity = 0, Parameters = 8200:1:(;8201:1:);, ExplicitInterface =  }, IsTestMethod = True, IsOwnTestClassRoot = False, BlocksNarrowing = False }]
↑ (pos 0)
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.AssertChanges(BaselineDocumentSnapshot before, BaselineDocumentSnapshot after, BaselineDocumentChange[] expected) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineAddedRootSafetyTests.cs:line 173    at RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.New_partial_part_does_not_narrow_type_or_constructor_but_keeps_its_new_test() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineAddedRootSafetyTests.cs:line 52    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)   Passed RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing(access: "", kind: "event", root: True) [1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing(access: "internal", kind: "property", root: False) [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Signature_changes_are_old_key_removal_and...

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Exact_nested_type_kinds_keep_outer_tests_visible_but_block_narrowing`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Exact_nested_type_kinds_keep_outer_tests_visible_but_block_narrowing(String nested) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineAddedRootSafetyTests.cs:line 92    at InvokeStub_BaselineAddedRootSafetyTests.Exact_nested_type_kinds_keep_outer_tests_visible_but_block_narrowing(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithOneArg(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Global_imports_and_assembly_module_attributes_expand_the_project(beforeSource: "[module: A]", afterSource: "[module: B]", reason: 4) [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing(access: "private protected", kind: "property", root: False) [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Global_imports_and_assembly_module_attributes_expand_the_project(beforeSource: "", afterSource: "[assembly: InternalsVisibleTo(\"Tests\")]", reason: 4) [< 1 ms]   Passed ...

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Exact_nested_type_kinds_keep_outer_tests_visible_but_block_narrowing`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Exact_nested_type_kinds_keep_outer_tests_visible_but_block_narrowing(String nested) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineAddedRootSafetyTests.cs:line 92    at InvokeStub_BaselineAddedRootSafetyTests.Exact_nested_type_kinds_keep_outer_tests_visible_but_block_narrowing(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithOneArg(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_constructor_and_initializer_changes_are_test_class_roots(beforeMember: "int value = 1;", afterMember: "int value = 2;", name: "value") [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Abstract_and_locally_inherited_type_roots_remain_ordinary(source: "[TestClass] abstract class Base { [TestInitialize]"···, name: "Prepare") [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_constructor_and_initializer_changes_are_test_class_roots(beforeMember: "C() { Work(1); }", afterMember: "C() { Work(2); }", name: ".cto...

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
