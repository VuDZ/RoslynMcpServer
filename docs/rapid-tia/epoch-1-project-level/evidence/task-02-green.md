# task-02: focused GREEN, 2026-11-03

Final code is frozen for coordinator validation. Roslyn MCP `run_specific_test`, project `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`, Release, includeFullOutput=true. Detector class: noBuild=false; previous snapshot-builder class: noBuild=true against the same final binaries. Actual tool outputs:

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentChangeDetectorTests`
**Match:** Class name contains `BaselineDocumentChangeDetectorTests`

## Filtered tests passed

Total: **85** · Passed: **85** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_setup_and_teardown_methods_are_test_class_roots', 'RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Own_setup_and_teardown_methods_are_test_class_roots', 'RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Signature_changes_are_old_key_removal_and_new_key_addition', 'RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Signature_changes_are_old_key_removal_and_new_key_addition', 'RoslynMcpServer.Tests.Testing.BaselineDocumentChangeDetectorTests.Global_imports_and_assembly_module_attributes_expand_the_project`

## Existing task-01 regression catalog

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDocumentSnapshotBuilderTests`
**Match:** Class name contains `BaselineDocumentSnapshotBuilderTests`

## Filtered tests passed

Total: **51** · Passed: **51** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes`
