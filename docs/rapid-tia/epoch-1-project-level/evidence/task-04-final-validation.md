# task-04 — финальная validation

- Дата: **2026-11-05** (Europe/Moscow).
- База: `7896d28`; task-04 code frozen до сборки и всех прогонов ниже. Skill-правка уже сохранена отдельным коммитом; посторонний untracked release skill вне scope.
- Координатор: Windows, Release, Roslyn MCP. Полная solution build с `noIncremental=true`; тесты против её результата с `noBuild=true`, `noRestore=true`.
- Build успешен; focused mapper + существующий discovery **54/54**, все SourceStructure **123/123**, полный main CI suite **1837/1837**. Все прогоны ненулевые, failed=0; total=passed.
- Main filter: `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`, как Windows shard `.github/workflows/test-suite.yml`.
- Точный inventory structural-analysis limits сохранён, тесты и ожидаемые identities/kinds не менялись.
- [RED 37/37 ожидаемых падений](task-04-red.md), [GREEN 37/37](task-04-green.md), [отчёт реализации](../task-04-implementation-report.md).
- AnalyzerLifecycle дополнительно не запускался: изменены только отдельный mapper, DTO, доступность существующего matcher и новые обычные тесты. Lifecycle/analyzer/load/prepare/publication paths и test/build configuration не менялись.
- Linux/WSL и performance benchmark не запускались. Публичный инструмент и приёмка всей эпохи вне scope. Версия не повышалась.

## Windows Release solution build

## Build succeeded

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Windows focused (mapper + existing discovery)

## Filtered test run

**Filter:** `FullyQualifiedName~ProjectImpactMapper|FullyQualifiedName~TestDiscoveryHelperTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **54** · Passed: **54** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped', 'RoslynMcpServer.Tests.TestRunner.TestDiscoveryHelperTests.ListTests_nameContains_finds_bound_class_by_name', 'RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped', 'RoslynMcpServer.Tests.TestRunner.TestDiscoveryHelperTests.ListTests_reports_total_found_before_filters', 'RoslynMcpServer.Tests.TestRunner.TestDiscoveryHelperTests.ListTests_fullyQualifiedName_is_vstest_shape_without_global_or_parens`

## Windows SourceStructure

## Filtered test run

**Filter:** `FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **123** · Passed: **123** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Unresolved_call_makes_reachability_undecidable', 'RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Crlf_and_lf_texts_produce_the_same_compilation_and_line_numbers', 'RoslynMcpServer.Tests.SourceStructure.WriteBoundaryChecksTests.Comment_and_string_mention_are_not_call_sites', 'RoslynMcpServer.Tests.SourceStructure.WriteBoundaryChecksTests.Second_call_site_fails', 'RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Delegate_assignment_to_a_lambda_body_is_followed`

## Windows main CI suite

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1837** · Passed: **1837** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.BuildHostOptimizationsTests.ForWorkspace_adds_the_solution_path_for_a_solution_or_a_filter', 'RoslynMcpServer.Tests.Workspace.WorkspaceLoadGuidanceTests.IsRoslynMsBuildBuildHostFailure_detects_type_initializer_exception', 'RoslynMcpServer.Tests.Build.DotNetConfigurationArgumentsTests.FormatPlatformProperty_wraps_name', 'RoslynMcpServer.Tests.Workspace.WorkspaceHealthReporterTests.HasRestoreAssets_finds_obj_sibling_of_output_bin', 'RoslynMcpServer.Tests.Workspace.WorkspaceLoadGuidanceTests.FormatRoslynMsBuildBuildHostFailureMessage_is_not_sdk_mismatch`
