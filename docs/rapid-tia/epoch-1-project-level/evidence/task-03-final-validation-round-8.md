# task-03 — окончательная validation после раунда 8

- Дата: 2026-11-03 (Europe/Moscow).
- База `31cc8d8`, окончательный CODE FROZEN после R7-001; дальнейшие правки только документации.
- [Независимое ревью раунда8 — ACCEPT](task-03-review-round-8.md). R5-001…R5-004 и R7-001 закрыты; task-03 всего8 раундов/8 исправленных уникальных замечаний.
- Windows SDK10.0.402, Roslyn MCP: Release solution build успешен, focused **385/385** (baseline254 + SourceStructure123 + watcher delivery8); окончательный повтор полного main **1687/1687**; opted-in AnalyzerLifecycle **13/13**.
- Ubuntu24.04/WSL, runner сообщил SDK10.0.114: Release solution build успешен; **три последовательных полных main suite, каждый1683/1683**, без падений и пропусков. [Все TRX counters](task-03-final-validation-round-8-linux/summary.json), [лог1](task-03-final-validation-round-8-linux/main-1.log), [лог2](task-03-final-validation-round-8-linux/main-2.log), [лог3](task-03-final-validation-round-8-linux/main-3.log), [полная сборка](task-03-final-validation-round-8-linux/build.log).
- Все проверки выше выполнены после последнего изменения C#; старые неудачные/отменённые прогоны их не подменяют. Полные сборки используют --no-incremental; тесты запускаются против них без новой сборки.
- [SHA-256 manifest](task-03-final-validation-round-8-linux/source-manifest.json) связывает9 контрольных C# файлов Windows/Linux: восемь изменённых и восстановленный к базе cached Integration. Хеши проверены перед каждой Linux suite и после неё, повторно сверены на Windows. Диагностическая instrumentation в окончательные binaries не переносилась.
- SourceStructure exact inventory16 с прежними identities/kinds/paths не менялся, новых opaque limits нет.
- Успешному Windows main предшествовал **1686/1687**, где неизменённый CLI-тест получил исчезнувший process-global cwd. Отдельный повтор1/1 и полный повтор1687/1687 прошли на тех же binaries. [Исходный failure и границы вывода](task-03-windows-cwd-retry.md). Существующая гонка cwd не объявляется исправленной task-03 и остаётся наблюдавшимся ограничением стабильности всей suite.
- Linux main до поздних fixture поправок падал1682/1683; после следующего freeze два Linux main прошли, третий явно отменён из-за R7-001. Эти фазы сохранены в [validation до раунда7](task-03-validation-before-round-7.md), [ревью7](task-03-review-round-7.md) и отчёте.
- Специализированный MCP test runner не выражает Linux/WSL окружение; применён разрешённый AGENTS.md integration runner в отдельной копии. Для Windows AnalyzerLifecycle API не принимает env; использован MCP runtime runner с --environment ROSLYN_MCP_ANALYZER_LIFECYCLE=1.
- Linux filter дополнительно исключает RequiresWindows, как workflow. Все три XML counters подтверждены отдельно: total=executed=passed=1683, failed=notExecuted=error=timeout=aborted=0. Windows lifecycle TRX также проверен отдельно, все13 выполнены и прошли.
- Три warning обеих сборок прежние: CS8603 NuGetFallbackAssemblyResolver:72, CS8601 HostSession:1179, xUnit1031 WpfTemporaryProjectCallbackTests:195. Новых warning нет.
- Linux AnalyzerLifecycle и отдельный GC probe не запускались. Источник корректировки часов WSL не устанавливался; controlled future-mtime proof не выдаётся за точную причину каждого прежнего startup failure.
- Версия1.5.18 сохранена; task-04+ и публичная активация не начаты. После настоящего observation gap rescan остаётся консервативным, полнота наблюдения не-C# входов не восстанавливается.

## Windows Release solution build

## Build succeeded

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Окончательный focused

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **385** · Passed: **385** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes', 'RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing`

## Окончательный Windows main повтор

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1687** · Passed: **1687** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.SolutionProjectTargetResolverTests.TryResolve_rejects_csproj_extension', 'RoslynMcpServer.Tests.Workspace.WorkspaceLoadGuidanceTests.IsRoslynMsBuildBuildHostFailure_detects_type_initializer_exception', 'RoslynMcpServer.Tests.Workspace.SkippedWorkspaceWriteDiskPolicyTests.ShouldWriteSkippedPathToDisk_only_for_unknown_snapshot_paths', 'RoslynMcpServer.Tests.Workspace.WorkspaceLoadGuidanceTests.FormatRoslynMsBuildBuildHostFailureMessage_is_not_sdk_mismatch', 'RoslynMcpServer.Tests.Build.SdkMismatchDiagnosticsTests.TryParseDotNetVersionFromMetadata_reads_version_line`

## Linux команды и итог

Рабочая копия `/home/runner/roslyn-mcp-rapid-tia-r5-final-r7-20261103`, повторно синхронизированная окончательным overlay раунда8 с проверкой всех9 хешей:

```text
dotnet build RoslynMcpServer.sln -c Release --no-incremental
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter "Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk&Category!=RequiresWindows" --logger "trx;LogFileName=main.trx" --results-directory <unique-run-directory>
```

Каждый из трёх запусков использует отдельный каталог TestResults; фактические пути и полные counters сохранены в summary.json.

```json
[
  {
    "run": 1,
    "exitCode": 0,
    "total": "1683",
    "executed": "1683",
    "passed": "1683",
    "failed": "0",
    "notExecuted": "0"
  },
  {
    "run": 2,
    "exitCode": 0,
    "total": "1683",
    "executed": "1683",
    "passed": "1683",
    "failed": "0",
    "notExecuted": "0"
  },
  {
    "run": 3,
    "exitCode": 0,
    "total": "1683",
    "executed": "1683",
    "passed": "1683",
    "failed": "0",
    "notExecuted": "0"
  }
]
```

## Windows opted-in AnalyzerLifecycle

```text
dotnet test "E:/Devel/RoslynMcpServer-rapid-tia/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj" -c Release --no-build --no-restore --filter "Category=AnalyzerLifecycle" --environment "ROSLYN_MCP_ANALYZER_LIFECYCLE=1" --logger "trx;LogFileName=task-03-round-8-lifecycle.trx" --results-directory "E:/Devel/RoslynMcpServer-rapid-tia/TestResults/task-03-round-8-lifecycle-1793739123460"
```

## dotnet command


### StdOut
```text
Test run for E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\bin\Release\net10.0\RoslynMcpServer.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
Results File: E:\Devel\RoslynMcpServer-rapid-tia\TestResults\task-03-round-8-lifecycle-1793739123460\task-03-round-8-lifecycle.trx

Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 1 m 5 s - RoslynMcpServer.Tests.dll (net10.0)
```

### StdErr
(empty)

Проверенные TRX counters:

```json
{"total":"13","notExecuted":"0","error":"0","aborted":"0","executed":"13","passed":"13","failed":"0","timeout":"0"}
```

Сырые TRX остаются в локальных ignored TestResults каталогах. Итоги, counters, source manifest и Linux логи сохранены в evidence.
