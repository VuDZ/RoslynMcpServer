# task-01: review round 1 RED, 2026-11-03

R1-001: two declaration import-scope movement regressions and a using-only invariance guard. R1-002: comment-only and whitespace-only insertion into an empty inactive branch. These five tests were added before their production fixes. Roslyn MCP `run_specific_test`, Release, noBuild=false, project/class as in initial RED.

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentSnapshotBuilderTests`
**Match:** Class name contains `BaselineDocumentSnapshotBuilderTests`

❌ 4 Tests Failed.

Total: **36** · Passed: **32** · Failed: **4**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash`
   **Error:**

```text
Assert.NotEqual() Failure: Strings are equal
Expected: Not "00F1A3F4BDC0BCD9C4A60931D8719E9B90DB25704DA824D95F"···
Actual:       "00F1A3F4BDC0BCD9C4A60931D8719E9B90DB25704DA824D95F"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash(String beforeSource, String afterSource) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 233    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)   Failed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash(beforeSource: "namespace N { using X = System.Int32; class C { X "···, afterSource: "namespace N { using X = System.Int32; } namespace "···) [< 1 ms]   Error Message:    Assert.NotEqual() Failure: Strings are equal Expected: Not "00F1A3F4BDC0BCD9C4A60931D8719E9B90DB25704DA824D95F"··· Actual:       "00F1A3F4BDC0BCD9C4A60931D8719E9B90DB25704DA824D95F"···   Stack Trace:      at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash(String beforeSource, String after...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Trivia_inserted_into_empty_inactive_branch_does_not_change_hashes`
   **Error:**

```text
Assert.Equal() Failure: Collections differ
↓ (pos 1)
Expected: [BaselineMemberSnapshot { Key = BaselineMemberKey { Kind = ClassDeclaration, ContainingTypePath = , Name = C, Arity = 0, Parameters = , ExplicitInterface =  }, SyntaxHash = E7C5BB4249B19E27DF35568518A52B7CFE785C108D46CDF70E3D33327BBA154A, IsPartial = False }, BaselineMemberSnapshot { Key = BaselineMemberKey { Kind = MethodDeclaration, ContainingTypePath = C`0, Name = Run, Arity = 0, Parameters = 8200:1:(;8201:1:);, ExplicitInterface =  }, SyntaxHash = 961AF687AD569B6EEC727E293ECD8F0E3F3985FA20BC3A4DA2BD46AE5DB3A95B, IsPartial = False }]
Actual:   [BaselineMemberSnapshot { Key = BaselineMemberKey { Kind = ClassDeclaration, ContainingTypePath = , Name = C, Arity = 0, Parameters = , ExplicitInterface =  }, SyntaxHash = E7C5BB4249B19E27DF35568518A52B7CFE785C108D46CDF70E3D33327BBA154A, IsPartial = False }, BaselineMemberSnapshot { Key = BaselineMemberKey { Kind = MethodDeclaration, ContainingTypePath = C`0, Name = Run, Arity = 0, Parameters = 8200:1:(;8201:1:);, ExplicitInterface =  }, SyntaxHash = 3D7269C376D985ED99F63A607A4395CC8A4E1A8786E03FA322A343BB4FA16B61, IsPartial = False }]
↑ (pos 1)
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Trivia_inserted_into_empty_inactive_branch_does_not_change_hashes(String insertedTrivia) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDocumentSnapshotBuilderTests.cs:line 252    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)   Failed RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Trivia_inserted_into_empty_inactive_branch_does_not_change_hashes(insertedTrivia: "// comment\n") [< 1 ms]   Error Message:    Assert.Equal() Failure: Collections differ                                                                                                                                                                                                                                                                               ↓ (pos 1) Expected: [BaselineMemberSnapshot { Key = BaselineMemberKey { Kind = ClassDeclaration, ContainingTypePath = , Name = C, Arity = 0, Parameters = , ExplicitInterface =  }...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash`

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash`

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Trivia_inserted_into_empty_inactive_branch_does_not_change_hashes`

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
