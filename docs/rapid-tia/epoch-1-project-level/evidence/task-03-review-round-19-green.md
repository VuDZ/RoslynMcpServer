# task-03 — review19 WPF prefilter GREEN

Дата:2026-11-04, база1b617c4. Прежний исполнитель Codex; точный runtime model ID неизвестен. Scope — [review19 refinement R14-001](task-03-review-round-19.md), без task04 и public activation. [Actual RED8/2/6](task-03-review-round-19-red.md) записан до production.

## Порядок admission

После canonical path validation захватывается session.InputMap и выполняется mapped Deleted-descendant stage. Она больше не зависит от WPF-looking имени root. Сохраняются Present=true file fast path, strict self exclusion и однократная expansion nested descendants. Далее применяется прежний WPF semantic suppression; для Deleted mapped TextDocument occurrence.DocumentId отдельно записывается только baseline fact. Это покрывает Known Compile Item_wpftmp.csproj, включая synthetic descendant call, без расширения graph/classification/dirty behavior самого WPF-looking файла.

Остальные legacy filters проверены: Changed-directory и own-write deferral не относятся к Deleted; ignored filter пропускает known entries по map authority, Known document classification не ProvenIrrelevant. Invalid raw path не valid map key; mapless behavior остаётся прежним. Прямой Deleted WPF-named Compile file теперь journaled, но его EventRevision/dirty paths/graph-stale flags остаются как при прежнем WPF suppression. Changed semantic support необычных extensions не заявляется.

## TDD и guards

Все8 cases написаны до production. Пять folder rows: WPF-named directory/обычныйsource, обычныйdirectory/WPF-namedsource, combo;2native и3deterministic. Во всех Known/mapped DocumentId preconditions подтвердились, включая Compile с .csproj extension. Windows native Directory.Move дал независимый Deleted original + Created Detached receipt, безgap и с missingjournal доfix. Отдельный direct-file fact требует baseline-only Deleted admission. Два actual unmapped temporary XML-file guards с map и без map уже были зелёными в RED и остаются зелёными; existing loaded-membership/real-graph/rename WPF guards входят в полный filter.

Fresh Windows Release specialized Roslyn MCP build+test: **578/578**,0 failed. Все123 SourceStructure и strict inventory16 identities/kinds/paths unchanged. Три напечатанныхwarnings прежние (CS8603, CS8601, xUnit1031). Exact filter и фактический output:

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~WindowsOnlyTestInventoryTests|FullyQualifiedName~WorkspaceDiskWatcherStarterTests|FullyQualifiedName~SolutionManagerDiskWatcherCallbackTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **578** · Passed: **578** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Temporary_project_name_is_separate_from_graph_file_classification', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Ignored_trees_stay_ignored_even_for_loaded_project_candidates`

### Build warnings

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Передача

**CODE FROZEN** сообщён сразу после578/578; весь C#/style cleanup до этого прогона. Current overlay по-прежнему8C#files. С предыдущего570-freeze изменены только Services/Workspace/SolutionManager.cs и RoslynMcpServer.Tests/Testing/BaselineDirectoryDisappearanceTests.cs. Добавлены отдельные review-round-19-red.md/-green.md и append implementation report; старые evidence сохранены.

[До fix main570 tree](task-03-validation-before-round-20.md): Windows1779/1779 и3×Linux1771/1771 прошли, но review19 выявило реальный missingjournal. Эти результаты исторические. Final build/main/lifecycle/freshreview20 и acceptance/status/commit/push принадлежат координатору. Автор не запускал Linux runtime. Версия1.5.18, spec/README/status/unrelatedskill не менялись; commit/push не выполнялись, IDE local fallback авторизован. Источник старых timestamp failures остаётся Unknown, WPFfix не приписывается их устранение.
