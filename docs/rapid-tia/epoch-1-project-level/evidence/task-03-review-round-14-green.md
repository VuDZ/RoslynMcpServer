# task-03 — R14-001 GREEN

Дата: 2026-11-04. База1b617c4. Прежний исполнитель Codex; точный runtime model ID неизвестен. Scope только R14-001. [Фактический RED и фазовые ограничения](task-03-review-round-14-red.md).

## Финальный focused результат

Windows Release specialized Roslyn MCP run_test_by_filter, noBuild=false, includeFullOutput=true, includeBuildWarnings=true: **537/537**,0 failed. Все19 новых cases, прежний Baseline каталог, starter/callback/delivery/freshness/classification guards, Windows-only reviewed inventory и весь SourceStructure входят в filter. Все123 SourceStructure прошли; strict16 identities/kinds/paths в SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits не менялись. Новых analysis limits не добавилось.

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~WindowsOnlyTestInventoryTests|FullyQualifiedName~WorkspaceDiskWatcherStarterTests|FullyQualifiedName~SolutionManagerDiskWatcherCallbackTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **537** · Passed: **537** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.PlatformSupport.WindowsOnlyTestInventoryTests.Every_WPF_fact_is_in_the_windows_only_category', 'RoslynMcpServer.Tests.Workspace.SolutionManagerDiskWatcherCallbackTests.Directory_rename_still_requests_full_refresh_when_the_logger_throws', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Absent_props_file_is_seen_by_the_non_recursive_watcher', 'RoslynMcpServer.Tests.Workspace.WorkspaceInputFreshnessTests.Detected_aba_sequence_keeps_the_revision`

### Build warnings

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Поведение

- Deleted неизвестного exact map path разворачивается во все mapped descendants через существующий QueueDiskPath. Exact mapped file deletion не сканирует map; для directory event выполняется один map scan, descendant exact lookups не запускают его рекурсивно. Baseline Known сохраняется, pending документы получают write invalidation. Необратимый gap для обычного delete не вводится.
- Для исходного ignored/unmapped Deleted path baseline admission использует root-relative helper; semantic classification исходного события сохранена. Синтетические descendant callbacks закономерно добавляют собственные revisions/dirty/composition signals — неизменность общего количества событий не заявляется. Mapped obj/artifacts priority сохранён.
- Существующие descriptor subscriptions сохраняют recursion и point semantics. Дополнительные lifetime subscriptions — exact-name DirectoryName-only nonrecursive watcher родителя; посторонние parent paths отбрасываются ещё до callbacks. Цепочка продолжается, пока существующая descriptor subscription не наблюдает следующий parent, с дедупликацией guarded roots. Это не membership/search roots и не recursive ancestor watch.
- Все handlers используют прежний session admission; watcher startup errors записывают coverage gap, все дополнительные watchers хранятся/отключаются/Dispose вместе с основной группой. Publication по-прежнему после завершения startup. Missing-directory guard не расширяется на ancestor watcher.
- Native19-case каталог включает17native +2 deterministic cases:4 glob/explicit/root/build move-out; linked root с/без parent subscription; cross-parent move; mapped obj/artifacts; оба Deleted/Created порядка; recursive delete внутри project и linked own root; Empty/Unmapped/obj/bin/node_modules/.git guards. Обычным неизвестным деревьям гарантирован no-gap; no-journal assertion требуется ignored paths. Настоящий directory rename gap закреплён прежними native tests этого focused.

## Границы и передача

Windows cross-parent Directory.Move фактически дал Deleted исходного Sub, затем Created Other/Sub, без Renamed; trace сохранён в RED. mv/git mv не выполнялись. В тестах нет sleep как доказательства порядка: native receipt и bounded polling ожидают состояние, deterministic pair проверяет оба callback порядка. Eight-second timeout — предел ожидания fixture, не время фиксации baseline. Linux behavior этим прогоном не заявляется: parent выполнит3 full main на Linux и требуемые lifecycle/build проверки. Неизвестная KVM-причина R5-002 не исследовалась.

Поздняя obj fixture correction и неудачный145/144/1 кандидат сохранены в RED. Первый18-case RED содержал2 избыточных assertions и1 harness precondition failure, поэтому он не выдаётся полностью за behavioral RED. Окончательный canonical obj/artifacts pair повторён на временно восстановленном baseproduction,2/0/2, до восстановления fix. Итоговый19-case каталог не расширялся behavior cases после реализации.

**CODE FROZEN** сообщён немедленно после537/537; далее только docs. Изменены3 C# файла: Services/Workspace/SolutionManager.cs; Services/Workspace/WorkspaceDiskWatcherStarter.cs; новый RoslynMcpServer.Tests/Testing/BaselineDirectoryDisappearanceTests.cs. Версия1.5.18, statuses/spec/README, task04+, старые evidence и посторонний untracked skill не менялись; commit/push не выполнялись. IDE file edit API недоступен; использован разрешённый local filesystem fallback, не Roslyn MCP apply_patch/update_file_content. Final validation/review15/status/commit/push принадлежат координатору.


## Повторная заморозка после resource correction раунда15

Первоначальный freeze537/537 и дальнейший parent Windows main1746/1746 остаются историческими результатами. [Parent validation до раунда16](task-03-validation-before-round-16.md) сохранила первый Linux main1736/1738: inotify limit IOException при plugin host startup и unexpected touched path timestamp guard. Пиковая причинность второго failure не установлена; последующий /proc snapshot не является peak measurement. [Review15](task-03-review-round-15.md) потребовал bounded lifetime seed correction того же R14-001.

До этой дополнительной production правки написаны13 late diagnostic cases, actual [RED13/9/4](task-03-review-round-14-red.md):3 pure metadata/output exclusions failed;9 retained guards passed; real-MSBuild App count failed с13planned descriptors,25actual watchers и13extra roots. SDK AnalyzerConfig chain8guards отдельно сохранена; полное устранение Linux pressure до finalmain не заявляется. Это доказательство лишних FSW, не измерение пикового inotify usage.

Lifetime seed исключается только если descriptor nonrecursive, Roles непуст и все роли MetadataDependency/Output. Recursive membership, UserInput, Generated, AdditionalFile, AnalyzerConfig, EvaluationInput, Unknown, empty и mixed roles сохраняются. Existing pure metadata/output content subscriptions и их использование как parent boundaries не менялись. Полная root-lifetime coverage pure metadata/output inputs не заявляется: они не являются C# baseline document keys; это явный scope-limit, отмеченный shortcut comment с ссылкой на evidence. Глобальные limits/parallelism не менялись.

Fresh specialized Windows Release MCP, тот же полный filter, noBuild=false: **550/550**,0 failed. Все123 SourceStructure и strict inventory16 unchanged. Фактический вывод:

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~WindowsOnlyTestInventoryTests|FullyQualifiedName~WorkspaceDiskWatcherStarterTests|FullyQualifiedName~SolutionManagerDiskWatcherCallbackTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **550** · Passed: **550** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.SolutionManagerDiskWatcherCallbackTests.Directory_rename_still_requests_full_refresh_when_the_logger_throws', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles`

### Build warnings

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

**CODE RE-FROZEN** сообщён сразу после550/550, далее только docs. Current C# overlay4files: Services/Workspace/SolutionManager.cs; Services/Workspace/WorkspaceDiskWatcherStarter.cs; RoslynMcpServer.Tests/Testing/BaselineDirectoryDisappearanceTests.cs; RoslynMcpServer.Tests/Workspace/WorkspaceDiskWatcherStarterTests.cs. Всего новые32cases: исходные19 +13late diagnostic cases. Real App test пишет aftercounts в ITestOutputHelper, но specialized runner GREEN не возвращает stdout successful tests; aftercounts здесь не выдумываются. Parent может извлечь их из final TRX. Final Linux3main при прежнем128, Windows main/build, lifecycle и freshreview16 ещё parent-owned, этим focused не заменяются.


## Повторная заморозка после transitive closure и диагностического main

[Review16](task-03-review-round-16.md) вернул R14-001 для transitive boundary. [Parent before17 evidence](task-03-validation-before-round-17.md) содержит Windows1759/1759 и Linux1750/1751 предыдущего4-file freeze, изолированный timestamp1/1, диагностический main1750/1751 (timestamp PASS, independent Output notice failure) и [native Linux boundary RED1/0/1](task-03-r16-boundary-native-red.json). Эти результаты не выдаются за final acceptance.

До production closure correction добавлены2 runtime starter registration rows (MetadataDependency/Output) и1 real-MSBuild linked/output boundary case. Windows RED3/0/3 и Linux native RED1/0/1 сохранены в [RED](task-03-review-round-14-red.md). Первая Windows native operation AccessDenied1/0/1 — ограничение операции, не behavioral proof; причина отказа не установлена. Final test на Windows проверяет registration External parent guard + deterministic Deleted admission, на Linux выполняет настоящий Directory.Move с independent observer. Skip/category не вводились; mv/git mv не запускались.

Lifetime chain теперь останавливается на covering primary subscription только если тот сам eligible lifetime seed. Через исключённый pure metadata/output parent chain идёт дальше, используя его existing content watcher без дублирования. Таким образом, самостоятельные metadata-only chains по-прежнему исключены, но metadata parent eligible linked C# получает нужную transitive protection. Root guard allocation не расширен на unrelated trees.

Permanent diagnostics трёх test-only файлов усиливают timestamp preconditions и выводят initial/prewrite/postwrite RequiresFullScan, session reasons/coverage/revisions/events/rename paths и точные logger exceptions. Logger Messages не менялись. В диагностическом Linux main timestamp PASS с SourceEventRevision0/RequiresFullScanfalse; причина двух прежних failures остаётся Unknown. Ни logger, ни closure не объявляются исправлением непойманного timestamp failure. Наблюдение peak120/128 sampled0.1s не доказывает exact peak или причинность.

Independent Output notice race устранён только в тесте: bounded WaitUntil ждёт прежний exact Output/!CountsAsInputRevision predicate до прежних notice assertions и unchanged input revision assertion. Другой путь watcher не используется как receipt output. Production classification/доставка ради этой race не менялись.

Fresh Windows Release MCP build+test: **553/553**,0 failed, все123 SourceStructure, строгий inventory16 unchanged. Exact filter прежний; фактический output:

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~WindowsOnlyTestInventoryTests|FullyQualifiedName~WorkspaceDiskWatcherStarterTests|FullyQualifiedName~SolutionManagerDiskWatcherCallbackTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **553** · Passed: **553** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.SolutionManagerDiskWatcherCallbackTests.Directory_rename_still_requests_full_refresh_when_the_logger_throws', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles', 'RoslynMcpServer.Tests.PlatformSupport.WindowsOnlyTestInventoryTests.Every_WPF_fact_is_in_the_windows_only_category', 'RoslynMcpServer.Tests.Workspace.SolutionManagerDiskWatcherCallbackTests.Watcher_error_still_requests_full_refresh_when_the_logger_throws', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles`

### Build warnings

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

**CODE RE-FROZEN** сообщён немедленно после553/553; далее только docs. Final current C# overlay8files: Services/Workspace/SolutionManager.cs; Services/Workspace/WorkspaceDiskWatcherStarter.cs; RoslynMcpServer.Tests/Testing/BaselineDirectoryDisappearanceTests.cs; RoslynMcpServer.Tests/Workspace/WorkspaceDiskWatcherStarterTests.cs; RoslynMcpServer.Tests/Support/WorkspaceRecordingLogger.cs; RoslynMcpServer.Tests/Testing/BaselineLifecycleFixture.cs; RoslynMcpServer.Tests/Testing/BaselineSessionIntegrationTests.cs; RoslynMcpServer.Tests/Workspace/WorkspaceInputWatcherDeliveryTests.cs. Всего35newcases:19initial +13resource diagnostics +3boundary, без изменения counts существующих timestamp/output facts. Mandatory fresh review17/full final validation — у координатора. Версия/status/spec/README/unrelated skill не менялись; exact model ID неизвестен.


## Последняя согласованная correction: C# seeds и подтверждённый missing-root Error

[Parent before18](task-03-validation-before-round-18.md) сохранила Linux1745/1754 (9прямыхinotify128 exceptions) и Windows1761/1762 (strict native no-gap failure). Conditional review17 не является acceptance. Это две отдельные доработки одного R14-001.

**Scope resources.** Подтверждённый C# baseline строится из project.Documents, source RoleForDocument возвращает UserInput/Generated/Unknown. Lifetime seed теперь recursive OR empty OR Any(UserInput,Generated,Unknown,PotentialMembership). Standalone known AdditionalFile/AnalyzerConfig/EvaluationInput/MetadataDependency/Output не начинают lifetime chain, но все их content watchers остаются. Mixed C#/conservative роли и derived ancestors проходят через любые excluded primary boundaries без дублирования existing watchers. Это C# baseline+derived ancestors, не полная lifetime coverage всех non-C# inputs. Глобальные limits/parallelism не менялись.

До этой правки скорректированы3 избыточных retained-role expectations и realApp count assertion, добавлены PotentialMembership и3 excluded-boundary guards. [RED21/16/5](task-03-review-round-14-red.md):3scope rows, realApp и native linkeddelete failed; остальные16passed. Измеренные Windows фазы обычного App:25watchers доpureMeta,20после,12послеC#scope;13planned descriptors во всех трёх, один regular descriptor dedup.

**Root Error.** Diagnostic-only origin logging на прежнем routing повторила native2/1/1: Linked=True, Shared/Sub directoryExistsFalse, Win32Exception5 AccessDenied, watcher-error, безDirectoryRenamePaths. После этого написаны8 deterministic rows настоящего registered FileSystemWatcher.OnError: RED8/5/3 доbranchfix. Missing-root normal Error теперь синтезирует Deleted через общий classifier/journal. Overflow независимо от root existence и Error existing root идут прежним onError→gap. Ранее raised overflow/rename gaps не очищаются, closed session gate сохраняется. Для volume root без родителя остаётся conservative onError. Diagnostic logging protected try/catch, logger provider не выбрасывает из native callback. Native no-gap assertion не ослаблен; diagnostic bounded1s settle позволяет увидеть root Error после child Deleted, deterministic rows проверяют порядок routing безsleep/native timing.

Первый related GREEN173/173 включает все123SourceStructure. Отдельный measured count через MCP execute_dotnet_command нужен только потому, что specialized successful report не показывает ITestOutputHelper stdout: raw TRX test1/1,0skips. [Извлечённые counters/полный stdout](task-03-r18-app-counts.json): **13planned,12actual,0extra**. Исходный TRX artifacts/task-03-r18-app-counts/app-counts.trx сохранён в workspace, command output ниже:

## dotnet command


### StdOut
```text
Test run for E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\bin\Release\net10.0\RoslynMcpServer.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
Results File: E:\Devel\RoslynMcpServer-rapid-tia\artifacts\task-03-r18-app-counts\app-counts.trx

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 1 s - RoslynMcpServer.Tests.dll (net10.0)
```

### StdErr
(empty)

## Финальный focused565/565

Windows Release specialized MCP, тот же полный filter, noBuild=false, все123SourceStructure и strict inventory16 identities/kinds/paths unchanged. Фактический результат:

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~WindowsOnlyTestInventoryTests|FullyQualifiedName~WorkspaceDiskWatcherStarterTests|FullyQualifiedName~SolutionManagerDiskWatcherCallbackTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **565** · Passed: **565** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure', 'RoslynMcpServer.Tests.PlatformSupport.WindowsOnlyTestInventoryTests.Every_WPF_fact_is_in_the_windows_only_category`

**CODE FROZEN** сообщён немедленно после565/565, style/logging cleanup выполнен до него. После freeze только docs. Current overlay по-прежнему8C#files, listed выше; новыеCaseRows теперь47 (19initial +13resource +3boundary +4C#scope +8Error-route), существующие noGap/timestamp/output facts сохранены/усилены. Старые более широкие role expectations и предыдущие freezes являются историей и superseded новым согласованным C#scope. Новых structural-analysis limits нет. Три прежнихwarnings сохранены, последний incremental report новыхwarnings не напечатал.

Остающаяся причина старых timestamp failures Unknown: ни C#filter, ни logger, ни missing-root fix не объявляются её доказанным исправлением. Прямыеresourceexceptions и WinRootGoneError подтверждены отдельно. Final Windows/Linux builds, Windowsmain,3Linuxmain при обычныхlimits, lifecycleW/L и freshreview18 принадлежат координатору;565/565 не заменяет их и не объявляет задачу принятой. Версия1.5.18/status/spec/README/unrelatedskill не менялись, commit/push не выполнялись; model ID неизвестен.
