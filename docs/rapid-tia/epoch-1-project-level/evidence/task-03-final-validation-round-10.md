# task-03 — финальная validation после раунда 10

- Дата: 2026-11-04 (Europe/Moscow).
- База `bfbe27c`, окончательные пять C# файлов после CODE FROZEN, форматирования вызовов и уточнения XML docs. После сборок код не менялся.
- [Независимое ревью10 — ACCEPT](task-03-review-round-10.md): R9-001 закрыт. Task-03:10 раундов/9 исправленных уникальных замечаний.
- Release solution build Windows и Linux успешны. Только три прежних warning: CS8603 NuGetFallbackAssemblyResolver:72, CS8601 HostSession:1179, xUnit1031 WpfTemporaryProjectCallbackTests:195.
- Координатор Windows/Roslyn MCP: focused **491/491** (основной405 +86 legacy guards, все123 SourceStructure); полный main CI suite **1707/1707**; opted-in AnalyzerLifecycle **13/13**. Все прогоны без падений и пропусков.
- Ubuntu24.04/WSL, runner SDK10.0.114: **три последовательных полных main suite, каждый1703/1703**, без падений и пропусков. [Counters всех TRX](task-03-final-validation-round-10-linux/summary.json), [лог1](task-03-final-validation-round-10-linux/main-1.log), [лог2](task-03-final-validation-round-10-linux/main-2.log), [лог3](task-03-final-validation-round-10-linux/main-3.log), [полная сборка](task-03-final-validation-round-10-linux/build.log).
- SourceStructure точный inventory16 с прежними identities/kinds/paths не менялся, новых limits нет.
- [SHA-256 manifest](task-03-final-validation-round-10-linux/source-manifest.json) связывает все5 изменённых C# файлов Windows/Linux. Проверка выполнялась перед каждой Linux suite и после неё; Windows файлы повторно сверены с финальной сборкой.
- Полные сборки --no-incremental; тесты против этих binaries без новой сборки. Linux использовал отдельный checkout базы bfbe27c с проверенным overlay, без прежних диагностических правок.
- Специализированный MCP runner не выражает Linux/WSL окружение, поэтому применён разрешённый AGENTS.md integration runner. Windows lifecycle API не принимает env: использован MCP execute_dotnet_command с --environment ROSLYN_MCP_ANALYZER_LIFECYCLE=1.
- Изменение watcher session construction относится к load path, поэтому AnalyzerLifecycle повторён. Его уникальный TRX отдельно проверен: total=executed=passed=13, failed=notExecuted=error=timeout=aborted=0. Три Linux XML counters аналогично подтверждают1703 выполненных/прошедших, без failed/skip/error.
- Git ancestor guards проверяют rename-handler: реальные Git операции при подавленных native callbacks и явной доставке metadata rename. Native .cs/directory regressions проверяют независимый приём настоящего события. Поведение прочих Changed/Created событий, включая прежний Consult.Unknown под ignored ancestor, сохранено; фильтрация всех native Git events не заявляется.
- R5-002 KVM failure не переисследовался и не объясняется WSL trace. Это неблокирующее ограничение из раунда9. Linux AnalyzerLifecycle и отдельный GC probe не запускались.
- Версия1.5.18 сохранена. Task-04+ и публичный инструмент не реализованы, эпоха целиком не принята.

## Windows Release solution build

## Build succeeded

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Окончательный focused

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **491** · Passed: **491** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Temporary_project_name_is_separate_from_graph_file_classification', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Ignored_trees_stay_ignored_even_for_loaded_project_candidates', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Callback_paused_before_admission_cannot_write_after_reset_and_session_replacement`

## Windows main CI suite

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1707** · Passed: **1707** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Build.DotNetHostResolverTests.ResolveDotNetExecutable_on_64bit_windows_prefers_program_files_dotnet', 'RoslynMcpServer.Tests.Config.RoslynMcpJsoncParserTests.StripComments_allows_line_and_block_comments', 'RoslynMcpServer.Tests.Build.SdkMismatchDiagnosticsTests.TryParseDotNetVersionFromMetadata_reads_version_line', 'RoslynMcpServer.Tests.Build.DotNetBuildArgumentsTests.Normalize_rejects_unsafe_characters', 'RoslynMcpServer.Tests.Build.MsBuildInstanceSelectorTests.FindLatestSdkDirectory_skips_roots_without_msbuild_and_picks_the_highest_folder_name`

## Linux команды и итог

Рабочая копия `/home/runner/roslyn-mcp-rapid-tia-r9-final-20261104`:

```text
dotnet build RoslynMcpServer.sln -c Release --no-incremental
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter "Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk&Category!=RequiresWindows" --logger "trx;LogFileName=main.trx" --results-directory <unique-run-directory>
```

Каждый запуск — отдельный каталог TestResults; фактические пути и все counters сохранены в summary.json. RequiresWindows исключается фильтром workflow, а не считается TRX skip.

```json
[
  {
    "run": 1,
    "exitCode": 0,
    "total": "1703",
    "executed": "1703",
    "passed": "1703",
    "failed": "0",
    "notExecuted": "0"
  },
  {
    "run": 2,
    "exitCode": 0,
    "total": "1703",
    "executed": "1703",
    "passed": "1703",
    "failed": "0",
    "notExecuted": "0"
  },
  {
    "run": 3,
    "exitCode": 0,
    "total": "1703",
    "executed": "1703",
    "passed": "1703",
    "failed": "0",
    "notExecuted": "0"
  }
]
```

## Windows AnalyzerLifecycle с opt-in

```text
dotnet test "E:/Devel/RoslynMcpServer-rapid-tia/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj" -c Release --no-build --no-restore --filter "Category=AnalyzerLifecycle" --environment "ROSLYN_MCP_ANALYZER_LIFECYCLE=1" --logger "trx;LogFileName=task-03-round-10-lifecycle.trx" --results-directory "E:/Devel/RoslynMcpServer-rapid-tia/TestResults/task-03-round-10-lifecycle-1793743608001"
```

## dotnet command


### StdOut
```text
Test run for E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\bin\Release\net10.0\RoslynMcpServer.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
Results File: E:\Devel\RoslynMcpServer-rapid-tia\TestResults\task-03-round-10-lifecycle-1793743608001\task-03-round-10-lifecycle.trx

Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 1 m 11 s - RoslynMcpServer.Tests.dll (net10.0)
```

### StdErr
(empty)

Проверенные TRX counters:

```json
{"total":"13","failed":"0","notExecuted":"0","passed":"13","aborted":"0","timeout":"0","executed":"13","error":"0"}
```

Сырые TRX остаются в локальных ignored TestResults. Итоги/counters, source manifest и Linux build/main logs сохранены в evidence.
