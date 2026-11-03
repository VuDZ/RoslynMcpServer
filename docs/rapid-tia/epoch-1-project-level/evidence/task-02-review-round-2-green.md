# task-02: acceptance review round 2 focused GREEN, 2026-11-03

After R2-001..004 behavior fixes: 133 focused detector/narrowing cases passed; unchanged task-01 builder catalog passed 51/51. Specialized Roslyn MCP runner, Release, includeFullOutput=true, project RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj. Combined filter used noBuild=false; builder class ran noBuild=true against the same binaries.

After these runs, only why/XML comments and two equivalent xUnit assertion APIs were cleaned up (Assert.False/True over Contains -> Assert.DoesNotContain/Contains; exact set inventory unchanged), at coordinator request to remove xUnit2017 warnings. No behavior changes followed. Final solution build, SourceStructure and main-suite validation of the frozen tree belong to the coordinator.

Actual focused output:

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentChangeDetectorTests|FullyQualifiedName~BaselineNarrowingSafetyTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **133** · Passed: **133** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_setup_and_teardown_methods_are_test_class_roots', 'RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing', 'RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_setup_and_teardown_methods_are_test_class_roots', 'RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing', 'RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing`

## Existing task-01 builder catalog

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentSnapshotBuilderTests`
**Match:** Class name contains `BaselineDocumentSnapshotBuilderTests`

## Filtered tests passed

Total: **51** · Passed: **51** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes`
