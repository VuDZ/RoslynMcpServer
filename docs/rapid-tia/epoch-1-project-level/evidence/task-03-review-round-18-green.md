# task-03 — review18 optional exact entry GREEN

Дата:2026-11-04, база1b617c4, прежний исполнитель Codex; exact runtime model ID неизвестен. Scope только refinement R14-001 из [review18](task-03-review-round-18.md). [RED до production](task-03-review-round-18-red.md): initial4/0/4 и final5/0/5; все fixtures/runtime failures фактические, не compilation RED.

## Минимальная правка

QueueDiskPath пропускает descendant scan лишь если exact map entry имел Present=true при загрузке. Наличие optional entry с Present=false не доказывает, что путь был файлом: Directory.Build.props/targets может быть каталогом с Known C# потомками. Exact present-file deletions сохраняют прежний fast path.

Scan исключает сам корневой entry (с нормализацией завершающего directory separator) и перечисляет все strict mapped descendants. Приватный expandDeletedDescendants=false у synthetic inner calls не даёт вложенным optional/missing entries повторно обходить весь map. Это implementation state, не публичная настройка. Для одного outer directory event выполняется один map enumeration и по одному обычному queue/classifier вызову на descendant; стоимость отдельных classification/occurrences не объявляется константой. No-gap semantics, current-key/hash identity и baseline Known retention прежние.

## Проверки

До production написаны4 rows: props/targets × native/deterministic. Windows native Directory.Move фактически дал Deleted корня + Created Detached; independent observer подтвердил receipt, exact Present=false/Directory.Exists=true/File.Exists=false и Known source preconditions прошли, journal source отсутствовал без gap. Это настоящий behavioral RED.

Пятый deterministic row с двумя реальными MSBuild projects добавлен также до production: nested optional Directory.Build.targets внутри outer candidate и два sources. После одного Deleted оба source EventRevision увеличиваются ровно на1; это behavior guard от повторного nested expansion, без source-text inspection. Все5 cases написаны до правки, поздних cases нет. Код tests исполняет native ветвь на обеих ОС; actual Linux result остаётся у координатора.

Fresh Windows Release specialized Roslyn MCP build+test: **570/570**,0 failed. Exact filter прежнего565, включает все Baseline, watcher starter/callback/delivery/freshness/classification, Windows inventory и весь SourceStructure. Все123 SourceStructure прошли; strict16 identities/kinds/paths не менялись. Actual output:

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~WindowsOnlyTestInventoryTests|FullyQualifiedName~WorkspaceDiskWatcherStarterTests|FullyQualifiedName~SolutionManagerDiskWatcherCallbackTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **570** · Passed: **570** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Ignored_trees_stay_ignored_even_for_loaded_project_candidates', 'RoslynMcpServer.Tests.Workspace.SolutionManagerDiskWatcherCallbackTests.Directory_rename_still_requests_full_refresh_when_the_logger_throws`

### Build warnings

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]

## Передача

**CODE FROZEN** сообщён сразу после570/570; style cleanup выполнен до этого прогона. С предыдущего565-freeze изменились только Services/Workspace/SolutionManager.cs и RoslynMcpServer.Tests/Testing/BaselineDirectoryDisappearanceTests.cs. Полный overlay задачи по-прежнему8C#files; новые evidence этого refinement — task-03-review-round-18-red.md/-green.md и дополнение implementation report. Версия1.5.18, task/spec/README/status, old evidence и unrelated untracked skill не менялись, commit/push не выполнялись. IDE edit API отсутствует, применён разрешённый local file fallback, не Roslyn MCP apply_patch/update_file_content.

[Предыдущий mandatory round](task-03-validation-before-round-19.md) на565 tree: Windows1774/1774, Linux1766/1766 трижды. Эти зелёные результаты остаются историческими и не заменяют обязательный rerun после fix. Final builds/main/lifecycle и independent review19, bookkeeping/commit/push — у координатора. Причина старых timestamp failures Unknown и этим shortcut fix не объявляется установленной или исправленной.
