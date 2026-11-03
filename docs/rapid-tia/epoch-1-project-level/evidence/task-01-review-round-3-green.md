# task-01: review round 3 focused GREEN, 2026-11-03

After R3-001/R3-002 fixes: declaration-order rank is included only for enum members, fields/event fields, and properties with initializers. A per-containing-type counter spans partial declarations in this document. Type headers do not include child bodies. Artificial EOF exclusion now has a why-comment. Keys and DTO contracts are unchanged.

Roslyn MCP `run_specific_test`, project `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`, class `BaselineDocumentSnapshotBuilderTests`, Release, noBuild=false, includeFullOutput=true. Actual tool output:

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentSnapshotBuilderTests`
**Match:** Class name contains `BaselineDocumentSnapshotBuilderTests`

## Filtered tests passed

Total: **47** · Passed: **47** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes`
