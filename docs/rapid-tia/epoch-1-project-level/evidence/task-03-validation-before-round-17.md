# task-03 — validation до раунда 17

Дата2026-11-04, база1b617c4. Freeze после pure metadata/output seed exclusion: [четыре C# SHA-256](task-03-validation-before-round-17-source-manifest.json).

- Release solution build Windows/Linux успешны, три прежних warning.
- Parent Windows focused550/550; все123 SourceStructure, strict16 unchanged.
- Полный Windows main1759/1759 прошёл.
- Первый Linux main1750/1751, skipped0: Timestamp_guard_works_without_any_watcher_event, строка72 Assert.DoesNotContain source inTouchedPaths; collection содержит два baseline paths. Это не доказывает причину gap либо фактической записи.
- [Полный Linux log](task-03-validation-before-round-17-linux.log), [TRX counters](task-03-validation-before-round-17-linux.json). Два следующих main не запускались, runner остановился после failure. Lifecycle на этом freeze не запускался.
- Изолированный тот же timestamp-тест без новых исходников/лимитов прошёл1/1: [лог](task-03-validation-before-round-17-timestamp-isolated.log), [counters](task-03-validation-before-round-17-timestamp-isolated.json). Он не заменяет три main suite.
- Прямой inotify exception в PluginStartup в этом прогоне не повторился. Влияние давления ресурсов на timestamp failure пока не доказано. Лимиты128/524288, fd10240 и параллельность16CPU не менялись.
- [Ревью16](task-03-review-round-16.md) независимо нашло транзитивную lifetime boundary, которую нужно исправить с RED. Одновременно требуется уточнить причину оставшегося Linux failure. Успех main/acceptance не объявляется.

## Дополнительная диагностика

Дополнительная диагностика на прежнем r16 production: три test-only файла добавили exception capture и более сильные preconditions timestamp-теста, не ослабляя assertions. [Диагностическая сборка](task-03-r16-timestamp-diagnostic-build.log), [семь source hashes](task-03-r16-timestamp-diagnostic-source-manifest.json), [полный main](task-03-r16-timestamp-diagnostic-main.log), [counters/наблюдения](task-03-r16-timestamp-diagnostic-summary.json). Итог1750/1751: timestamp прошёл с RequiresFullScan=false и SourceEventRevision=0; [его полный output](task-03-r16-timestamp-diagnostic-test-output.log) сохранён с нормализацией trailing whitespace; исходный TRX остаётся в ignored TestResults. Причина двух прежних timestamp failures всё ещё не доказана; logger не объявляется их исправлением. При sampling0.1s максимальное наблюдённое число inotify instances120 при лимите128, это не строгая верхняя граница peak.

Диагностический main выявил иной failure: Explicit_obj_input_is_synced_and_generated_outputs_do_not_bump_input_revisions требует Output notice без ожидания её доставки, хотя ждёт notices других файлов. Исправление теста должно дождаться того же Output/!CountsAsInputRevision predicate перед прежним assertion; production ради этого не меняется.

Транзитивная boundary отдельно воспроизведена до production fix: [native Linux RED1/0/1](task-03-r16-boundary-native-red.log), [counters](task-03-r16-boundary-native-red.json), [source hashes](task-03-r16-boundary-native-red-source-manifest.json). Independent observer подтвердил Renamed External/Shared→Detached, затем Known linked source отсутствовал в journal при опущенном gap. На Windows первый native Directory.Move получил AccessDenied до event: это operation limitation, не behavioral RED; там закреплены actual registration и deterministic Deleted, Linux выполняет native case. mv/git mv не запускались.

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

Total: **550** · Passed: **550** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix', 'RoslynMcpServer.Tests.Workspace.SolutionManagerDiskWatcherCallbackTests.Directory_rename_still_requests_full_refresh_when_the_logger_throws', 'RoslynMcpServer.Tests.Workspace.SolutionManagerDiskWatcherCallbackTests.Watcher_error_still_requests_full_refresh_when_the_logger_throws', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation`

## Windows main

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1759** · Passed: **1759** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Build.DotNetHostResolverTests.ResolveDotNetExecutable_on_64bit_windows_prefers_program_files_dotnet', 'RoslynMcpServer.Tests.Workspace.SolutionProjectTargetResolverTests.TryResolve_rejects_csproj_extension', 'RoslynMcpServer.Tests.Config.RoslynMcpJsoncParserTests.StripComments_allows_line_and_block_comments', 'RoslynMcpServer.Tests.Navigation.CodeSkeletonToolsTests.GetCodeSkeleton_returns_friendly_message_when_path_missing', 'RoslynMcpServer.Tests.Workspace.SolutionProjectTargetResolverTests.TryResolve_matches_virtual_path_when_display_name_is_ambiguous`
