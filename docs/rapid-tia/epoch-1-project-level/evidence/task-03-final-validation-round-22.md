# task-03 — финальная validation после раунда 22

- Дата: 2026-11-05 (Europe/Moscow).
- R21-001 evidence: RED, GREEN, before/after measurements, исходные rows/counters.
- База `1622e41`, окончательный CODE FROZEN: два production C# файла и новый BaselineDeletedPathCostTests (13 cases). После freeze C# не менялся.
- Независимое review22 — ACCEPT; закрыт R21-001, R14-001 сохранён. Task-03: 22 раунда, 12 исправленных уникальных findings. Эпоха целиком, task-04+ и публичная активация не принимаются.
- Координатор: Windows Release solution build успешен; focused **611/611**, включая все **123 SourceStructure**, точный inventory **16** неизменен; main **1800/1800**; opted-in AnalyzerLifecycle **13/13**.
- Ubuntu24.04/WSL, SDK10.0.114: Release solution build успешен; три последовательных полных main **1792/1792 каждый**; opted-in AnalyzerLifecycle **13/13**. TRX: total=executed=passed>0, failed/error/timeout/aborted/notExecuted=0.
- Linux build, main counters, main1, main2, main3, lifecycle counters, lifecycle log.
- SHA-256 manifest трёх C# файлов проверялся перед каждым Linux запуском и после всех проверок на обеих ОС. Linux checkout `HOME_DIR/roslyn-mcp-rapid-tia-r21-final-20261105`: detached1622e41 + exact final overlay, без диагностических production edits.
- Build на обеих ОС с --no-incremental; только три прежних warning: CS8603 NuGetFallbackAssemblyResolver:72, CS8601 HostSession:1179, xUnit1031 WpfTemporaryProjectCallbackTests:195.
- Все финальные tests: Release --no-build --no-restore. Main filter Windows: Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk; Linux дополнительно &Category!=RequiresWindows. Разница8 — workflow exclusions, не TRX skips.
- Специализированный MCP не выражает WSL integration environment: Linux использует разрешённый AGENTS.md runner. Windows build/tests через Roslyn MCP; Windows lifecycle — execute_dotnet_command с --environment ROSLYN_MCP_ANALYZER_LIFECYCLE=1. Linux environment явно opted-in. Lifecycle повторён из-за изменения watcher processing path.
- Inotify max_user_instances128, max_user_watches524288, max_queued_events16384 и штатная параллельность не менялись. Нагрузочные measurements завершены до final main, diagnostic profiler не применялся.
- Перед исправлением runtime counting RED **7/0/7** на обеих ОС; Windows native node_modules RED с прямым InternalBufferOverflowException. На WSL before native3/3 прошли: overflow рецензента KVM здесь не воспроизведён. Это не объявляется локальным Linux RED.
- Windows obj2000/5000 overflow существует и на1b617c4, и после fix; этот ряд — measurement, не новый no-gap контракт. Node_modules50/200 сохраняют строгие no-gap/no-journal проверки. Время удаления и callback tail записываются отдельно; единичные замеры не доказывают универсальную скорость или частоту переполнений.
- Сохранены Present=false optional candidates, WPF-named directory/file guards, mapped ignored inputs, linked directories/root disappearance, recursive deletion без ложного gap, прежние Changed/Renamed и text sync. Индексы строятся при создании карты, при empty Deleted карта и occurrences не обходятся.
- Ограничения раунда21 сохраняются: внешний non-C# directory с собственным watcher может исчезнуть молча; после удаления/восстановления граф требует reload; inotify128 может дать startup gap средним solution. Прежние timestamp/KVM R5-002 причины не установлены. Отдельный GC probe не запускался, версия1.5.18 сохранена.

## Windows Release solution build

## Build succeeded

- **warning** `CS8603` `REPO_ROOT/Services/Resolve/NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [REPO_ROOT/RoslynMcpServer.csproj]
- **warning** `CS8601` `REPO_ROOT/RoslynMcpServer.LifecycleTestHost/HostSession.cs(1179,34)` — Possible null reference assignment. [REPO_ROOT/RoslynMcpServer.LifecycleTestHost/RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `REPO_ROOT/RoslynMcpServer.Tests/Workspace/WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [REPO_ROOT/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj]

## Windows focused

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputMapTests|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~WindowsOnlyTestInventoryTests|FullyQualifiedName~WorkspaceDiskWatcherStarterTests|FullyQualifiedName~SolutionManagerDiskWatcherCallbackTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **611** · Passed: **611** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.SolutionManagerDiskWatcherCallbackTests.Directory_rename_still_requests_full_refresh_when_the_logger_throws', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure`

## Windows main CI suite

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1800** · Passed: **1800** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Config.RoslynMcpJsoncParserTests.StripComments_allows_line_and_block_comments', 'RoslynMcpServer.Tests.Build.DotNetCliRunnerEnvironmentTests.Missing_processor_architecture_is_filled_only_on_Windows', 'RoslynMcpServer.Tests.Logging.ProcessOutputExcerptTests.BuildStdoutExcerpt_uses_head_and_tail_when_long', 'RoslynMcpServer.Tests.Analyzers.AnalyzerShadowPublicationPlannerTests.Confirmed_overlay_excludes_foreign_and_unconfirmed', 'RoslynMcpServer.Tests.Build.DotNetHostResolverTests.ResolveDotNetExecutable_on_64bit_windows_prefers_program_files_dotnet`

## Windows opted-in AnalyzerLifecycle

Команда: `dotnet test "REPO_ROOT/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj" -c Release --no-build --no-restore --filter "Category=AnalyzerLifecycle" --environment "ROSLYN_MCP_ANALYZER_LIFECYCLE=1" --logger "trx;LogFileName=lifecycle.trx" --results-directory "REPO_ROOT/TestResults/task-03-round-22-lifecycle-1793895972726"`

VSTest — прошло 13 из 13, упало 0, пропущено 0.

TRX counters отдельно проверены: total=executed=passed=13, failed/error/timeout/aborted/notExecuted=0. Сырые TRX остаются в ignored TestResults; Сводные результаты обеих платформ приведены выше.
