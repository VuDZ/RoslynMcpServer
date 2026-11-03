# task-03 — RED исправлений раунда 5

Дата: 2026-11-03. Windows Release, Roslyn MCP. Новые и пересмотренные регрессии написаны до исправления поведения; production на этом этапе неизменён.

Первый прогон: 15 выполнено, 1 passed, 14 failed. Два Git-case падения были перекрыты ошибкой cleanup: Git objects read-only на Windows. Это не заявляется RED поведения для этих двух cases; перед повтором fixture data нормализованы до assertions. Детерминированный R5-001 уже падает с FileSystemWatcher did not deliver Program.cs; остальные показанные baseline gaps падают на требованиях.

## R5-002: воспроизведение на Linux и детерминированный future-mtime RED

После исходного Windows RED исследование продолжено на исходной Linux базе31cc8d8. Bare cached-test упал на6-м повторе (5 passed), диагностический — на17-м (16 passed), фазовый — на25-м (24 passed); это focused повторы, не main suite. Cache-hit и generation assertions прошли, All(Known) упал на двух Unknown. [Исходный полный log](task-03-review-round-5-linux/r5-cached-load-06.log), [диагностический](task-03-review-round-5-linux/r5-cached-diagnostic-17.log), [фазовый](task-03-review-round-5-linux/r5-cached-phase-25.log), [manifest](task-03-review-round-5-linux/r5-cached-repeat-manifest.log).

Фазовый trace показывает неизменные исходные mtimes и UTC-время после load раньше UTC до load; mtime оказался позже Published. RequiresFullScan=false, worker failure и watcher Changed/Renamed отсутствуют. Непосредственная причина — корректный timestamp guard, а не cache miss или потеря поколения. Происхождение коррекции часов не устанавливалось. Это не гипотеза о поздней BuildHost записи.

До коррекции cached-test выполнен отдельный детерминированный RED: mtime A/B установлен на5 минут в будущем до load; прежний All(Known) упал, cache/generation assertions прошли. [Полный actual output](task-03-review-round-5-linux/r5-cached-future-red.log), [точный временный patch](task-03-review-round-5-linux/r5-deterministic-future.patch). После этого только тест получил контролируемые прошлые mtimes. Production timestamp guard не ослаблялся; dedicated future timestamp test сохранён.

## Поздний RED: исчезнувший dotted directory с mapped descendants

Добавлен после первого поведения R5-003 при проверке критерия strict descendant. Это всё ещё явно требуемое directory evidence: старая refresh-эвристика отвергает dotted missing path. Тест написан перед дополнительной правкой, фактический 1/1 RED — Assert.True(RequiresFullScan) с False. Следующая правка проводит baseline gap независимо от legacy refresh-эвристики, не меняя её.

## Filtered test run

**Filter:** `FullyQualifiedName~.BaselineRenameJournalTests.A_missing_dotted_directory_with_mapped_descendants_is_a_baseline_gap`
**Match:** Name suffix `.BaselineRenameJournalTests.A_missing_dotted_directory_with_mapped_descendants_is_a_baseline_gap`

❌ 1 Tests Failed.

Total: **1** · Passed: **0** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.A_missing_dotted_directory_with_mapped_descendants_is_a_baseline_gap`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.A_missing_dotted_directory_with_mapped_descendants_is_a_baseline_gap() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 126    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.A_missing_dotted_directory_with_mapped_descendants_is_a_baseline_gap() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 126 --- End of stack trace from previous location --- Total tests: 1      Failed: 1  Total time: 2.2548 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).      1>_VSTestConsole:          MSB4181: The "VSTestTask" task returned false but did not log an error.      1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED. Build FAILED.     0 Warning(s)     0 Error(s) Time Elapsed 00:00:02.74

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.A_missing_dotted_directory_with_mapped_descendants_is_a_baseline_gap`

### Execution context
- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.402` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.402\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.402\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **WorkingDirectory:** `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests`
- **DOTNET_CLI_UI_LANGUAGE:** `en-US`
- **Configuration:** Release
- **Platform:** (SDK/solution default)
- **BuildArgs:** (none)
- **PreTestBuild:** yes (`dotnet build` then `dotnet test --no-build`)
- **TestOutput:** StdOut 100000 chars, StdErr 100000 chars (`includeFullOutput`)

### Build warnings

- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Повтор RED после исправления cleanup, до изменения поведения

15 выполнено, 1 passed, 14 failed; оба Git-case теперь падают на Assert.False(RequiresFullScan), fixture cleanup не перекрывает assertion. После этого уточнён guard Git-журнала: проверяется только .git subtree; bin/obj от поздней сборки остаются допустимыми элементами task-03 журнала и не объявляются дефектом.

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineRenameJournalTests|FullyQualifiedName~Startup_gap_survives_first_publication|FullyQualifiedName~Opt_in_prepublication_gap_is_persistent|FullyQualifiedName~Prepublication_gap_still_rejects|FullyQualifiedName~Pre_read_directory_rename_then_semantic`
**Match:** Caller-supplied VSTest filter

❌ 14 Tests Failed.

Total: **15** · Passed: **1** · Failed: **14**

1. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceInputWatcherDeliveryTests.Pre_read_directory_rename_then_semantic_sync_preserves_bom_and_project_files`
   **Error:** FileSystemWatcher did not deliver Program.cs.
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceInputWatcherDeliveryTests.Semantic_sync_preserves_bom_and_project_files() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceInputWatcherDeliveryTests.cs:line 285    at RoslynMcpServer.Tests.Workspace.WorkspaceInputWatcherDeliveryTests.Pre_read_directory_rename_then_semantic_sync_preserves_bom_and_project_files() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceInputWatcherDeliveryTests.cs:line 274 --- End of stack trace from previous location --- [xUnit.net 00:00:11.17]       Assert.False() Failure [xUnit.net 00:00:11.17]       Expected: False [xUnit.net 00:00:11.17]       Actual:   True [xUnit.net 00:00:11.17]       Stack Trace: [xUnit.net 00:00:11.17]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(35,0): at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths(Boolean commit) [xUnit.net 00:00:11.17]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(36,0): at RoslynMcpServer.Tests.Testing...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 35    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 36 --- End of stack trace from previous location --- [xUnit.net 00:00:12.03]       Assert.False() Failure [xUnit.net 00:00:12.03]       Expected: False [xUnit.net 00:00:12.03]       Actual:   True [xUnit.net 00:00:12.03]       Stack Trace: [xUnit.net 00:00:12.03]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(35,0): at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths(Boolean commit) [xUnit.net 00:00:12.03]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(36,0): at RoslynMcpServer.Tests....

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 35    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 36 --- End of stack trace from previous location ---

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal(String directory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 61    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal(String directory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 63 --- End of stack trace from previous location --- [xUnit.net 00:00:13.32]       Assert.False() Failure [xUnit.net 00:00:13.32]       Expected: False [xUnit.net 00:00:13.32]       Actual:   True [xUnit.net 00:00:13.32]       Stack Trace: [xUnit.net 00:00:13.32]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(61,0): at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal(String directory) [xUnit.net 00:00:13.32]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(63,0): at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename...

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal(String directory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 61    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal(String directory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 63 --- End of stack trace from previous location ---

[!] Showing first 5 failures only.

### Execution context
- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.402` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.402\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.402\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **WorkingDirectory:** `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests`
- **DOTNET_CLI_UI_LANGUAGE:** `en-US`
- **Configuration:** Release
- **Platform:** (SDK/solution default)
- **BuildArgs:** (none)
- **PreTestBuild:** yes (`dotnet build` then `dotnet test --no-build`)
- **TestOutput:** StdOut 100000 chars, StdErr 100000 chars (`includeFullOutput`)

### Build warnings

- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Первый прогон с диагностикой fixture cleanup

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineRenameJournalTests|FullyQualifiedName~Startup_gap_survives_first_publication|FullyQualifiedName~Opt_in_prepublication_gap_is_persistent|FullyQualifiedName~Prepublication_gap_still_rejects|FullyQualifiedName~Pre_read_directory_rename_then_semantic`
**Match:** Caller-supplied VSTest filter

❌ 14 Tests Failed.

Total: **15** · Passed: **1** · Failed: **14**

1. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceInputWatcherDeliveryTests.Pre_read_directory_rename_then_semantic_sync_preserves_bom_and_project_files`
   **Error:** FileSystemWatcher did not deliver Program.cs.
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceInputWatcherDeliveryTests.Semantic_sync_preserves_bom_and_project_files() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceInputWatcherDeliveryTests.cs:line 285    at RoslynMcpServer.Tests.Workspace.WorkspaceInputWatcherDeliveryTests.Pre_read_directory_rename_then_semantic_sync_preserves_bom_and_project_files() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceInputWatcherDeliveryTests.cs:line 274 --- End of stack trace from previous location --- [xUnit.net 00:00:12.24]       System.UnauthorizedAccessException : Access to the path 'b94b9bebd4615e41c51205bdaf128bf992aeaa' is denied. [xUnit.net 00:00:12.24]       Stack Trace: [xUnit.net 00:00:12.24]            at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel) [xUnit.net 00:00:12.24]            at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive) [xUnit.net 00:00:12.24]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineLifecycleFixture.cs(33,0): at RoslynMcpServer.Tests.Testing.BaselineLifecycleFixture.DisposeA...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths`
   **Error:** System.UnauthorizedAccessException : Access to the path 'b94b9bebd4615e41c51205bdaf128bf992aeaa' is denied.
   **Stack:** at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)    at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)    at RoslynMcpServer.Tests.Testing.BaselineLifecycleFixture.DisposeAsync() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineLifecycleFixture.cs:line 33    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 31 --- End of stack trace from previous location --- [xUnit.net 00:00:13.57]       System.UnauthorizedAccessException : Access to the path 'b94b9bebd4615e41c51205bdaf128bf992aeaa' is denied. [xUnit.net 00:00:13.57]       Stack Trace: [xUnit.net 00:00:13.57]            at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel) [xUnit.net 00:00:13.57]            at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive) [xUnit.net 00:00:13.57]         E:\Devel\RoslynMcpServer-rapid-tia\Roslyn...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths`
   **Error:** System.UnauthorizedAccessException : Access to the path 'b94b9bebd4615e41c51205bdaf128bf992aeaa' is denied.
   **Stack:** at System.IO.FileSystem.RemoveDirectoryRecursive(String fullPath, WIN32_FIND_DATA& findData, Boolean topLevel)    at System.IO.FileSystem.RemoveDirectory(String fullPath, Boolean recursive)    at RoslynMcpServer.Tests.Testing.BaselineLifecycleFixture.DisposeAsync() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineLifecycleFixture.cs:line 33    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Real_git_add_and_commit_do_not_invalidate_baseline_or_journal_git_paths(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 31 --- End of stack trace from previous location --- [xUnit.net 00:00:15.01]       Assert.False() Failure [xUnit.net 00:00:15.01]       Expected: False [xUnit.net 00:00:15.01]       Actual:   True [xUnit.net 00:00:15.01]       Stack Trace: [xUnit.net 00:00:15.01]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(56,0): at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal(String directory) [xUnit.net 00:00:15.01]         E:\Devel\RoslynMcpServer-ra...

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal(String directory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 56    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal(String directory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 58 --- End of stack trace from previous location --- [xUnit.net 00:00:15.78]       Assert.False() Failure [xUnit.net 00:00:15.78]       Expected: False [xUnit.net 00:00:15.78]       Actual:   True [xUnit.net 00:00:15.78]       Stack Trace: [xUnit.net 00:00:15.78]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(56,0): at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal(String directory) [xUnit.net 00:00:15.78]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(58,0): at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename...

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal(String directory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 56    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_file_rename_does_not_enter_baseline_journal(String directory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 58 --- End of stack trace from previous location ---

[!] Showing first 5 failures only.

### Execution context
- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.402` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.402\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.402\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **WorkingDirectory:** `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests`
- **DOTNET_CLI_UI_LANGUAGE:** `en-US`
- **Configuration:** Release
- **Platform:** (SDK/solution default)
- **BuildArgs:** (none)
- **PreTestBuild:** yes (`dotnet build` then `dotnet test --no-build`)
- **TestOutput:** StdOut 100000 chars, StdErr 100000 chars (`includeFullOutput`)

### Build warnings

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]
