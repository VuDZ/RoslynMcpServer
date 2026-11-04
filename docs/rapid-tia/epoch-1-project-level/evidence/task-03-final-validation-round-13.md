# task-03 — финальная validation после раунда 13

- Дата: 2026-11-04 (Europe/Moscow).
- База: `6bedfcfe674b6b1970a08a14911ba9bc17c41cd9`. Окончательный CODE FROZEN после exact Windows-only inventory correction; четыре C# файла. После сборок и прогонов C# не менялся.
- [Независимое ревью13 — ACCEPT](task-03-review-round-13.md): R11-001 закрыт. Task-03: **13 раундов, 10 исправленных уникальных замечаний**. [Условное ревью12](task-03-review-round-12.md) и [предварительные inventory failures](task-03-validation-before-round-13.md) сохранены отдельно.
- Release solution build Windows и Linux успешны, обе --no-incremental. Три прежних warning: CS8603 NuGetFallbackAssemblyResolver:72, CS8601 HostSession:1179, xUnit1031 WpfTemporaryProjectCallbackTests:195.
- Координатор Windows/Roslyn MCP: focused **513/513**, включая весь SourceStructure123 и PlatformSupport inventory; полный main **1727/1727**; opted-in AnalyzerLifecycle **13/13**. Падений и пропусков нет.
- Ubuntu24.04/WSL, SDK10.0.114: три последовательных полных main suite, **1719/1719 каждый**, без падений и пропусков. [Все TRX counters](task-03-final-validation-round-13-linux/summary.json), [лог1](task-03-final-validation-round-13-linux/main-1.log), [лог2](task-03-final-validation-round-13-linux/main-2.log), [лог3](task-03-final-validation-round-13-linux/main-3.log), [полный build log](task-03-final-validation-round-13-linux/build.log).
- Exact SourceStructure inventory16 identities/kinds/paths не менялся; новых limits нет. Windows-only inventory дополнен ровно одной reviewed method identity с четырьмя theory rows, strict equality/categories не ослаблены. Разница Windows/Linux8 cases соответствует filter exclusions, а не TRX skips.
- [SHA-256 manifest](task-03-final-validation-round-13-linux/source-manifest.json) связывает все4 изменённых C# файла Windows/Linux. Хеши сверены перед каждой Linux suite, после неё и повторно в Windows после финальных проверок.
- Linux checkout базы6bedfcf с verified overlay: `/home/runner/roslyn-mcp-rapid-tia-r11-final-20261104`. Тесты против финальных Release binaries, --no-build --no-restore; диагностических production edits нет.
- Специализированный MCP runner не выражает WSL environment: использован разрешённый AGENTS.md integration runner. Windows lifecycle API не принимает env: использован MCP execute_dotnet_command с --environment ROSLYN_MCP_ANALYZER_LIFECYCLE=1.
- На этом раунде load/publication code не менялся; Windows AnalyzerLifecycle повторён как дополнительный regression check watcher filtering. Его отдельный TRX проверен: total=executed=passed=13, failed=notExecuted=error=timeout=aborted=0. Все три Linux TRX проверены теми же условиями для1719.
- Native obj/node_modules/.git directory/file guards проверяют sibling paths относительно build/All.slnx. Реальные git add/commit выполнены с активными watchers и receipt before assertions. Source directory/source file/mapped obj guards сохраняют корректное наблюдение. Directory.Move внутри .git не выдаётся за Git rebase, node_modules move — за npm install. Первоначальный cross-parent receipt timeout не засчитан behavioral RED; corrected same-parent rows дали2/0/2 после receipt до fix.
- Прежние Changed/Created под ignored ancestor остаются границей будущей task-04; production поведение других events и map priority не менялись. KVM-причина старого R5-002 не установлена, review11 считает это неблокирующим. Linux AnalyzerLifecycle и отдельный GC probe не запускались.
- Версия1.5.18 сохранена. Task-04+ не реализованы; эпоха целиком не принята, публичной активации нет.

## Windows Release solution build

## Build succeeded

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Windows focused

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~WindowsOnlyTestInventoryTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **513** · Passed: **513** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.PlatformSupport.WindowsOnlyTestInventoryTests.Every_WPF_fact_is_in_the_windows_only_category', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Callback_paused_before_admission_cannot_write_after_reset_and_session_replacement`

## Windows main CI suite

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1727** · Passed: **1727** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.TestRunner.DotNetTestArgumentsTests.Build_both_flags_and_filter_in_stable_order', 'RoslynMcpServer.Tests.WorkspaceStateCache.IsolatedRestoreBenchmarkTests.Attempt_requires_a_runner_directory', 'RoslynMcpServer.Tests.TestRunner.DotNetTestArgumentsTests.RemainingTimeout_subtracts_elapsed_and_clamps_to_zero', 'RoslynMcpServer.Tests.TestRunner.TestToolsRunTestByFilterTests.RunTestByFilter_rejects_empty_filter', 'RoslynMcpServer.Tests.TestRunner.TestDllEnsureTests.Missing_solution_does_not_produce_a_test_command`

## Linux команды и counters

```text
dotnet build RoslynMcpServer.sln -c Release --no-incremental

dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter "Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk&Category!=RequiresWindows" --logger "trx;LogFileName=main.trx" --results-directory <unique-run-directory>
```

Каждый запуск имеет отдельный TestResults directory. Все counters и фактические пути сохранены в summary.json; runner проверял exit code и не принимал нулевой/пропущенный прогон за pass.

```json
[
  {
    "run": 1,
    "exitCode": 0,
    "total": "1719",
    "executed": "1719",
    "passed": "1719",
    "failed": "0",
    "error": "0",
    "timeout": "0",
    "aborted": "0",
    "inconclusive": "0",
    "passedButRunAborted": "0",
    "notRunnable": "0",
    "notExecuted": "0",
    "disconnected": "0",
    "warning": "0",
    "completed": "0",
    "inProgress": "0",
    "pending": "0"
  },
  {
    "run": 2,
    "exitCode": 0,
    "total": "1719",
    "executed": "1719",
    "passed": "1719",
    "failed": "0",
    "error": "0",
    "timeout": "0",
    "aborted": "0",
    "inconclusive": "0",
    "passedButRunAborted": "0",
    "notRunnable": "0",
    "notExecuted": "0",
    "disconnected": "0",
    "warning": "0",
    "completed": "0",
    "inProgress": "0",
    "pending": "0"
  },
  {
    "run": 3,
    "exitCode": 0,
    "total": "1719",
    "executed": "1719",
    "passed": "1719",
    "failed": "0",
    "error": "0",
    "timeout": "0",
    "aborted": "0",
    "inconclusive": "0",
    "passedButRunAborted": "0",
    "notRunnable": "0",
    "notExecuted": "0",
    "disconnected": "0",
    "warning": "0",
    "completed": "0",
    "inProgress": "0",
    "pending": "0"
  }
]
```

## Windows AnalyzerLifecycle opt-in

```text
dotnet test "E:/Devel/RoslynMcpServer-rapid-tia/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj" -c Release --no-build --no-restore --filter "Category=AnalyzerLifecycle" --environment "ROSLYN_MCP_ANALYZER_LIFECYCLE=1" --logger "trx;LogFileName=task-03-round-13-lifecycle.trx" --results-directory "E:/Devel/RoslynMcpServer-rapid-tia/TestResults/task-03-round-13-lifecycle-1793752884470"
```

## dotnet command


### StdOut
```text
Test run for E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\bin\Release\net10.0\RoslynMcpServer.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
Results File: E:\Devel\RoslynMcpServer-rapid-tia\TestResults\task-03-round-13-lifecycle-1793752884470\task-03-round-13-lifecycle.trx

Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 50 s - RoslynMcpServer.Tests.dll (net10.0)
```

### StdErr
(empty)

Отдельно проверенные TRX counters:

```json
{"timeout":"0","error":"0","passed":"13","notExecuted":"0","failed":"0","total":"13","executed":"13","aborted":"0"}
```

## SHA-256 окончательных C# файлов

```json
{
  "RoslynMcpServer.Tests/Testing/BaselineNestedSolutionRenameTests.cs": "bdc2a4a4518e7811481e8d0c9fd6ca81cbfef39f8e8af1d97eefd376f468dae9",
  "RoslynMcpServer.Tests/Workspace/WorkspaceDiskPathFilterTests.cs": "07de28787b1610517a432685473ead71330c6315a65c4e95cc399cab171af095",
  "Services/Workspace/WorkspaceDiskPathFilter.cs": "5884dc6c16517e5508bdd98362467a95f3af270a7dfc36e20a66025611a57f5f",
  "RoslynMcpServer.Tests/PlatformSupport/WindowsOnlyTestInventoryTests.cs": "131f20720c97d12ec60888909a6e1ecea149a8c824d176bd5380da7aceda22d6"
}
```

Сырые TRX остаются в ignored TestResults; полный Linux stdout, counters, manifest и итог Windows MCP сохранены в evidence.
