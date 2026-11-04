# task-03 — validation до раунда 20

Дата2026-11-04, база1b617c4, [8source hashes](task-03-validation-before-round-20-linux/source-manifest.json).

- Release solution build Windows/Linux успешны, три прежних warning.
- Parent Windows focused570/570, SourceStructure123, exact inventory16 unchanged.
- Полный Windows main1779/1779 прошёл.
- Три последовательных полных Linux main1771/1771 каждый, без падений и пропусков; штатные inotify128 и parallelism сохранены.
- [TRX counters](task-03-validation-before-round-20-linux/summary.json), [build](task-03-validation-before-round-20-linux/build.log), [main1](task-03-validation-before-round-20-linux/main-1.log), [main2](task-03-validation-before-round-20-linux/main-2.log), [main3](task-03-validation-before-round-20-linux/main-3.log).
- Lifecycle не запускался на этом freeze из-за [MUST-FIX review19](task-03-review-round-19.md). Зелёные main не заменяют исправления prefilter loss и не являются окончательной приёмкой.

## Windows build

## Build succeeded

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Windows focused

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~WindowsOnlyTestInventoryTests|FullyQualifiedName~WorkspaceDiskWatcherStarterTests|FullyQualifiedName~SolutionManagerDiskWatcherCallbackTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **570** · Passed: **570** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix`

## Windows main

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1779** · Passed: **1779** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Config.MsBuildWorkspacePropertiesPassedArgsTests.MatchesPassedLoadArguments_different_path_misses_cache', 'RoslynMcpServer.Tests.Build.DotNetCliRunnerEnvironmentTests.Missing_processor_architecture_is_filled_only_on_Windows', 'RoslynMcpServer.Tests.Build.DotNetCliRunnerHangTests.FormatHangHints_mentions_timeout_and_zombie_dotnet', 'RoslynMcpServer.Tests.Logging.TruncatedProcessLogTests.StripTrailingMsBuildOutcome_keeps_payload_that_is_only_the_footer', 'RoslynMcpServer.Tests.Workspace.SolutionProjectTargetResolverTests.TryResolve_rejects_csproj_extension`
