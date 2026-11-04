# task-03 — validation до раунда 18

Дата2026-11-04, база1b617c4, [восемь source hashes](task-03-validation-before-round-18-source-manifest.json).

- Обе Release solution build успешны, только3 прежних warning; parent focused553/553, SourceStructure123, strict inventory16 unchanged.
- [Ревью17](task-03-review-round-17.md) приняло код условно по последующей обязательной validation. Эта фаза не является приёмкой.
- Первый Linux main1745/1754, executed1754, skipped0: семь McpToolCatalogTests и два BuildProgressIntegrationTests дали IOException о достижении inotify user limit128. [Полный log](task-03-validation-before-round-18-linux.log), [TRX counters](task-03-validation-before-round-18-linux.json). Следующие два main не запускались, runner остановился.
- Windows main1761/1762: Native_recursive_delete_journals_the_document_without_a_gap(Boolean linked), Assert.False(RequiresFullScan) получилtrue. Specialized report не показал theory argument, поэтому он здесь не выдумывается. Требуются точные exception/coverage diagnostics; причина пока не объявляется.
- Lifecycle на этом freeze не запускался. Лимиты и global parallelism не менялись. Новая root-lifetime protection всё ещё порождает SDK analyzer-config chains; дальнейший seed scope должен соответствовать физическому C# baseline, не скрывая их resource multiplier.
- Неуспешные прогоны сохраняются; финальная validation должна целиком повторяться после исправлений.

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

Total: **553** · Passed: **553** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Ignored_trees_stay_ignored_even_for_loaded_project_candidates', 'RoslynMcpServer.Tests.Workspace.SolutionManagerDiskWatcherCallbackTests.Directory_rename_still_requests_full_refresh_when_the_logger_throws`

## Windows main failure

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

❌ 1 Tests Failed.

Total: **1762** · Passed: **1761** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap(Boolean linked) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 115    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap(Boolean linked) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 116 --- End of stack trace from previous location ---   Passed RoslynMcpServer.Tests.Analyzers.AnalyzerShadowGenerationPublisherTests.Pdb_copy_failure_does_not_fail_main_only_and_does_not_patch_published_generation [8 ms]   Passed RoslynMcpServer.Tests.Analyzers.AnalyzerShadowGenerationPublisherTests.Same_size_and_timestamp_but_different_bytes_get_new_identity [9 ms]   Passed RoslynMcpServer.Tests.Analyzers.AnalyzerShadowGenerationPublisherTests.Cleanup_refuses_path_outside_allowed_parent_even_when_owners_stopped [1 ms]   Passed RoslynMcpServer.Tests.Analyzers.AnalyzerShadowGenerationPublisherTests.Same_content_reuses_generation_without_overwrite [7 ms]   Passe...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap`

### Execution context
- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.402` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.402\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.402\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **SDK from MSBuild log paths:** `10.0.402`
- **WorkingDirectory:** `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests`
- **DOTNET_CLI_UI_LANGUAGE:** `en-US`
- **Configuration:** Release
- **Platform:** (SDK/solution default)
- **BuildArgs:** (none)
- **PreTestBuild:** skipped (`noBuild=true`)
- **TestOutput:** StdOut 100000 chars, StdErr 100000 chars (`includeFullOutput`)
