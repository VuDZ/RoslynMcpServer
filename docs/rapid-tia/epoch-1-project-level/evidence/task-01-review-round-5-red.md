# task-01: review round 5 RED, 2026-11-03

R5-001 regression tests were written before the production fix: reordered record init properties, struct auto-properties with different widths, and class D public auto-properties. The obsolete property-without-initializer invariance guard from R3 was replaced by these regressions; existing method/nested-type guards remain, and new indexer/accessor-event guards verify their order stays ignored. Keys are compared by identity and multiplicity, not list position.

This is the final pre-fix run: 51 executed, 48 passed, 3 failed at the expected unchanged-member-hash assertion. No production edit preceded RED. Historic R3 evidence is unchanged; R5 explicitly revises its property-order allowance.

Roslyn MCP `run_specific_test`, project `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`, class `BaselineDocumentSnapshotBuilderTests`, Release, noBuild=false, includeFullOutput=true. Actual tool output:

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentSnapshotBuilderTests`
**Match:** Class name contains `BaselineDocumentSnapshotBuilderTests`

❌ 3 Tests Failed.

Total: **51** · Passed: **48** · Failed: **3**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash`
   **Error:**

```text
Assert.NotEqual() Failure: Strings are equal
Expected: Not "09A4DA93D5C4A9D68888AA881A3FD6BBAF762F90F95188C6E6"···
Actual:       "09A4DA93D5C4A9D68888AA881A3FD6BBAF762F90F95188C6E6"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(String beforeSource, String afterSource, String memberName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 293    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(beforeSource: "struct S { int a; long b; }", afterSource: "struct S { long b; int a; }", memberName: "a") [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(beforeSource: "class C { static int A = 1; static int B { get; } "···, afterSource: "class C { static int B { get; } = A + 1; static in"···, memberName: "A") [4 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(beforeSource: "partia...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash`
   **Error:**

```text
Assert.NotEqual() Failure: Strings are equal
Expected: Not "E5B74A3E30DAABA80769D4D544201A6E49078EB81523632879"···
Actual:       "E5B74A3E30DAABA80769D4D544201A6E49078EB81523632879"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(String beforeSource, String afterSource, String memberName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 293    at InvokeStub_BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(beforeSource: "struct S { event System.Action A; event System.Act"···, afterSource: "struct S { event System.Action B; event System.Act"···, memberName: "A") [< 1 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash(beforeSource: "class C { static int A { get; } = 1; static int B "···, afterSource: "class C { static int B { get; } = A + 1; static in"···, memberName: "A") [< 1 ms]   Failed RoslynMcpServer.Tests.Testing....

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash`

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash`

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Meaningful_declaration_order_changes_member_hash`

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
