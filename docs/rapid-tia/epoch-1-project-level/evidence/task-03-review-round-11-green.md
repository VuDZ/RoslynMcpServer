# task-03 — R11-001 GREEN

Дата: 2026-11-04. База 6bedfcf. Windows Release, Roslyn MCP run_test_by_filter, noBuild=false.

## Итог исполнителя

**511/511**, 0 failed: прежний focused491 +20 новых cases (9 native и11 pure paths). Все123 SourceStructure включены; точный inventory16 identities/kinds/paths не менялся.

Filter:
`FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`.

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **511** · Passed: **511** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Callback_paused_before_admission_cannot_write_after_reset_and_session_replacement', 'RoslynMcpServer.Tests.Workspace.WorkspaceInputFreshnessTests.Detected_aba_sequence_keeps_the_revision`

### Build warnings

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]

## Исправление

Единственный production change — internal WorkspaceDiskPathFilter.IsIgnoredPath(path, workspaceDirectory). Оба пути канонизируются, разбиваются на platform segments; совпавшее начало отбрасывается с InputPathCanon.Comparer, оставшиеся target segments проверяются тем же ignored set. Вне каталога solution фильтр больше не возвращает blanket false. Root itself и shared ancestors исключены; sibling ignored segments сохранены. Null/invalid workspace directory сохраняет legacy fallback.

Literal segment comparison выражает common-prefix контракт напрямую, включая общую server часть разных UNC shares. Pure Windows guards проверяют этот результат без UNC/network I/O, а также drive/case и разные drives. Публичный one-argument overload, R9 map authority/both endpoints, session admission, nonrename classification и document sync не менялись.

## Native доказательства и границы

- build/All.slnx + src/App: Directory.Move внутри obj и node_modules не даёт gap/journal endpoints; File.Move obj/temp.lock→index не попадает в journal.
- build/All.slnx + root project: настоящие git init/add/commit при работающем native watcher, receipt index/head rename, отсутствие .git в journal и gap.
- Directory.Move .git/rebase-apply→rebase-merge — реальная файловая операция, не заявляется Git rebase. Для node_modules также не запускался npm.
- Source directory gap, оба .cs rename endpoints и explicit obj input rename сохраняются при той же nested-solution раскладке; исходники имеют Known/fresh-journal предусловие.
- Во всех9 final native cases watcher не подавлялся, callbacks вручную не доставлялись. Receipt проверяется через Session.DirectoryRenamePaths/EventRevision до baseline assertions.

Первоначальный node_modules cross-parent move .staging/pkg→pkg дал Windows harness timeout при ожидании Renamed, поэтому не засчитан behavioral RED. Final fixture использует same-parent .staging/pkg→pkg2; отдельный pre-fix rerun двух ignored-directory rows дал2/0/2 на Assert.False(RequiresFullScan) после native receipt. [RED и полная честная история](task-03-review-round-11-red.md). Все20 cases и fixture correction написаны до production fix; после него cases не добавлялись.

## Передача

CODE FROZEN сообщён сразу после GREEN; после него только документация. Final Windows build/main, три Linux main, lifecycle по применимости и новое review принадлежат координатору. Исполнитель не запускал runtime на parent Linux roots; этот focused результат не подменяет final validation.

C# files3: Services/Workspace/WorkspaceDiskPathFilter.cs; RoslynMcpServer.Tests/Workspace/WorkspaceDiskPathFilterTests.cs; новый RoslynMcpServer.Tests/Testing/BaselineNestedSolutionRenameTests.cs. Версия1.5.18, statuses/spec/README, task04+, старые evidence и посторонний untracked skill не менялись; commit/push не выполнялись. Runtime model ID неизвестен. Неизвестная KVM-причина R5-002 не исследовалась и не объявляется установленной.


## Поздняя точная Windows-only inventory доработка

Дата: 2026-11-04. После parent main RED, описанного в [RED](task-03-review-round-11-red.md) и [validation до раунда13](task-03-validation-before-round-13.md), в WindowsOnlyTestInventoryTests.cs добавлена ровно одна ожидаемая identity с кратким why-comment: WorkspaceDiskPathFilterTests.Workspace_filter_respects_windows_roots_without_filesystem_access. Строгий Assert.Equal и RequiresWindows категории сохранены. Reviewed список теперь содержит5 методов; новый метод содержит4 theory rows. Никаких production edits или новых cases на этой фазе нет.

Windows Release specialized Roslyn MCP, noBuild=false, includeFullOutput=true, includeBuildWarnings=true: **28/28**,0 failed. Это2 inventory facts и26 path-filter cases. Вывод ниже является фактическим результатом. Напечатан существующий xUnit1031 в WpfTemporaryProjectCallbackTests.cs:195; новая правка warning не вводит.

## Filtered test run

**Filter:** `FullyQualifiedName~WindowsOnlyTestInventoryTests|FullyQualifiedName~WorkspaceDiskPathFilterTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **28** · Passed: **28** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix', 'RoslynMcpServer.Tests.PlatformSupport.WindowsOnlyTestInventoryTests.Every_WPF_fact_is_in_the_windows_only_category', 'RoslynMcpServer.Tests.PlatformSupport.WindowsOnlyTestInventoryTests.Windows_only_category_matches_the_reviewed_list`

### Build warnings

- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

**CODE FROZEN повторно сообщён сразу после28/28.** Текущий overlay содержит4 C# файла: прежние3 плюс RoslynMcpServer.Tests/PlatformSupport/WindowsOnlyTestInventoryTests.cs. Первоначальные511/511 и review12 остаются историческими фазами до full-main inventory failure. Новые final build/main, три Linux main, lifecycle по применимости и fresh review13 выполняет координатор; этот focused GREEN не заявляется окончательной приёмкой. Статусы/spec/README/version и parent evidence не менялись.
