# Task-09 — validation перед исправлением раунда 1

Дата: **2026-11-06**. Координатор: Codex (точный runtime model ID средой не предоставлен). Windows, SDK 10.0.402. База: `2c16c9e`, версия 1.6.1.

Это проверка первоначальной реализации, **не финальная приёмка**. Независимое ревью обнаружило неподдерживаемые параметры в подсказках ответа (R1-001); после исправления обязательные проверки повторяются на окончательном коде.

Все проверки выполнены координатором через Roslyn MCP последовательно: Release solution build `noIncremental=true`; тесты `noBuild=true`, `noRestore=true`. SourceStructure inventory не менялся: 16 известных limits. Main filter совпадает с Windows CI. Lifecycle включён через `--environment ROSLYN_MCP_ANALYZER_LIFECYCLE=1`; specialized test tools не имеют параметра окружения, поэтому использован `execute_dotnet_command`.

## Release build

## Build succeeded

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Focused

## Filtered test run

**Filter:** `FullyQualifiedName~EstimateAffectedTests|FullyQualifiedName~DotNetTestExecutorTests|FullyQualifiedName~TestTools|FullyQualifiedName~DotNetCliRunner|FullyQualifiedName~VstestOutputParser|FullyQualifiedName~McpToolCatalogTests|FullyQualifiedName~McpToolHelpTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **201** · Passed: **201** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Build.DotNetCliRunnerHangTests.FormatHangHints_mentions_timeout_and_zombie_dotnet', 'RoslynMcpServer.Tests.Build.DotNetCliRunnerEnvironmentTests.Missing_processor_architecture_is_filled_only_on_Windows', 'RoslynMcpServer.Tests.Build.DotNetCliRunnerLocaleTests.BuildRunMetadata_uses_the_current_platform_when_the_pin_is_missing', 'RoslynMcpServer.Tests.TestRunner.TestToolsRunTestByFilterTests.RunTestByFilter_rejects_empty_filter', 'RoslynMcpServer.Tests.Build.DotNetCliRunnerLocaleTests.BuildRunMetadata_surfaces_english_cli_locale`

## SourceStructure

## Filtered test run

**Filter:** `FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **123** · Passed: **123** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Unresolved_call_makes_reachability_undecidable', 'RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Crlf_and_lf_texts_produce_the_same_compilation_and_line_numbers', 'RoslynMcpServer.Tests.SourceStructure.WriteBoundaryChecksTests.Comment_and_string_mention_are_not_call_sites', 'RoslynMcpServer.Tests.SourceStructure.WriteBoundaryChecksTests.Second_call_site_fails', 'RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Delegate_assignment_to_a_lambda_body_is_followed`

## Main

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **2003** · Passed: **2003** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Navigation.SymbolDeclarationResolverTests.NormalizeFqn_strips_global_and_empty_parens', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.MsBuildWorkspacePropertiesTests.MatchesPassedLoadArguments_omitted_does_not_clear_loaded_values', 'RoslynMcpServer.Tests.Build.DotNetHostResolverTests.ResolveDotNetExecutable_on_64bit_windows_prefers_program_files_dotnet', 'RoslynMcpServer.Tests.Workspace.MsBuildWorkspacePropertiesTests.Create_omits_empty_properties`

## AnalyzerLifecycle

````text
## dotnet command


### StdOut
```text
Test run for E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\bin\Release\net10.0\RoslynMcpServer.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
Results File: E:\Devel\RoslynMcpServer-rapid-tia\TestResults\task-09-lifecycle\lifecycle.trx

Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 47 s - RoslynMcpServer.Tests.dll (net10.0)
```

### StdErr
(empty)
````

Linux/macOS не запускались. Task-10/task-11 вне scope. Коммит и accepted status на этом состоянии не создавались.
