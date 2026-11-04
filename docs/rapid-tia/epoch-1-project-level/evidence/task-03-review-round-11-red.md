# task-03 — R11-001 RED

Дата: 2026-11-04. База 6bedfcf. Windows Release, специализированный Roslyn MCP. Все новые regressions написаны до production fix.

20 выполнено,10 passed,10 failed:9 native cases и11 pure path guards. Native layout — build/All.slnx с src/App или проектом в корне. DirectoryRenamePaths/EventRevision подтверждают receipt до assertions baseline; watchers не подавляются, Notify injection не используется. Git add/commit выполняются реально; obj/node_modules/.git directory scenarios — Directory.Move, не заявляются npm install или Git rebase.

Эта первоначальная сводка не выдаётся целиком за behavioral RED: первоначальная cross-parent строка отдельно дала delivery timeout, сохранённый ниже. Final same-parent fixture подтверждена отдельным pre-production 2/0/2 на baseline assertions; timeout не засчитывается доказательством дефекта продукта.

Pure guards проверяют общий префикс, sibling boundary, root itself, Windows drive/case и UNC shares. UNC строки проверяются без сетевого и файлового доступа. Три native source/mapped guards сохраняют Known/fresh-journal предусловия.

## Исправленные native directory rows: настоящий behavioral RED до production fix

Повтор двух final rows (obj/foo→obj/bar и node_modules/.staging/pkg→pkg2): **2 выполнено,0 passed,2 failed** на Assert.False(RequiresFullScan), после успешного native receipt. Production ещё не менялся. В совокупности с остальными native failures исходного прогона это закрепляет все требуемые negative сценарии; первоначальный cross-parent timeout не выдаётся за behavioral RED.

## Filtered test run

**Filter:** `FullyQualifiedName~.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths`
**Match:** Name suffix `.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths`

❌ 2 Tests Failed.

Total: **2** · Passed: **0** · Failed: **2**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths(String oldName, String newName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 29    at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths(String oldName, String newName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 31 --- End of stack trace from previous location ---

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths(String oldName, String newName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 29    at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths(String oldName, String newName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 31 --- End of stack trace from previous location --- Total tests: 2      Failed: 2  Total time: 2.4760 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).      1>_VSTestConsole:          MSB4181: The "VSTestTask" task returned false but did not log an error.      1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED. Build FAILED.     0 Warning(s)     0 Error(s) Time Elapsed 00:00:02.87

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths`

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths`

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


## Поздний RED: точный Windows-only inventory

После первоначального focused GREEN511/511 и review12 координатор выполнил полные main suites. Linux: **1719 выполнено,1718 passed,1 failed,0 skipped**; Windows: **1727 выполнено,1726 passed,1 failed**. Единственное падение на обеих платформах — Windows_only_category_matches_the_reviewed_list. Production/native failures не обнаружены. [Полная parent validation до раунда13](task-03-validation-before-round-13.md) сохранена отдельно и не переписывается.

Первоначальный focused filter не включал PlatformSupport inventory и не доказывал его прохождение. Новый RequiresWindows метод с четырьмя pure path строками отсутствовал в точном reviewed списке. Его фактическая identity — WorkspaceDiskPathFilterTests.Workspace_filter_respects_windows_roots_without_filesystem_access. Отдельный искусственный RED не создавался: реальные main failures являются RED этой поздней inventory доработки. Категория метода корректна; production behavior не меняется.

### Полный прочитанный Linux failure output

```text
Test run for /home/runner/roslyn-mcp-rapid-tia-r11-final-20261104/RoslynMcpServer.Tests/bin/Release/net10.0/RoslynMcpServer.Tests.dll (.NETCoreApp,Version=v10.0)
VSTest version 18.0.2 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:09.40]     RoslynMcpServer.Tests.PlatformSupport.WindowsOnlyTestInventoryTests.Windows_only_category_matches_the_reviewed_list [FAIL]
  Failed RoslynMcpServer.Tests.PlatformSupport.WindowsOnlyTestInventoryTests.Windows_only_category_matches_the_reviewed_list [60 ms]
  Error Message:
   Assert.Equal() Failure: Collections differ
                                                                                                                             ↓ (pos 2)
Expected: ["DotNetHostResolverTests.ResolveDotNetExecutable_on"···, "HarnessSessionIdTests.Windows_reads_this_process_i"···, "WpfTemporaryProjectWatchingTests.Real_WPF_build_pr"···, "WpfTemporaryProjectWatchingTests.Real_project_edit"···]
Actual:   ["DotNetHostResolverTests.ResolveDotNetExecutable_on"···, "HarnessSessionIdTests.Windows_reads_this_process_i"···, "WorkspaceDiskPathFilterTests.Workspace_filter_resp"···, "WpfTemporaryProjectWatchingTests.Real_WPF_build_pr"···, "WpfTemporaryProjectWatchingTests.Real_project_edit"···]
                                                                                                                             ↑ (pos 2)
  Stack Trace:
     at RoslynMcpServer.Tests.PlatformSupport.WindowsOnlyTestInventoryTests.Windows_only_category_matches_the_reviewed_list() in /home/runner/roslyn-mcp-rapid-tia-r11-final-20261104/RoslynMcpServer.Tests/PlatformSupport/WindowsOnlyTestInventoryTests.cs:line 16
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
Results File: /home/runner/roslyn-mcp-rapid-tia-r11-final-20261104/TestResults/task-03-r12-linux-1793752419672-1/main.trx

Failed!  - Failed:     1, Passed:  1718, Skipped:     0, Total:  1719, Duration: 1 m 26 s - RoslynMcpServer.Tests.dll (net10.0)
```


## Уточнение native fixture до production fix

Первый каталог включал cross-parent Directory.Move node_modules/.staging/pkg→node_modules/pkg. В отдельном native повторе эта строка дала timeout при ожидании Renamed receipt; точная причина отсутствия этого receipt не устанавливалась. Такой failure не считается behavioral RED. Строка заменена на same-parent node_modules/.staging/pkg→pkg2, чтобы реальный native Renamed проверял тот же ignored segment. Это не npm operation и не production workaround. Отдельный фактический native output с timeout сохранён ниже; последующий targeted RED фиксирует исправленную fixture.

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineNestedSolutionRenameTests`
**Match:** Class name contains `BaselineNestedSolutionRenameTests`

❌ 6 Tests Failed.

Total: **9** · Passed: **3** · Failed: **6**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_obj_file_native_rename_does_not_enter_journal`
   **Error:**

```text
Assert.DoesNotContain() Failure: Item found in collection
↓ (pos 1)
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
Found:      "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_obj_file_native_rename_does_not_enter_journal() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 52    at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_obj_file_native_rename_does_not_enter_journal() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 53 --- End of stack trace from previous location --- [xUnit.net 00:00:01.89]       Assert.False() Failure [xUnit.net 00:00:01.89]       Expected: False [xUnit.net 00:00:01.89]       Actual:   True [xUnit.net 00:00:01.89]       Stack Trace: [xUnit.net 00:00:01.89]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs(105,0): at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_directory_native_move_does_not_mark_gap_or_journal_paths() [xUnit.net 00:00:01.89]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs(107,0): at RoslynMcpServer.Tests.Testing.BaselineNestedSolution...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_directory_native_move_does_not_mark_gap_or_journal_paths`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_directory_native_move_does_not_mark_gap_or_journal_paths() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 105    at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_directory_native_move_does_not_mark_gap_or_journal_paths() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 107 --- End of stack trace from previous location ---   Passed RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_explicit_obj_input_native_rename_preserves_both_journal_paths [513 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_source_directory_native_rename_marks_gap [509 ms] [xUnit.net 00:00:04.17]       Assert.DoesNotContain() Failure: Filter matched in collection [xUnit.net 00:00:04.17]                    ↓ (pos 0) [xUnit.net 00:00:04.17]       Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMc...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_add_and_commit_native_renames_do_not_enter_journal`
   **Error:**

```text
Assert.DoesNotContain() Failure: Filter matched in collection
↓ (pos 0)
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, ···]
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_add_and_commit_native_renames_do_not_enter_journal(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 83    at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_add_and_commit_native_renames_do_not_enter_journal(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 83 --- End of stack trace from previous location --- [xUnit.net 00:00:04.81]       Assert.DoesNotContain() Failure: Filter matched in collection [xUnit.net 00:00:04.81]                    ↓ (pos 0) [xUnit.net 00:00:04.81]       Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, ···] [xUnit.net 00:00:04.81]       Stack Trace: [xUnit.net 00:00:04.81]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\B...

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_add_and_commit_native_renames_do_not_enter_journal`
   **Error:**

```text
Assert.DoesNotContain() Failure: Filter matched in collection
↓ (pos 0)
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, ···]
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_add_and_commit_native_renames_do_not_enter_journal(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 83    at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_add_and_commit_native_renames_do_not_enter_journal(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 83 --- End of stack trace from previous location ---

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths(String oldName, String newName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 29    at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths(String oldName, String newName) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 31 --- End of stack trace from previous location --- [xUnit.net 00:00:35.82]       Native watcher did not admit the ignored directory rename. [xUnit.net 00:00:35.82]       Stack Trace: [xUnit.net 00:00:35.82]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs(26,0): at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_ignored_directory_native_rename_does_not_mark_gap_or_journal_paths(String oldName, String newName) [xUnit.net 00:00:35.82]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSoluti...

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
- **PreTestBuild:** skipped (`noBuild=true`)
- **TestOutput:** StdOut 100000 chars, StdErr 100000 chars (`includeFullOutput`)

## Общий RED до изменения поведения

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineNestedSolutionRenameTests|FullyQualifiedName~Workspace_filter_`
**Match:** Caller-supplied VSTest filter

❌ 10 Tests Failed.

Total: **20** · Passed: **10** · Failed: **10**

1. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix`
   **Error:**

```text
Assert.Equal() Failure: Values differ
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix(String directory, String path, Boolean expected) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceDiskPathFilterTests.cs:line 69    at InvokeStub_WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Passed RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix(directory: "artifacts/Repo/build", path: "artifacts/Repo/build/obj/index", expected: True) [< 1 ms]   Passed RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix(directory: "bin/Repo", path: "bin/Repo/src/A.cs", expected: False) [< 1 ms]   Passed RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.Workspace_filter_checks_segments_after_the_common_prefix(directory: "artifacts/Repo", path: "artifacts/Repo", expected: False) [< 1 ms]   ...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_obj_file_native_rename_does_not_enter_journal`
   **Error:**

```text
Assert.DoesNotContain() Failure: Item found in collection
↓ (pos 1)
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
Found:      "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_obj_file_native_rename_does_not_enter_journal() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 52    at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_obj_file_native_rename_does_not_enter_journal() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 53 --- End of stack trace from previous location ---

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_directory_native_move_does_not_mark_gap_or_journal_paths`
   **Error:**

```text
Assert.False() Failure
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_directory_native_move_does_not_mark_gap_or_journal_paths() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 105    at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_directory_native_move_does_not_mark_gap_or_journal_paths() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 107 --- End of stack trace from previous location ---   Passed RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_explicit_obj_input_native_rename_preserves_both_journal_paths [707 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_source_directory_native_rename_marks_gap [759 ms] [xUnit.net 00:00:06.36]       Assert.DoesNotContain() Failure: Filter matched in collection [xUnit.net 00:00:06.36]                    ↓ (pos 0) [xUnit.net 00:00:06.36]       Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMc...

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_add_and_commit_native_renames_do_not_enter_journal`
   **Error:**

```text
Assert.DoesNotContain() Failure: Filter matched in collection
↓ (pos 0)
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, ···]
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_add_and_commit_native_renames_do_not_enter_journal(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 83    at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_add_and_commit_native_renames_do_not_enter_journal(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 83 --- End of stack trace from previous location --- [xUnit.net 00:00:07.26]       Assert.DoesNotContain() Failure: Filter matched in collection [xUnit.net 00:00:07.26]                    ↓ (pos 0) [xUnit.net 00:00:07.26]       Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, ···] [xUnit.net 00:00:07.26]       Stack Trace: [xUnit.net 00:00:07.26]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\B...

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_add_and_commit_native_renames_do_not_enter_journal`
   **Error:**

```text
Assert.DoesNotContain() Failure: Filter matched in collection
↓ (pos 0)
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, ···]
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_add_and_commit_native_renames_do_not_enter_journal(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 83    at RoslynMcpServer.Tests.Testing.BaselineNestedSolutionRenameTests.Sibling_git_add_and_commit_native_renames_do_not_enter_journal(Boolean commit) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineNestedSolutionRenameTests.cs:line 83 --- End of stack trace from previous location ---

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
