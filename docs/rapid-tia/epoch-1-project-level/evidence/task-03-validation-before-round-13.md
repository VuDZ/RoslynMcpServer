# task-03 — validation до раунда 13

Дата: 2026-11-04. База `6bedfcf`, три C# файла первоначального R11-001 freeze.

Обе Release solution build успешны, Windows focused511/511, включая все123 SourceStructure. [Ревью12](task-03-review-round-12.md) приняло поведение, но обязательные main suites выявили пропуск точной Windows-only inventory записи. Windows1726/1727 и Linux1718/1719: единственное падение WindowsOnlyTestInventoryTests.Windows_only_category_matches_the_reviewed_list. Новый метод WorkspaceDiskPathFilterTests.Workspace_filter_respects_windows_roots_without_filesystem_access отсутствовал в expected list. Нулевых тестов/пропусков нет. Последующие два Linux прогона не начинались: runner остановился после первого failure. Эти результаты не являются финальной зелёной validation.

[Полный Linux failure log](task-03-validation-before-round-13-linux.log), [TRX counters](task-03-validation-before-round-13-linux.json). Исправление передано прежнему исполнителю: добавить конкретную reviewed identity с причиной, сохранив строгую equality и категории. Production после этого не менялся.

## Windows main MCP output

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

❌ 1 Tests Failed.

Total: **1727** · Passed: **1726** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.PlatformSupport.WindowsOnlyTestInventoryTests.Windows_only_category_matches_the_reviewed_list`
   **Error:**

```text
Assert.Equal() Failure: Collections differ
↓ (pos 2)
Expected: ["DotNetHostResolverTests.ResolveDotNetExecutable_on"···, "HarnessSessionIdTests.Windows_reads_this_process_i"···, "WpfTemporaryProjectWatchingTests.Real_WPF_build_pr"···, "WpfTemporaryProjectWatchingTests.Real_project_edit"···]
Actual:   ["DotNetHostResolverTests.ResolveDotNetExecutable_on"···, "HarnessSessionIdTests.Windows_reads_this_process_i"···, "WorkspaceDiskPathFilterTests.Workspace_filter_resp"···, "WpfTemporaryProjectWatchingTests.Real_WPF_build_pr"···, "WpfTemporaryProjectWatchingTests.Real_project_edit"···]
↑ (pos 2)
```
   **Stack:** at RoslynMcpServer.Tests.PlatformSupport.WindowsOnlyTestInventoryTests.Windows_only_category_matches_the_reviewed_list() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\PlatformSupport\WindowsOnlyTestInventoryTests.cs:line 16    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)   Passed RoslynMcpServer.Tests.Config.RoslynMcpJsoncParserTests.StripComments_allows_line_and_block_comments [< 1 ms]   Passed RoslynMcpServer.Tests.Config.RoslynMcpJsoncParserTests.StripComments_does_not_strip_slashes_inside_strings [< 1 ms]   Passed RoslynMcpServer.Tests.Analyzers.AnalyzerProvenanceReplayTests.Newer_empty_buildhost_log_does_not_make_the_capture_incomplete [10 ms]   Passed RoslynMcpServer.Tests.Analyzers.AnalyzerProvenanceReplayTests.Recoverable_errors_discard_file_evidence_and_count_failed_files_not_errors [5 ms]   Passed RoslynMcpServer.Tests.Analyzers.AnalyzerProvenanceReplayTests.Missing_corrupt_and_cancelled_captures_keep_their_failure_behavior [1 ms]   Passed RoslynMcpServer.Tests.Navigation.SymbolDeclarationResolverTests.Simple...

2. **TestName:** `RoslynMcpServer.Tests.PlatformSupport.WindowsOnlyTestInventoryTests.Windows_only_category_matches_the_reviewed_list`

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
