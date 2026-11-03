# task-02: acceptance review round 4 focused GREEN, 2026-11-03

After R4-001/R4-002 fixes, specialized Roslyn MCP Release runners passed the full detector/safety catalog 159/159 and the unchanged task-01 builder catalog 51/51. IsTestType is an immutable per-type-declaration fact independent of root eligibility; added-root checks use baseline/current type-path indexes. Exact nested class/struct/record/record-struct/interface kinds block only the outer type; enum/delegate and inner roles are preserved. Existing expectations were not changed.

Project RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj, includeFullOutput=true. Detector/safety filter used noBuild=false; builder class ran noBuild=true against the same binaries. Code/tests were frozen after these runs; coordinator owns final solution build, all SourceStructure, main suite and acceptance.

Actual focused output:

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentChangeDetectorTests|FullyQualifiedName~BaselineNarrowingSafetyTests|FullyQualifiedName~BaselineAddedRootSafetyTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **159** · Passed: **159** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing', 'RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary', 'RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_setup_and_teardown_methods_are_test_class_roots', 'RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary', 'RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary`

## Existing task-01 builder catalog

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentSnapshotBuilderTests`
**Match:** Class name contains `BaselineDocumentSnapshotBuilderTests`

## Filtered tests passed

Total: **51** · Passed: **51** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes`
