# task-03 — финальная validation после раунда 20

- Дата:2026-11-04 (Europe/Moscow).
- База `1b617c4`, окончательный CODE FROZEN578 после всех исправлений R14-001 и reviews18/19. Все8 C# файлов неизменны после сборок/прогонов.
- [Независимое review20 — ACCEPT](task-03-review-round-20.md). Task-03:20 раундов,11 исправленных unique findings; последние доработки относятся к одному R14-001.
- Release solution build Windows/Linux успешны, --no-incremental. Три прежних warning: CS8603 NuGetFallbackAssemblyResolver:72; CS8601 HostSession:1179; xUnit1031 WpfTemporaryProjectCallbackTests:195.
- Parent Windows Roslyn MCP: focused **578/578**, все123 SourceStructure; полный main **1787/1787**; opted-in AnalyzerLifecycle **13/13**. Падений и пропусков нет.
- Ubuntu24.04/WSL, SDK10.0.114: **три последовательных полных main1779/1779 каждый**, opted-in AnalyzerLifecycle **13/13**. Все TRX counters отдельно проверены: total=executed=passed>0, failed/notExecuted/error/timeout/aborted=0.
- [Linux main counters](task-03-final-validation-round-20-linux/summary.json), [build](task-03-final-validation-round-20-linux/build.log), [main1](task-03-final-validation-round-20-linux/main-1.log), [main2](task-03-final-validation-round-20-linux/main-2.log), [main3](task-03-final-validation-round-20-linux/main-3.log), [lifecycle counters](task-03-final-validation-round-20-linux/lifecycle-summary.json), [полный lifecycle log](task-03-final-validation-round-20-linux/lifecycle.log).
- [SHA-256 manifest8C#](task-03-final-validation-round-20-linux/source-manifest.json) сверялся перед каждым Linux main и после всего validation, затем повторно в Windows. Linux checkout базы1b617c4: `/home/runner/roslyn-mcp-rapid-tia-r14-final-20261104`, verified final overlay, без временных диагностических production edits. Permanent diagnostic tests входят в manifest и binaries.
- Все tests Release --no-build --no-restore против финальных binaries. Main filters: Windows Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk; Linux дополнительно &Category!=RequiresWindows. Разница8 cases — workflow exclusions, не TRX skips. SourceStructure exact inventory16 identities/kinds/paths не менялся, новых limits нет.
- Специализированный MCP не выражает WSL integration environment; использован разрешённый AGENTS.md runner. Windows lifecycle API не принимает env: MCP execute_dotnet_command с --environment ROSLYN_MCP_ANALYZER_LIFECYCLE=1. Linux subprocess environment тоже явно opted-in. Lifecycle повторён на обеих OS, поскольку меняется watcher startup/load path.
- Inotify max_user_instances128, max_user_watches524288, fdlimit10240 и штатная16CPU параллельность Linux не менялись; main runs выполнялись без diagnostic profiler. [Windows App counters](task-03-r18-app-counts.json):13 planned descriptors,12 actual watchers,0 extra против25/20 промежуточных вариантов. Это локальный measurement, не универсальная верхняя граница.
- Принятый runtime scope: journal mapped descendants при исчезновении каталога; C# baseline/membership lifetime seeds и derived ancestors, консервативные Unknown/empty/PotentialMembership. Non-C# content subscriptions сохранены; independent lifetime coverage всех non-C# inputs не заявляется. Map authority и прежние rename/rescan guards сохранены.
- Optional exact entry Present=false может быть директорией: props/targets native/deterministic/nested proofs в [review18 RED](task-03-review-round-18-red.md). Only Present=true file shortcut, strict self exclusion и synthetic non-expansion сохраняют однократность.
- [Review19 RED](task-03-review-round-19-red.md) закрепляет WPF-named root/child/combo, Known Compile unusual extension, direct mapped Deleted и actual temporary-file/mapless guards. Mapped Deleted stage выполняется до filename suppression; legacy semantic/graph suppression сохранена.
- Missing-root nonoverflow Error с доказанным Windows Win32Exception5/Directory.Exists=false маршрутизируется как Deleted без ложного gap. Overflow и genuine existing-root errors остаются gaps; ранее поднятый gap не очищается, session closure сохранён. Native/deterministic proofs в [R14 RED](task-03-review-round-14-red.md).
- Прежние failures не скрыты: [до16](task-03-validation-before-round-16.md), [до17](task-03-validation-before-round-17.md), [до18](task-03-validation-before-round-18.md). [Зелёные main до19](task-03-validation-before-round-19.md) и [до20](task-03-validation-before-round-20.md) не подменяют финальные.
- Причина двух прежних timestamp failures не установлена; усиленные preconditions/logging не выдаются за причинное исправление. Output-notice test race устранена ожиданием того же exact predicate. Native Windows cross-parent Deleted→Created записан; output-parent Directory.Move AccessDenied доevent отделён от Linux native proof и Windows registration/deterministic branch. mv/git mv не запускались.
- Отдельный GC probe не запускался. Старый KVM R5-002 остаётся с неизвестной причиной, не объявляется объяснённым новым Windows trace.
- Версия1.5.18 сохранена. Task-04+ не реализованы, эпоха целиком и публичная активация не принимаются.

## Windows Release solution build

## Build succeeded

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Windows focused

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~WindowsOnlyTestInventoryTests|FullyQualifiedName~WorkspaceDiskWatcherStarterTests|FullyQualifiedName~SolutionManagerDiskWatcherCallbackTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **578** · Passed: **578** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Temporary_project_name_is_separate_from_graph_file_classification', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.SolutionManagerDiskWatcherCallbackTests.Directory_rename_still_requests_full_refresh_when_the_logger_throws`

## Windows main CI suite

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1787** · Passed: **1787** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Navigation.SymbolDeclarationResolverTests.NormalizeFqn_strips_global_and_empty_parens', 'RoslynMcpServer.Tests.Build.DotNetCliRunnerEnvironmentTests.Missing_processor_architecture_is_filled_only_on_Windows', 'RoslynMcpServer.Tests.Workspace.SolutionConfigurationCatalogTests.ListFromSlnx_cartesian_buildtype_and_platform', 'RoslynMcpServer.Tests.Navigation.SourcePositionHelperTests.TryGetAbsolutePosition_line_past_eof_returns_readable_error', 'RoslynMcpServer.Tests.Workspace.SourceTextEncodingTests.ForDiskPath_returns_bom_free_utf8_for_absent_file`

## Linux команды и main counters

```text
dotnet build RoslynMcpServer.sln -c Release --no-incremental
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter "Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk&Category!=RequiresWindows" --logger "trx;LogFileName=main.trx" --results-directory <unique-run-directory>
```

```json
[
  {
    "run": 1,
    "exitCode": 0,
    "total": "1779",
    "executed": "1779",
    "passed": "1779",
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
    "total": "1779",
    "executed": "1779",
    "passed": "1779",
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
    "total": "1779",
    "executed": "1779",
    "passed": "1779",
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
dotnet test "E:/Devel/RoslynMcpServer-rapid-tia/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj" -c Release --no-build --no-restore --filter "Category=AnalyzerLifecycle" --environment "ROSLYN_MCP_ANALYZER_LIFECYCLE=1" --logger "trx;LogFileName=task-03-round-20-lifecycle.trx" --results-directory "E:/Devel/RoslynMcpServer-rapid-tia/TestResults/task-03-round-20-lifecycle-1793801841817"
```

## dotnet command


### StdOut
```text
Test run for E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\bin\Release\net10.0\RoslynMcpServer.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
Results File: E:\Devel\RoslynMcpServer-rapid-tia\TestResults\task-03-round-20-lifecycle-1793801841817\task-03-round-20-lifecycle.trx

Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 1 m 11 s - RoslynMcpServer.Tests.dll (net10.0)
```

### StdErr
(empty)

Независимо проверенные TRX counters:

```json
{"notExecuted":"0","failed":"0","executed":"13","timeout":"0","aborted":"0","passed":"13","total":"13","error":"0"}
```

## Linux AnalyzerLifecycle opt-in

```text
ROSLYN_MCP_ANALYZER_LIFECYCLE=1 dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter "Category=AnalyzerLifecycle" --logger "trx;LogFileName=lifecycle.trx" --results-directory <unique-run-directory>
```

```json
{
  "exitCode": 0,
  "filter": "Category=AnalyzerLifecycle",
  "optIn": "ROSLYN_MCP_ANALYZER_LIFECYCLE=1",
  "counters": {
    "total": "13",
    "executed": "13",
    "passed": "13",
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
  "log": "/home/runner/roslyn-mcp-rapid-tia-r14-final-20261104/artifacts/task-03-r20-linux-lifecycle-1793801975037.log",
  "trx": "/home/runner/roslyn-mcp-rapid-tia-r14-final-20261104/TestResults/task-03-r20-linux-lifecycle-1793801975037/lifecycle.trx"
}
```

Сырые TRX остаются в ignored TestResults; полные Linux outputs, counters, manifest и Windows MCP summaries сохранены в evidence.
