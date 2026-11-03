# task-03: focused GREEN, 2026-11-03

Specialized Roslyn MCP Release run of Baseline plus all SourceStructure: 351 executed, 351 passed, 0 failed. Includes 18 real MSBuild lifecycle integration cases, 210 previous baseline cases and 123 SourceStructure cases. Project RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj, noBuild=false, timeoutSeconds=600, includeFullOutput=true, includeBuildWarnings=true. Actual output below.

The incremental focused pre-build printed only the pre-existing CS8603 warning shown below. The coordinator's subsequent full solution build exposed 6 new xUnit2031 warnings. Before the final re-freeze, only equivalent Assert.Single(sequence.Where(predicate)) calls were replaced with Assert.Single(sequence,predicate), preserving predicates/results. No behavior/cases changed and no extra RED was claimed for equivalent assertion API cleanup. Coordinator owns final rebuild, full main suite and opted-in AnalyzerLifecycle on that frozen tree.

Exact new structural limits: beforeRead and beforeCommit, each ReachabilityLimitKind.TargetOutsideTheScope, reached via CaptureWork.BeforeRead/BeforeCommit in Services/Testing/Baseline/BaselineSession.cs. SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits inventories both names, kinds and source path in RoslynMcpServer.Tests/SourceStructure/SourceSetAnalysisTests.cs; previous 11 plus exact 2, no blanket count allowance.

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **351** · Passed: **351** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes', 'RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing', 'RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary`

### Build warnings

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
