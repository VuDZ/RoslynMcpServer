# task-03 — R9-001 GREEN

Дата: 2026-11-04. База bfbe27c. Windows Release, специализированный Roslyn MCP run_test_by_filter, noBuild=false.

## Результат исполнителя

**491/491**, 0 failed: baseline и watcher delivery вместе со всем SourceStructure и существующими WorkspaceDiskEventClassificationTests, WorkspaceDiskPathFilterTests, WorkspaceInputFreshnessTests, WpfTemporaryProjectCallbackTests. В основной выборке теперь 405 cases вместо385 (добавлены20), плюс86 дополнительных legacy guards. Все123 SourceStructure включены; строгий inventory16 identities/kinds/paths не менялся.

Filter:
`FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`.

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~WorkspaceDiskEventClassificationTests|FullyQualifiedName~WorkspaceDiskPathFilterTests|FullyQualifiedName~WorkspaceInputFreshnessTests|FullyQualifiedName~WpfTemporaryProjectCallbackTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **491** · Passed: **491** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Temporary_project_name_is_separate_from_graph_file_classification', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Real_graph_files_keep_their_existing_invalidation', 'RoslynMcpServer.Tests.Workspace.WpfTemporaryProjectCallbackTests.Ignored_trees_stay_ignored_even_for_loaded_project_candidates', 'RoslynMcpServer.Tests.Workspace.WorkspaceInputFreshnessTests.Detected_aba_sequence_keeps_the_revision`

### Build warnings

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

После результата выполнена только обязательная доводка формата новых expanded calls (один аргумент на строку) и уточнён XML comment о legacy fallback при null root. Семантика не менялась; затем сообщён CODE FROZEN. Координатор проверяет окончательные исходники полными build/suites. Полные Windows main, три Linux main и AnalyzerLifecycle в этот focused результат не входят.

## Что проверено

- Настоящий native watcher под обоими ancestors artifacts/bin принимает .cs rename и directory rename; receipt фиксируется отдельно от baseline. Оба .cs endpoint попадают в journal, настоящее directory rename поднимает RequiresFullScan.
- Native directory guards также перемещают obj/Sub с mapped descendant: root-relative ignored directory не скрывает явно известный вход.
- Явный obj/Manual.cs: mapped old, mapped new (tmp save), оба mapped endpoints — в обычном root и под artifacts/bin. Изменяемые исходники Known, их пути отсутствуют в journal до операции; реальные File.Move сопровождаются детерминированной доставкой rename callback.
- Явный .git/Manual.cs→unmapped .git/Moved.cs сохраняет оба пути: карта имеет приоритет даже там, где прежняя semantic classification отбрасывает unmapped counterpart. Проверяется baseline journal, прежняя semantic ветвь не переопределяется.
- Реальные Git add/commit под обоими ancestors: для этих строк native watchers подавлены, затем явно доставляется index.lock→index rename. Так проверяется rename-only исключение metadata. Прежние Git guards обычного root сохраняют native watchers.

Граница последнего пункта существенна: исходное Changed/Created Consult.Unknown поведение под ignored ancestor могло журналировать Git paths и остаётся прежним. Этот результат не объявляет все native Git events отфильтрованными. Изменён только baseline admission в rename handler; legacy map/session revisions и semantic refresh/classification не менялись.

## Файлы и границы

Production: Services/Models/WorkspaceInputSession.cs, Services/Workspace/WorkspaceDiskPathFilter.cs, Services/Workspace/WorkspaceDiskEventClassifier.cs, Services/Workspace/SolutionManager.cs. Tests: RoslynMcpServer.Tests/Testing/BaselineRenameJournalTests.cs.

Session хранит immutable directory загруженного solution/csproj; callbacks не читают позднее изменяемый LoadedPath. Root-relative overload исключает собственное имя root и его ancestors, а для paths outside root не применяет ancestor filter. Старый public IsIgnoredPath(fullPath) не менялся; null-root test sessions сохраняют legacy fallback. Карта учитывает endpoint и descendants; strict directory evidence остаётся отдельной проверкой, поэтому mapped extensionless file сам по себе gap не даёт.

Все20 новых cases и изменения guards написаны до production fix: [RED](task-03-review-round-9-red.md) 29/16/13, расширенный32/16/16, native subset6/0/6. После поведения новые cases не добавлялись. Исторические evidence сохранены.

Версия1.5.18, task/spec/README/status, task04+, посторонний untracked skill не менялись. Commit/push не выполнялись. Exact runtime model ID недоступен. R5-002 за пределами WSL trace не переисследовался; происхождение старого KVM failure остаётся неустановленным, как указал рецензент. Final validation, review, bookkeeping и commit/push принадлежат координатору.
