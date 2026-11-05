# task-05 — финальная validation

- Дата: **2026-11-05** (Europe/Moscow); база `9686c4c`.
- Координатор проверил замороженный финальный код на Windows через Roslyn MCP: Release solution build с `noIncremental=true`; тесты с `noBuild=true`, `noRestore=true` против этой сборки.
- Build PASS; focused **74/74**, все SourceStructure **123/123**, main **1874/1874**. Все прогоны ненулевые; failed=0, total=passed.
- Main filter Windows: `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`, по `.github/workflows/test-suite.yml`.
- Exact structural-analysis inventory сохранён; expected identities/kinds и SourceStructure-тесты не редактировались.
- [RED](task-05-red.md): 37/37 ожидаемых падений, 30 stub failures и 7 настоящих поведенческих регрессий watcher. [GREEN](task-05-green.md): composer 29/29, native watcher 8/8, прежний mapper 37/37. [Реализация](../task-05-implementation-report.md).
- SHA-256 трёх новых файлов tests/fixture совпадает до RED и после GREEN/freeze; после финальных прогонов код и тесты не менялись. Две прежние task-04 unit-проверки нового `.cs` до GREEN адаптированы к явному контракту реального composition-stale, без ослабления owners/test/reload/uncovered assertions.
- T04-C1/C2 проверены настоящим watcher и реальным reload: новый тест до/после reload, неизвестный non-C#, deleted/Compile Remove, directory in/out/restore, graph-file fallback. T04-C3 сохранён по требованию владельца.
- AnalyzerLifecycle не запускался: lifecycle/analyzer/shadow-copy/load/prepare/publication paths, host и test/build configuration не менялись. Изменены только отдельный composer, mapper, внутренние модели и обычные тесты.
- Linux/WSL и performance benchmark не запускались. Публичная активация, форматирование/план запуска и приёмка всей эпохи вне scope; версия сохранена.

## Windows Release solution build

## Build succeeded

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Windows focused

## Filtered test run

**Filter:** `FullyQualifiedName~TestImpact|FullyQualifiedName~ProjectImpactMapper`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **74** · Passed: **74** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped', 'RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Direct_tests_and_own_roots_are_unioned_with_required_project_expansion', 'RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped', 'RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Direct_tests_and_own_roots_are_unioned_with_required_project_expansion', 'RoslynMcpServer.Tests.Testing.ProjectImpactMapperTests.Restore_inputs_remain_a_blind_spot_even_when_mapped`

## Windows SourceStructure

## Filtered test run

**Filter:** `FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **123** · Passed: **123** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Unresolved_call_makes_reachability_undecidable', 'RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Crlf_and_lf_texts_produce_the_same_compilation_and_line_numbers', 'RoslynMcpServer.Tests.SourceStructure.WriteBoundaryChecksTests.Comment_and_string_mention_are_not_call_sites', 'RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Delegate_assignment_to_a_lambda_body_is_followed', 'RoslynMcpServer.Tests.SourceStructure.WriteBoundaryChecksTests.Second_call_site_fails`

## Windows main CI suite

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1874** · Passed: **1874** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiagnosticFormatterTests.IsBlockingLoadFailure_true_for_wrapped_msbuild_error', 'RoslynMcpServer.Tests.Analyzers.InProcessAnalyzerAssemblyLoaderPathTests.Path_keys_ignore_case_only_on_Windows', 'RoslynMcpServer.Tests.Hosting.ServerGcConfigurationTests.Server_runtimeconfig_selects_server_gc', 'RoslynMcpServer.Tests.Workspace.SolutionConfigurationCatalogTests.ListFromSlnx_cartesian_buildtype_and_platform', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiagnosticFormatterTests.IsBlockingLoadFailure_true_for_unwrapped_generic_failure`
