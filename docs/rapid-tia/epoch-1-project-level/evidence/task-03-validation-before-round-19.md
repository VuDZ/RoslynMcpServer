# task-03 — validation до раунда 19

Дата2026-11-04, база1b617c4. Freeze565, [8source SHA](task-03-validation-before-round-19-linux/source-manifest.json).

- Release solution build Windows/Linux успешны, три прежних warning.
- Parent Windows focused565/565, SourceStructure123 и exact inventory16 unchanged.
- Полный Windows main1774/1774 прошёл.
- Три последовательных полных Linux main1766/1766 каждый, без падений и пропусков. Inotify128 и parallelism не менялись. [Все TRX counters](task-03-validation-before-round-19-linux/summary.json), [build](task-03-validation-before-round-19-linux/build.log), [main1](task-03-validation-before-round-19-linux/main-1.log), [main2](task-03-validation-before-round-19-linux/main-2.log), [main3](task-03-validation-before-round-19-linux/main-3.log).
- Lifecycle на этом freeze не запускался: [review18](task-03-review-round-18.md) нашло новый exact-entry corner case внутри R14-001. Эти зелёные результаты не заменяют final validation после его исправления и не являются приёмкой.

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

Total: **565** · Passed: **565** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Ignored_trees_stay_ignored_even_for_loaded_project_candidates', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure', 'RoslynMcpServer.Tests.Workspace.SolutionManagerDiskWatcherCallbackTests.Directory_rename_still_requests_full_refresh_when_the_logger_throws', 'RoslynMcpServer.Tests.Workspace.SolutionManagerDiskWatcherCallbackTests.Watcher_error_still_requests_full_refresh_when_the_logger_throws`

## Windows main

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1774** · Passed: **1774** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Build.DotNetCliRunnerEnvironmentTests.Missing_processor_architecture_is_filled_only_on_Windows', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiagnosticFormatterTests.IsBlockingLoadFailure_true_for_wrapped_msbuild_error', 'RoslynMcpServer.Tests.TestRunner.TestAssemblyPathResolverTests.TryResolve_fails_when_binaries_path_is_not_a_directory', 'RoslynMcpServer.Tests.Workspace.WorkspaceLoadGuidanceTests.IsRoslynMsBuildBuildHostFailure_detects_type_initializer_exception', 'RoslynMcpServer.Tests.Config.MsBuildWorkspacePropertiesPassedArgsTests.MatchesPassedLoadArguments_different_path_misses_cache`
