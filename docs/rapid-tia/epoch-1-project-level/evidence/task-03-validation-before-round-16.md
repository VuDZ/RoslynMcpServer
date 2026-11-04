# task-03 — validation до раунда 16

Дата2026-11-04, база1b617c4. Первоначальный freeze R14-001 из3 C# файлов: [SHA-256 manifest](task-03-validation-before-round-16-source-manifest.json).

- Обе Release solution build успешны, только3 прежних warning.
- Parent Windows focused537/537, все123 SourceStructure и strict inventory16 сохранены.
- Parent полный Windows main1746/1746, без падений.
- Первый Linux main1736/1738: два failures, skipped0. Последующие2 main не начинались, runner остановился после failure. AnalyzerLifecycle на этом freeze не запускался.
- PluginStartupTests.Help_of_a_loaded_plugin_tool_comes_from_the_start_report: IOException о достижении inotify user limit128 или per-process fd limit при создании config watcher.
- BaselineSessionIntegrationTests.Timestamp_guard_works_without_any_watcher_event: Assert.DoesNotContain found source inTouchedPaths. Возможна связь со startup gap при исчерпании watcher resources, причинность не установлена.
- [Полный Linux log](task-03-validation-before-round-16-linux.log), [все TRX counters](task-03-validation-before-round-16-linux.json).
- /proc после suite:0 видимых inotify instances, max_user_instances128, max_user_watches524288, fdlimit10240, uid1000. Лимиты и global test parallelism не менялись. Наблюдение после завершения не является измерением peak.
- [Ревью15 — MUST-FIX](task-03-review-round-15.md): ограничить лишние lifetime seeds чистых metadata/output roots, сохранить content watchers и linked source guards. Это доработка R14-001, не отдельный ID. Предварительные результаты не объявляются финальной приёмкой.

## Windows Release build

## Build succeeded

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Windows focused

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~WindowsOnlyTestInventoryTests|FullyQualifiedName~WorkspaceDiskWatcherStarterTests|FullyQualifiedName~SolutionManagerDiskWatcherCallbackTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **537** · Passed: **537** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.SolutionManagerDiskWatcherCallbackTests.Directory_rename_still_requests_full_refresh_when_the_logger_throws', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Ignored_trees_stay_ignored_even_for_loaded_project_candidates`

## Windows main

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1746** · Passed: **1746** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Analyzers.AnalyzerShadowPublicationPlannerTests.Confirmed_overlay_excludes_foreign_and_unconfirmed', 'RoslynMcpServer.Tests.Workspace.WorkspaceLoadGuidanceTests.IsRoslynMsBuildBuildHostFailure_detects_type_initializer_exception', 'RoslynMcpServer.Tests.Workspace.WorkspaceHealthReporterTests.HasRestoreAssets_finds_obj_sibling_of_output_bin', 'RoslynMcpServer.Tests.Build.MsBuildInstanceSelectorTests.FindLatestSdkDirectory_skips_roots_without_msbuild_and_picks_the_highest_folder_name', 'RoslynMcpServer.Tests.Workspace.WorkspaceLoadGuidanceTests.FormatRoslynMsBuildBuildHostFailureMessage_is_not_sdk_mismatch`
