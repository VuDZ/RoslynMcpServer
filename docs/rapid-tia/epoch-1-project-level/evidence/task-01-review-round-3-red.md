# task-01: review round 3 RED, 2026-11-03

R3-001 regressions were written and executed before changing production code: enum values, static field initializers, struct layout, event fields with and without initializers, auto-property initializers, mixed field/property initializers, and two partial declarations in one document. Three guards compare all members by key after method, property-without-initializer, and nested-type reordering. Key multiplicities are checked for order-sensitive cases, including duplicate partial type keys.

An initial focused run without the partial-parts case executed 46 tests (39 passed, 7 expected failures). The final pre-fix run below includes all 11 new cases: 47 executed, 39 passed, 8 failed at the expected unchanged-member-hash assertion. Existing 36 cases and all three guards passed. No production edits preceded either RED run.

Roslyn MCP `run_specific_test`, project `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`, class `BaselineDocumentSnapshotBuilderTests`, Release, noBuild=false, includeFullOutput=true. Actual final pre-fix output follows (the runner displays the first five failures):

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentSnapshotBuilderTests`
**Match:** Class name contains `BaselineDocumentSnapshotBuilderTests`

❌ 8 Tests Failed.

Total: **47** · Passed: **39** · Failed: **8**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash`
   **Error:**

```text
Assert.NotEqual() Failure: Strings are equal
Expected: Not "B98C2172CA7924A25D73CFA45AA5F929742CD7E7072DBC1744"···
Actual:       "B98C2172CA7924A25D73CFA45AA5F929742CD7E7072DBC1744"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(String beforeSource, String afterSource, String memberName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 287    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash`
   **Error:**

```text
Assert.NotEqual() Failure: Strings are equal
Expected: Not "29EFBFC864A1E386716A9FFA5D46DE315EC9884B3545AC1BC0"···
Actual:       "29EFBFC864A1E386716A9FFA5D46DE315EC9884B3545AC1BC0"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(String beforeSource, String afterSource, String memberName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 287    at InvokeStub_BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Failed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(beforeSource: "partial class C { static int A = 1; } partial clas"···, afterSource: "partial class C { static int B = A + 1; } partial "···, memberName: "A") [< 1 ms]   Error Message:    Assert.NotEqual() Failure: Strings are equal Expected: Not "29EFBFC864A1E386716A9FFA5D46DE315EC9884B3545AC1BC0"··· Actual:       "29EFBFC864A1E386716A9FFA5D46DE315EC9884B3545AC1BC0"···   Stack Trace:      at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_cha...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash`
   **Error:**

```text
Assert.NotEqual() Failure: Strings are equal
Expected: Not "7D9239F43D247422F71B59EF04FDBE93D72382E8F69E91A171"···
Actual:       "7D9239F43D247422F71B59EF04FDBE93D72382E8F69E91A171"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(String beforeSource, String afterSource, String memberName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 287    at InvokeStub_BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Failed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(beforeSource: "struct S { event System.Action A; event System.Act"···, afterSource: "struct S { event System.Action B; event System.Act"···, memberName: "A") [< 1 ms]   Error Message:    Assert.NotEqual() Failure: Strings are equal Expected: Not "19047D67EADEA89FA4132D3AE2A7CF82B9EC94BFEB86EE6815"··· Actual:       "19047D67EADEA89FA4132D3AE2A7CF82B9EC94BFEB86EE6815"···   Stack Trace:      at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_cha...

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash`
   **Error:**

```text
Assert.NotEqual() Failure: Strings are equal
Expected: Not "DD5A97089765631D12C6AB9209AB9EBC7DD1A94D611EEE6C74"···
Actual:       "DD5A97089765631D12C6AB9209AB9EBC7DD1A94D611EEE6C74"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(String beforeSource, String afterSource, String memberName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 287    at InvokeStub_BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Parameter_syntax_distinguishes_overloads [1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Top_level_statements_have_entries [2 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Standalone_directives_and_disabled_declarations_are_covered [1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Directive_condition_changes_hash_even_when_branch_stays_inactive [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Nested_type...

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash`

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
