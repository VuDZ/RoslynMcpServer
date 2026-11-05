# task-03 — R21-001 GREEN

Дата:2026-11-05, база1622e41, прежний исполнитель Codex; exact runtime model ID неизвестен. [RED](task-03-review-round-21-red.md), [before/after и1b measurements](task-03-review-round-21-measurements.md), [compact raw rows/counters](task-03-r21-measurements.json).

InputMap один раз строит immutable FrozenSet strict ancestor directories и external-input parent directories. Второй set логически сохраняет прежний All(EvaluationInput), включая empty occurrences, mixed roles, исключение paths под Regions и OS comparer; он также убирает старый _paths.Values scan из Consult. Индексы строятся по deduplicated dictionary (last entry wins). Query canonicalizes/trims directory ending, equality comparer совпадает с платформой. Callback empty Deleted не перечисляет Paths или Occurrences; стоимость проверки зависит от path length, не source path count. Existing region consultation и классификация сохранены.

Queue gate охватывает только foreach: ignored baseline suppression остаётся снаружи, поэтому unknown Deleted obj/bin/artifacts не попадают в journal. При наличии descendants сохраняется однократный outer scan, Present=true fast path, self exclusion, nested candidates, WPF filename authority, linked roots и root Error routing.

First related GREEN187/187; final full Release Roslyn MCP611/611, все123 SourceStructure и exact inventory16 unchanged. Node Windows after full20041 callbacks/noGap/journal0 в обеих rows; Linux after3/3PASS. Obj Windows overflow воспроизведён и на1b, оставлен measurement-only без расширения OtherEvents semantics. Никаких rate guarantees по единичным запускам не заявляется.

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceInputMapTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~WindowsOnlyTestInventoryTests|FullyQualifiedName~WorkspaceDiskWatcherStarterTests|FullyQualifiedName~SolutionManagerDiskWatcherCallbackTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **611** · Passed: **611** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.PlatformSupport.WindowsOnlyTestInventoryTests.Every_WPF_fact_is_in_the_windows_only_category', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure`

### Build warnings

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

CODE FROZEN сообщён сfinal611/611. C#3files: Services/Models/WorkspaceInputMap.cs; Services/Workspace/SolutionManager.cs; новый RoslynMcpServer.Tests/Testing/BaselineDeletedPathCostTests.cs. Версия1.5.18, status/spec/README/contracts не менялись, commit/push не выполнялись. IDE edits отсутствуют, ранееавторизованный local PowerShell fallback использован без MCP apply_patch/update_file_content. Known non-C# outside-project root exception изreview21 остаётся. Mandatory final validation/review/status принадлежат координатору; этот focused не объявляет acceptance.
