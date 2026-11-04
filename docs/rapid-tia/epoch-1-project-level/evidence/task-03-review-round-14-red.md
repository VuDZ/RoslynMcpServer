# task-03 — R14-001 RED

Дата: 2026-11-04. База 1b617c4. Windows Release, specialized Roslyn MCP. Все18 cases написаны до production:16 native (Directory.Move/Directory.Delete и работающие watchers),2 deterministic Deleted/Created orders на реальной MSBuild загрузке. Historical mtimes, Known/fresh-journal предусловия сохранены. Native observer пишет реальную последовательность в test output.

Первый прогон:18 выполнено,3 passed,15 failed. Два failures Empty/Unmapped на избыточном no-journal assertion не являются R14 behavioral RED: обычным неизвестным каталогам требуется отсутствие gap. До production assertion ограничен ignored paths; результаты повторного прогона записываются ниже. Ещё один failure — позднее обнаруженная obj Known/Single fixture precondition из-за mixed separators; он также не является behavioral RED. Эти фазы и окончательный canonical obj repeat описаны ниже. Остальные12 failures — отсутствие descendant journal при опущенном gap либо лишняя ignored-directory запись. Рекурсивное удаление уже проходит без gap. MCP выводит первые5 failures; full suite totals фактические.

## Первый actual MCP output до production

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDirectoryDisappearanceTests`
**Match:** Class name contains `BaselineDirectoryDisappearanceTests`

❌ 15 Tests Failed.

Total: **18** · Passed: **3** · Failed: **15**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-9bba9af0e8e048ab8b56aa52d1000f18\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-9bba9af0e8e048ab8b56aa52d1000f18\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 175    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-9bba9af0e8e048ab8b56aa52d1000f18\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-9bba9af0e8e048ab8b56aa52d1000f18\Detached
[xUnit.net 00:00:18.25]       Mapped descendant was omitted without a baseline gap. Native sequence:
[xUnit.net 00:00:18.25]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-e27495d3960f483eae8f2c2479548d87\src\App\Sub
[xUnit.net 00:00:18.25]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-e27495d3960f483eae8f2c2479548d87\Detached
[xUnit.net 00:00:18.25]       Stack Trace:
[xUnit.net 00:00:18.25]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(175,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination)
[xUnit.net 00:00:18.25]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(27,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile)
[xUnit.net 00:00:18.25]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(27,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile)
[xUnit.net 00:00:18.25]         --- End of stack trace from previous location ---
[xUnit.net 00:00:18.25]       Output:
[xUnit.net 00:00:18.25]         Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-e27495d3960f483eae8f2c2479548d87\src\App\Sub
[xUnit.net 00:00:18.25]         Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-e27495d3960f483eae8f2c2479548d87\Detached
```

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-e27495d3960f483eae8f2c2479548d87\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-e27495d3960f483eae8f2c2479548d87\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 175    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-e27495d3960f483eae8f2c2479548d87\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-e27495d3960f483eae8f2c2479548d87\Detached
[xUnit.net 00:00:26.79]       Mapped descendant was omitted without a baseline gap. Native sequence:
[xUnit.net 00:00:26.79]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-31f23ffe6bfb4031a3dd6c55811c5cc1\src\App\Sub
[xUnit.net 00:00:26.79]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-31f23ffe6bfb4031a3dd6c55811c5cc1\Detached
[xUnit.net 00:00:26.79]       Stack Trace:
[xUnit.net 00:00:26.79]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(175,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination)
[xUnit.net 00:00:26.79]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(27,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile)
[xUnit.net 00:00:26.79]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(27,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile)
[xUnit.net 00:00:26.79]         --- End of stack trace from previous location ---
[xUnit.net 00:00:26.79]       Output:
[xUnit.net 00:00:26.79]         Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-31f23ffe6bfb4031a3dd6c55811c5cc1\src\App\Sub
[xUnit.net 00:00:26.79]         Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-31f23ffe6bfb4031a3dd6c55811c5cc1\Detached
```

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-31f23ffe6bfb4031a3dd6c55811c5cc1\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-31f23ffe6bfb4031a3dd6c55811c5cc1\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 175    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-31f23ffe6bfb4031a3dd6c55811c5cc1\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-31f23ffe6bfb4031a3dd6c55811c5cc1\Detached
[xUnit.net 00:00:35.33]       Mapped descendant was omitted without a baseline gap. Native sequence:
[xUnit.net 00:00:35.33]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b4d72476462448c987a025cfe35e7ccc\src\App\Sub
[xUnit.net 00:00:35.33]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b4d72476462448c987a025cfe35e7ccc\Detached
[xUnit.net 00:00:35.33]       Stack Trace:
[xUnit.net 00:00:35.33]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(175,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination)
[xUnit.net 00:00:35.33]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(27,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile)
[xUnit.net 00:00:35.33]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(27,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile)
[xUnit.net 00:00:35.33]         --- End of stack trace from previous location ---
[xUnit.net 00:00:35.33]       Output:
[xUnit.net 00:00:35.33]         Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b4d72476462448c987a025cfe35e7ccc\src\App\Sub
[xUnit.net 00:00:35.33]         Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b4d72476462448c987a025cfe35e7ccc\Detached
```

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b4d72476462448c987a025cfe35e7ccc\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b4d72476462448c987a025cfe35e7ccc\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 175    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b4d72476462448c987a025cfe35e7ccc\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b4d72476462448c987a025cfe35e7ccc\Detached
[xUnit.net 00:00:36.83]       Assert.DoesNotContain() Failure: Item found in collection
[xUnit.net 00:00:36.83]                    ↓ (pos 0)
[xUnit.net 00:00:36.83]       Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
[xUnit.net 00:00:36.83]       Found:      "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
[xUnit.net 00:00:36.83]       Stack Trace:
[xUnit.net 00:00:36.83]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(148,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry(String relative, Boolean payload)
[xUnit.net 00:00:36.83]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(148,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry(String relative, Boolean payload)
[xUnit.net 00:00:36.83]         --- End of stack trace from previous location ---
Passed RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap [502 ms]
Passed RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry(relative: "node_modules/Unmapped", payload: True) [498 ms]
```

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry`
   **Error:**

```text
Assert.DoesNotContain() Failure: Item found in collection
↓ (pos 0)
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
Found:      "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry(String relative, Boolean payload) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 148    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry(String relative, Boolean payload) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 148 --- End of stack trace from previous location --- [xUnit.net 00:00:37.81]       Assert.DoesNotContain() Failure: Item found in collection [xUnit.net 00:00:37.81]                    ↓ (pos 0) [xUnit.net 00:00:37.81]       Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···] [xUnit.net 00:00:37.81]       Found:      "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"··· [xUnit.net 00:00:37.81]       Stack Trace: [xUnit.net 00:00:37.81]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(148,...

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


## Точный native root Error и deterministic routing RED до branch fix

В starter добавлена только diagnostic origin logging после прежнего onError, без изменения routing. Targeted native2/1/1 повтор подтвердил: Linked=True, watcher origin Shared/Sub, directoryExists=False, Win32Exception5 AccessDenied, watcher-error, DirectoryRenamePaths empty. Удалённый собственный point watcher root действительно дал Error после child Deleted; это не overflow и не startup resource hypothesis. Project-subtree case passed. Actual output:

## Filtered test run

**Filter:** `FullyQualifiedName~.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap`
**Match:** Name suffix `.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap`

❌ 1 Tests Failed.

Total: **2** · Passed: **1** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap`
   **Error:**

```text
Linked=True; SourceEventRevision=2; RequiresFullScan=True.
Coverage reasons: imports-not-in-evaluated-graph
restore-inputs-not-in-evaluated-graph
custom-tasks-not-in-evaluated-graph
external-glob-completeness-unknown
xaml-and-resources-beyond-evaluated-documents-unknown
walk-up-file-is-a-candidate-not-a-proven-import
watcher-error
Touched: C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\Shared\Sub\A.cs
Directory renames:
Workspace log: workspace_missing_source_files {"documents":0,"files":0,"available":true,"installed":null,"elapsedMs":0.3249,"paths":[]}
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\src\App
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\src\App\bin\Debug\net10.0
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\src\App\obj\Debug\net10.0\ref
Disk watcher st

...[MIDDLE LOG TRUNCATED]...

sk watcher started on C:\Users\VuDZ\AppData
Disk watcher started on C:\Users\VuDZ
Disk watcher started on C:\Users
Disk watcher started on C:\
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\Shared
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402
Disk watcher started on C:\Program Files\dotnet\sdk
Disk watcher started on C:\Program Files\dotnet
Disk watcher started on C:\Program Files
Loaded Roslyn workspace from C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\All.slnx (Configuration=(default), Platform=(default), PlatformRaw=(default), TargetFramework=(default), BuildArgs=(none))
baseline_capture generation=1 outcome=success
Disk watcher error (buffer overflow or inotify limit). Next semantic call will re-read known documents from disk, not OpenSolutionAsync.
Disk watcher error origin: C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\Shared\Sub; directoryExists=False.
Exceptions: System.ComponentModel.Win32Exception (5): Access is denied.
System.ComponentModel.Win32Exception (5): Access is denied.
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap(Boolean linked) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 129    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap(Boolean linked) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 130 --- End of stack trace from previous location ---
   **StdOut:**

```text
Linked=True; SourceEventRevision=2; RequiresFullScan=True.
Coverage reasons: imports-not-in-evaluated-graph
restore-inputs-not-in-evaluated-graph
custom-tasks-not-in-evaluated-graph
external-glob-completeness-unknown
xaml-and-resources-beyond-evaluated-documents-unknown
walk-up-file-is-a-candidate-not-a-proven-import
watcher-error
Touched: C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\Shared\Sub\A.cs
Directory renames:
Workspace log: workspace_missing_source_files {"documents":0,"files":0,"available":true,"installed":null,"elapsedMs":0.3249,"paths":[]}
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\src\App
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\src\App\bin\Debug\net10.0
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\src\App\obj\Debug\net10.0\ref
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\Shared\Sub
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\src\App\obj\Debug\net10.0
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build\config
Disk watcher started on C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.13\ref\net10.0
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\src
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp
Disk watcher started on C:\Users\VuDZ\AppData\Local
Disk watcher started on C:\Users\VuDZ\AppData
Disk watcher started on C:\Users\VuDZ
Disk watcher started on C:\Users
Disk watcher started on C:\
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\Shared
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402
Disk watcher started on C:\Program Files\dotnet\sdk
Disk watcher started on C:\Program Files\dotnet
Disk watcher started on C:\Program Files
Loaded Roslyn workspace from C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\All.slnx (Configuration=(default), Platform=(default), PlatformRaw=(default), TargetFramework=(default), BuildArgs=(none))
baseline_capture generation=1 outcome=success
Disk watcher error (buffer overflow or inotify limit). Next semantic call will re-read known documents from disk, not OpenSolutionAsync.
Disk watcher error origin: C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85d3d919f98a41d3845c50522ce58218\Shared\Sub; directoryExists=False.
Exceptions: System.ComponentModel.Win32Exception (5): Access is denied.
System.ComponentModel.Win32Exception (5): Access is denied.
Total tests: 2
Passed: 1
Failed: 1
Total time: 3.2088 Seconds
1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).
1>_VSTestConsole:
MSB4181: The "VSTestTask" task returned false but did not log an error.
1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED.
```

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap`

### Execution context
- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.402` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.402\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.402\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **SDK from MSBuild log paths:** `10.0.402`
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
Full report: pass reportCursor=`10b5d3b604784b57b536924c61a4fa97` to this same tool.

До production routing correction написаны8 deterministic rows. Framework protected OnError вызывается через reflection у настоящего FileSystemWatcher, созданного starter, с отключённой native delivery; проверяются реальные зарегистрированные callbacks, не исходный текст. Missing root normal error должен дать Deleted, missing-root overflow всегда gap, existing-root normal/overflow gap, прежние overflow/rename gaps остаются raised, closed session не мутируется. Actual RED8/5/3: три missing-root normal paths без нового gap/с прежним gap не получили Deleted, остальные5 guards passed.

## Filtered test run

**Filter:** `FullyQualifiedName~.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure`
**Match:** Name suffix `.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure`

❌ 3 Tests Failed.

Total: **8** · Passed: **5** · Failed: **3**

1. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure`
   **Error:** Assert.Single() Failure: The collection was empty
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure(Boolean missingRoot, Boolean overflow, String priorGap, Boolean closed) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceDiskWatcherStarterTests.cs:line 274    at InvokeStub_WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)

2. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure`
   **Error:** Assert.Single() Failure: The collection was empty
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure(Boolean missingRoot, Boolean overflow, String priorGap, Boolean closed) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceDiskWatcherStarterTests.cs:line 274    at InvokeStub_WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Passed RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure(missingRoot: True, overflow: True, priorGap: null, closed: True) [1 ms]   Passed RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure(missingRoot: False, overflow: False, priorGap: null, closed: False) [1 ms]   Passed RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherS...

3. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure`
   **Error:**

```text
Assert.Equal() Failure: Values differ
Expected: False
Actual:   True
```
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure(Boolean missingRoot, Boolean overflow, String priorGap, Boolean closed) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceDiskWatcherStarterTests.cs:line 271    at InvokeStub_WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture) Total tests: 8      Passed: 5      Failed: 3  Total time: 0.8269 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).      1>_VSTestConsole:          MSB4181: The "VSTestTask" task returned false but did not log an error.      1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED. Build FAILED.     0 Warning(s)     0 Error(s) Time Elapsed 00:00:01.33

4. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure`

5. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure`

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

После этих actual RED начинается согласованная production correction C# seed eligibility и missing-root error routing. Overflow и настоящие existing-root errors не подавляются, прежний gap никогда не очищается. Diagnostic native settling1s не ослабляет строгий no-gap assertion; deterministic Error rows отдельно доказывают порядок routing без native timing.


## Последний согласованный resource scope и native no-gap диагностика до correctionproduction

[Parent before18 validation](task-03-validation-before-round-18.md): Linux1745/1754,9 прямых inotify128 failures; Windows1761/1762, no-gap native recursive-delete guard raised gap. Оба исхода сохранены, conditional review17 не является приёмкой. Никаких лимитов/глобального параллелизма не менялось.

Согласован точный C# baseline seed scope: recursive membership OR empty roles OR UserInput/Generated/Unknown/PotentialMembership. Baseline.Inventory индексирует project.Documents; builder RoleForDocument(sourceDocument=true) возвращает UserInput/Generated/Unknown. Standalone AdditionalFile/AnalyzerConfig/EvaluationInput/MetadataDependency/Output lifetime не является C# baseline contract; их обычные content watchers сохраняются. Предыдущие3 positive role expectations были избыточным contract, исправлены до production. Добавлен PotentialMembership positive и расширены3 excluded-boundary registration rows; derived C# ancestor protection сохраняется через все excluded roles.

Actual pre-correction RED21/16/5:3 role negatives, realApp count и linked recursive-delete fail;16 retained guards pass. Реальный App13planned/20actual,8SDK AnalyzerConfig guards — actual measured multiplier после pureMeta fix. Native Linked=True: watcher-error, SourceEventRevision2, DirectoryRenamePaths empty, Win32Exception5 AccessDenied. Это не overflow; origin watcher.Directory ещё не был в общем logger, поэтому root disappearance на этой фазе не объявляется доказанной причиной. Дальнейшая diagnostic-only origin logging и повтор описываются ниже. Полный available MCP output:

## Filtered test run

**Filter:** `FullyQualifiedName~WorkspaceDiskWatcherStarterTests.Root_lifetime_guards|FullyQualifiedName~WorkspaceDiskWatcherStarterTests.Eligible_descendant|FullyQualifiedName~BaselineDirectoryDisappearanceTests.Real_project_root_guard_count|FullyQualifiedName~BaselineDirectoryDisappearanceTests.Native_recursive_delete`
**Match:** Caller-supplied VSTest filter

❌ 5 Tests Failed.

Total: **21** · Passed: **16** · Failed: **5**

1. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_csharp_baseline_membership_and_conservative_roles`
   **Error:**

```text
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
```
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_csharp_baseline_membership_and_conservative_roles(String roleNames, Boolean recursive, Boolean expectedGuard) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceDiskWatcherStarterTests.cs:line 165    at InvokeStub_WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_csharp_baseline_membership_and_conservative_roles(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)

2. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_csharp_baseline_membership_and_conservative_roles`
   **Error:**

```text
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
```
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_csharp_baseline_membership_and_conservative_roles(String roleNames, Boolean recursive, Boolean expectedGuard) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceDiskWatcherStarterTests.cs:line 165    at InvokeStub_WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_csharp_baseline_membership_and_conservative_roles(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Passed RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_csharp_baseline_membership_and_conservative_roles(roleNames: "MetadataDependency", recursive: False, expectedGuard: False) [1 ms]   Passed RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_csharp_baseline_membership_and_conservative_roles(roleNames: "PotentialMembership", recursive: False, expectedGuard: True) [1 ms]   Passed RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preser...

3. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_csharp_baseline_membership_and_conservative_roles`
   **Error:**

```text
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
```
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_csharp_baseline_membership_and_conservative_roles(String roleNames, Boolean recursive, Boolean expectedGuard) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceDiskWatcherStarterTests.cs:line 165    at InvokeStub_WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_csharp_baseline_membership_and_conservative_roles(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture) [DEBUG] Using MSBuild from: C:\Program Files\dotnet\sdk\10.0.402 [xUnit.net 00:00:01.94]       Planned descriptors: 13; actual watchers: 20; extra roots: 8. [xUnit.net 00:00:01.94]       Extra: C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build [xUnit.net 00:00:01.94]       C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers [xUnit.net 00:00:01.94]       C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk [xUnit.net 00:00:01.94]       C:\Program Files\dotnet\sdk\10.0.402\Sdks [xUnit.net 00:00:01.94]       C:\...

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Real_project_root_guard_count_excludes_standalone_non_csharp_ancestor_chains`
   **Error:**

```text
Planned descriptors: 13; actual watchers: 20; extra roots: 8.
Extra: C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build
C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers
C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk
C:\Program Files\dotnet\sdk\10.0.402\Sdks
C:\Program Files\dotnet\sdk\10.0.402
C:\Program Files\dotnet\sdk
C:\Program Files\dotnet
C:\Program Files
Unsupported: C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build
C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers
C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk
C:\Program Files\dotnet\sdk\10.0.402\Sdks
C:\Program Files\dotnet\sdk\10.0.402
C:\Program Files\dotnet\sdk
C:\Program Files\dotnet
C:\Program Files
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Real_project_root_guard_count_excludes_standalone_non_csharp_ancestor_chains() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 191    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Real_project_root_guard_count_excludes_standalone_non_csharp_ancestor_chains() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 191 --- End of stack trace from previous location ---
   **StdOut:**

```text
Planned descriptors: 13; actual watchers: 20; extra roots: 8.
Extra: C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build
C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers
C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk
C:\Program Files\dotnet\sdk\10.0.402\Sdks
C:\Program Files\dotnet\sdk\10.0.402
C:\Program Files\dotnet\sdk
C:\Program Files\dotnet
C:\Program Files
Unsupported: C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build
C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers
C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk
C:\Program Files\dotnet\sdk\10.0.402\Sdks
C:\Program Files\dotnet\sdk\10.0.402
C:\Program Files\dotnet\sdk
C:\Program Files\dotnet
C:\Program Files
[xUnit.net 00:00:03.38]       Linked=True; SourceEventRevision=2; RequiresFullScan=True.
[xUnit.net 00:00:03.38]       Coverage reasons: imports-not-in-evaluated-graph
[xUnit.net 00:00:03.38]       restore-inputs-not-in-evaluated-graph
[xUnit.net 00:00:03.38]       custom-tasks-not-in-evaluated-graph
[xUnit.net 00:00:03.38]       external-glob-completeness-unknown
[xUnit.net 00:00:03.38]       xaml-and-resources-beyond-evaluated-documents-unknown
[xUnit.net 00:00:03.38]       walk-up-file-is-a-candidate-not-a-proven-import
[xUnit.net 00:00:03.38]       watcher-error
[xUnit.net 00:00:03.38]       Touched: C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\Shared\Sub\A.cs
[xUnit.net 00:00:03.38]       Directory renames:
[xUnit.net 00:00:03.38]       Workspace log: workspace_missing_source_files {"documents":0,"files":0,"available":true,"installed":null,"elapsedMs":0.342,"paths":[]}
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App\bin\Debug\net10.0
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App\obj\Debug\net10.0\ref
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\Shared\Sub
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App\obj\Debug\net10.0
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build\config
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.13\ref\net10.0
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Users\VuDZ\AppData\Local
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Users\VuDZ\AppData
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Users\VuDZ
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Users
[xUnit.net 00:00:03.38]       Disk watcher started on C:\
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\Shared
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Program Files\dotnet\sdk
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Program Files\dotnet
[xUnit.net 00:00:03.38]       Disk watcher started on C:\Program Files
[xUnit.net 00:00:03.38]       Loaded Roslyn workspace from C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\All.slnx (Configuration=(default), Platform=(default), PlatformRaw=(default), TargetFramework=(default), BuildArgs=(none))
[xUnit.net 00:00:03.38]       baseline_capture generation=1 outcome=success
[xUnit.net 00:00:03.38]       Disk watcher error (buffer overflow or inotify limit). Next semantic call will re-read known documents from disk, not OpenSolutionAsync.
[xUnit.net 00:00:03.38]       Exceptions: System.ComponentModel.Win32Exception (5): Access is denied.
[xUnit.net 00:00:03.38]       Stack Trace:
[xUnit.net 00:00:03.38]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(129,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap(Boolean linked)
[xUnit.net 00:00:03.38]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(130,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap(Boolean linked)
[xUnit.net 00:00:03.38]         --- End of stack trace from previous location ---
[xUnit.net 00:00:03.38]       Output:
[xUnit.net 00:00:03.38]         Linked=True; SourceEventRevision=2; RequiresFullScan=True.
[xUnit.net 00:00:03.38]         Coverage reasons: imports-not-in-evaluated-graph
[xUnit.net 00:00:03.38]         restore-inputs-not-in-evaluated-graph
[xUnit.net 00:00:03.38]         custom-tasks-not-in-evaluated-graph
[xUnit.net 00:00:03.38]         external-glob-completeness-unknown
[xUnit.net 00:00:03.38]         xaml-and-resources-beyond-evaluated-documents-unknown
[xUnit.net 00:00:03.38]         walk-up-file-is-a-candidate-not-a-proven-import
[xUnit.net 00:00:03.38]         watcher-error
[xUnit.net 00:00:03.38]         Touched: C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\Shared\Sub\A.cs
[xUnit.net 00:00:03.38]         Directory renames:
[xUnit.net 00:00:03.38]         Workspace log: workspace_missing_source_files {"documents":0,"files":0,"available":true,"installed":null,"elapsedMs":0.342,"paths":[]}
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App\bin\Debug\net10.0
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App\obj\Debug\net10.0\ref
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\Shared\Sub
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App\obj\Debug\net10.0
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build\config
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.13\ref\net10.0
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Users\VuDZ\AppData\Local
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Users\VuDZ\AppData
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Users\VuDZ
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Users
[xUnit.net 00:00:03.38]         Disk watcher started on C:\
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\Shared
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Program Files\dotnet\sdk
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Program Files\dotnet
[xUnit.net 00:00:03.38]         Disk watcher started on C:\Program Files
[xUnit.net 00:00:03.38]         Loaded Roslyn workspace from C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\All.slnx (Configuration=(default), Platform=(default), PlatformRaw=(default), TargetFramework=(default), BuildArgs=(none))
[xUnit.net 00:00:03.38]         baseline_capture generation=1 outcome=success
[xUnit.net 00:00:03.38]         Disk watcher error (buffer overflow or inotify limit). Next semantic call will re-read known documents from disk, not OpenSolutionAsync.
[xUnit.net 00:00:03.38]         Exceptions: System.ComponentModel.Win32Exception (5): Access is denied.
[xUnit.net 00:00:03.39]   Finished:    RoslynMcpServer.Tests
Passed RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap(linked: False) [720 ms]
```

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap`
   **Error:**

```text
Linked=True; SourceEventRevision=2; RequiresFullScan=True.
Coverage reasons: imports-not-in-evaluated-graph
restore-inputs-not-in-evaluated-graph
custom-tasks-not-in-evaluated-graph
external-glob-completeness-unknown
xaml-and-resources-beyond-evaluated-documents-unknown
walk-up-file-is-a-candidate-not-a-proven-import
watcher-error
Touched: C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\Shared\Sub\A.cs
Directory renames:
Workspace log: workspace_missing_source_files {"documents":0,"files":0,"available":true,"installed":null,"elapsedMs":0.342,"paths":[]}
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App\bin\Debug\net10.0
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App\obj\Debug\net10.0\ref
Disk watcher sta

...[MIDDLE LOG TRUNCATED]...

started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp
Disk watcher started on C:\Users\VuDZ\AppData\Local
Disk watcher started on C:\Users\VuDZ\AppData
Disk watcher started on C:\Users\VuDZ
Disk watcher started on C:\Users
Disk watcher started on C:\
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\Shared
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402
Disk watcher started on C:\Program Files\dotnet\sdk
Disk watcher started on C:\Program Files\dotnet
Disk watcher started on C:\Program Files
Loaded Roslyn workspace from C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\All.slnx (Configuration=(default), Platform=(default), PlatformRaw=(default), TargetFramework=(default), BuildArgs=(none))
baseline_capture generation=1 outcome=success
Disk watcher error (buffer overflow or inotify limit). Next semantic call will re-read known documents from disk, not OpenSolutionAsync.
Exceptions: System.ComponentModel.Win32Exception (5): Access is denied.
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap(Boolean linked) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 129    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap(Boolean linked) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 130 --- End of stack trace from previous location ---
   **StdOut:**

```text
Linked=True; SourceEventRevision=2; RequiresFullScan=True.
Coverage reasons: imports-not-in-evaluated-graph
restore-inputs-not-in-evaluated-graph
custom-tasks-not-in-evaluated-graph
external-glob-completeness-unknown
xaml-and-resources-beyond-evaluated-documents-unknown
walk-up-file-is-a-candidate-not-a-proven-import
watcher-error
Touched: C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\Shared\Sub\A.cs
Directory renames:
Workspace log: workspace_missing_source_files {"documents":0,"files":0,"available":true,"installed":null,"elapsedMs":0.342,"paths":[]}
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App\bin\Debug\net10.0
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App\obj\Debug\net10.0\ref
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\Shared\Sub
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src\App\obj\Debug\net10.0
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build\config
Disk watcher started on C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.13\ref\net10.0
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\src
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp
Disk watcher started on C:\Users\VuDZ\AppData\Local
Disk watcher started on C:\Users\VuDZ\AppData
Disk watcher started on C:\Users\VuDZ
Disk watcher started on C:\Users
Disk watcher started on C:\
Disk watcher started on C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\Shared
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402\Sdks
Disk watcher started on C:\Program Files\dotnet\sdk\10.0.402
Disk watcher started on C:\Program Files\dotnet\sdk
Disk watcher started on C:\Program Files\dotnet
Disk watcher started on C:\Program Files
Loaded Roslyn workspace from C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c75d9545987f45b2a855162810b68691\All.slnx (Configuration=(default), Platform=(default), PlatformRaw=(default), TargetFramework=(default), BuildArgs=(none))
baseline_capture generation=1 outcome=success
Disk watcher error (buffer overflow or inotify limit). Next semantic call will re-read known documents from disk, not OpenSolutionAsync.
Exceptions: System.ComponentModel.Win32Exception (5): Access is denied.
Total tests: 21
Passed: 16
Failed: 5
Total time: 3.8187 Seconds
1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).
1>_VSTestConsole:
MSB4181: The "VSTestTask" task returned false but did not log an error.
1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED.
```

[!] Showing first 5 failures only.

### Execution context
- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.402` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.402\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.402\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **SDK from MSBuild log paths:** `10.0.402`
- **WorkingDirectory:** `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests`
- **DOTNET_CLI_UI_LANGUAGE:** `en-US`
- **Configuration:** Release
- **Platform:** (SDK/solution default)
- **BuildArgs:** (none)
- **PreTestBuild:** yes (`dotnet build` then `dotnet test --no-build`)
- **TestOutput:** StdOut 100000 chars, StdErr 100000 chars (`includeFullOutput`)

### Build warnings

- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]
Full report: pass reportCursor=`20115c9845e544a19e96a7df6a4d1817` to this same tool.


Parent raw outputs/counters/source hashes описанных поздних фаз уже сохранены: [validation-before17](task-03-validation-before-round-17.md), [native boundary RED counters](task-03-r16-boundary-native-red.json), [native source manifest](task-03-r16-boundary-native-red-source-manifest.json), [diagnostic main summary](task-03-r16-timestamp-diagnostic-summary.json), [diagnostic source manifest](task-03-r16-timestamp-diagnostic-source-manifest.json). Эти parent files исполнитель не редактировал. Final553/553 GREEN и передача — [отдельно](task-03-review-round-14-green.md).


## До closure fix: транзитивный pure metadata/output boundary, раунд16

Новая ресурсная eligibility исключила lifetime seed pure metadata/output parent. Chain eligible linked Sub останавливалась на его primary content watcher, предполагая, что сам parent начнёт свою lifetime chain. Для External/Shared(Output)/Sub(UserInput) это неверно. До production closure fix написаны2 starter registration rows (MetadataDependency/Output) и1 real-MSBuild boundary case.

Первая Windows native попытка1/0/1 не является behavioral RED: map pureOutput и sourceKnown preconditions прошли, но Directory.Move самого Shared завершился IOException AccessDenied до события. Точная причина отказа операции не установлена; workaround рискованными filehandle операциями не применялся. Actual output:

## Filtered test run

**Filter:** `FullyQualifiedName~.BaselineDirectoryDisappearanceTests.Native_linked_source_keeps_lifetime_coverage_through_a_pure_output_parent_boundary`
**Match:** Name suffix `.BaselineDirectoryDisappearanceTests.Native_linked_source_keeps_lifetime_coverage_through_a_pure_output_parent_boundary`

❌ 1 Tests Failed.

Total: **1** · Passed: **0** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_source_keeps_lifetime_coverage_through_a_pure_output_parent_boundary`
   **Error:** System.IO.IOException : Access to the path 'C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-cfe88c82a13240d9a2ec55382f65a57a\External\Shared' is denied.
   **Stack:** at System.IO.FileSystem.MoveDirectory(String sourceFullPath, String destFullPath, Boolean _)    at System.IO.FileSystem.MoveDirectory(String sourceFullPath, String destFullPath)    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination, String originalDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 220    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_source_keeps_lifetime_coverage_through_a_pure_output_parent_boundary() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 200    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_source_keeps_lifetime_coverage_through_a_pure_output_parent_boundary() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 200 --- End of stack trace from previous location --- Total tests: 1      Failed: 1  Total time: 3.2226 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMc...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_source_keeps_lifetime_coverage_through_a_pure_output_parent_boundary`

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

Final portable test не skip: Windows проверяет точную registration External parent guard и deterministic Deleted admission, Linux выполняет настоящий Directory.Move Shared с независимым observer. Windows Release MCP RED3/0/3 ниже: два starter rows и real-MSBuild case упали на отсутствующем guard, до production fix.

## Filtered test run

**Filter:** `FullyQualifiedName~WorkspaceDiskWatcherStarterTests.Eligible_descendant|FullyQualifiedName~BaselineDirectoryDisappearanceTests.Linked_source_keeps_lifetime`
**Match:** Caller-supplied VSTest filter

❌ 3 Tests Failed.

Total: **3** · Passed: **0** · Failed: **3**

1. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Eligible_descendant_keeps_ancestor_guard_through_a_pure_metadata_or_output_boundary`
   **Error:**

```text
Assert.Contains() Failure: Filter not matched in collection
Collection: [Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpWa"···, False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpWa"···, False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpWa"···, False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp", False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local", False), ···]
```
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Eligible_descendant_keeps_ancestor_guard_through_a_pure_metadata_or_output_boundary(String boundaryRole) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceDiskWatcherStarterTests.cs:line 199    at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)    at System.Reflection.MethodBaseInvoker.InvokeDirectByRefWithFewArgs(Object obj, Span'1 copyOfArgs, BindingFlags invokeAttr)

2. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Eligible_descendant_keeps_ancestor_guard_through_a_pure_metadata_or_output_boundary`
   **Error:**

```text
Assert.Contains() Failure: Filter not matched in collection
Collection: [Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpWa"···, False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpWa"···, False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpWa"···, False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp", False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local", False), ···]
```
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Eligible_descendant_keeps_ancestor_guard_through_a_pure_metadata_or_output_boundary(String boundaryRole) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceDiskWatcherStarterTests.cs:line 199    at InvokeStub_WorkspaceDiskWatcherStarterTests.Eligible_descendant_keeps_ancestor_guard_through_a_pure_metadata_or_output_boundary(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithOneArg(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture) [xUnit.net 00:00:01.96]       Assert.Contains() Failure: Filter not matched in collection [xUnit.net 00:00:01.96]       Collection: [Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, True), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, False), ···] [xUnit.net 00:00:01.96]       Stack Trace: [xUnit.net 00:00:01.96]         E:\Devel\RoslynMcpServer-rap...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Linked_source_keeps_lifetime_coverage_through_a_pure_output_parent_boundary`
   **Error:**

```text
Assert.Contains() Failure: Filter not matched in collection
Collection: [Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, True), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, False), Tuple ("C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, False), ···]
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Linked_source_keeps_lifetime_coverage_through_a_pure_output_parent_boundary() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 204    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Linked_source_keeps_lifetime_coverage_through_a_pure_output_parent_boundary() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 215 --- End of stack trace from previous location --- Total tests: 3      Failed: 3  Total time: 2.3940 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).      1>_VSTestConsole:          MSB4181: The "VSTestTask" task returned false but did not log an error.      1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED. Build FAILED.     0 Warning(s)     0 Error(s) Time Elapsed 00:00:02.92

4. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Eligible_descendant_keeps_ancestor_guard_through_a_pure_metadata_or_output_boundary`

5. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Eligible_descendant_keeps_ancestor_guard_through_a_pure_metadata_or_output_boundary`

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

Parent до production fix отдельно подтвердил Linux native RED1/0/1 на старом r16 production. Known/map preconditions passed; независимый observer получил Renamed External/Shared -> Detached, затем assertion missing mapped descendant при опущенном gap упала. Это не Windows native proof. Parent сохранит полный log/TRX рядом с validation-before-round-17; исходные paths: artifacts/task-03-r16-boundary-native-red.log и TestResults/task-03-r16-boundary-native-red/boundary-red.trx в Linux r16 validation checkout. Source closure fix начинается только после этого подтверждения.

## Поздняя timestamp диагностика и independent Output delivery race

После pure metadata filter parent Linux main1750/1751 вновь упал в Timestamp_guard_works_without_any_watcher_event. Наличие2 touched paths само по себе не доказывает gap/inotify. Permanent test-only diagnostics добавляют separate logger.Exceptions queue (Messages unchanged), optional fixture logger и phase output с initial/prewrite RequiresFullScan preconditions, coverage/reasons/revisions/events/touched и точными logger exceptions. Original DoesNotContain и timestamp assertions сохранены/усилены. Новые assertions в диагностическом fullLinux1751 run прошли; timestamp test там PASS, причина двух прошлых failures остаётся Unknown и logger не объявляется её исправлением.

Diagnostic full Linux на том же r16 production:1750/1751, единственный failure теперь WorkspaceInputWatcherDeliveryTests.Explicit_obj_input_is_synced_and_generated_outputs_do_not_bump_input_revisions, отсутствующий Output notice. Test ждал AssemblyInfo/Mystery notices, но не Output, который приходит независимо. Этот реальный RED — основание для test-only bounded WaitUntil с прежним exact Output role/!CountsAsInputRevision predicate до прежних assertions и проверки unchanged input revision. Production delivery ради race не меняется. Parent observed sample peak120/128 при0.1s sampling, не exactpeak; global limits/parallelism прежние. Полные parent diagnostic evidence остаются у координатора.


## Поздняя resource correction после parent validation

Первый замороженный537/537 tree прошёл Windows main1746/1746, но parent Linux main дал1736/1738. PluginStartupTests.Help_of_a_loaded_plugin_tool_comes_from_the_start_report получил IOException из JSON config watcher: configured user limit128 on inotify instances or process fd limit. Timestamp_guard_works_without_any_watcher_event также упал на неожиданном touched path. Parent после suite проверил /proc: видимые inotify holders0, max_user_instances128, max_user_watches524288, fd limit10240. Пиковый per-process usage в момент failures не измерен; связь второго failure со startup gap под давлением правдоподобна, но не доказана этим логом.

Дополнительные root-lifetime seeds строились для SDK metadata/output roots и умножали watcher count. Согласован bounded scope correction: исключить только nonrecursive seeds с непустым Roles, где все MetadataDependency/Output; существующие content watchers не меняются. Recursive membership, остальные/empty/mixed roles сохраняются. Это late доработка R14-001 после validation, не новый finding ID; неизвестная KVM-причина прежнего R5-002 сюда не относится.

Перед correctionproduction добавлены13 cases:12 starter count rows (3 exclusions+9 retained guards) и1 real-MSBuild App count. Actual RED13/9/4 ниже:3 excluded-role rows expected0,actual1; все9 retained rows PASS. Реальный App:13planned descriptors,25actual watchers,13 extra roots (один regular descriptor dedup). Неподдержанные4 unique parents под SDK packs и дублирующий shared-parent guard принадлежат metadata chain; AnalyzerConfig chain8 guards сохраняется, поэтому ещё не заявляется окончательное устранение pressure на Linux.

## Filtered test run

**Filter:** `FullyQualifiedName~WorkspaceDiskWatcherStarterTests.Root_lifetime_guards|FullyQualifiedName~BaselineDirectoryDisappearanceTests.Real_project_root_guard_count`
**Match:** Caller-supplied VSTest filter

❌ 4 Tests Failed.

Total: **13** · Passed: **9** · Failed: **4**

1. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles`
   **Error:**

```text
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
```
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles(String roleNames, Boolean recursive, Boolean expectedGuard) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceDiskWatcherStarterTests.cs:line 164    at InvokeStub_WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)

2. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles`
   **Error:**

```text
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
```
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles(String roleNames, Boolean recursive, Boolean expectedGuard) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceDiskWatcherStarterTests.cs:line 164    at InvokeStub_WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)

3. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles`
   **Error:**

```text
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
```
   **Stack:** at RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles(String roleNames, Boolean recursive, Boolean expectedGuard) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WorkspaceDiskWatcherStarterTests.cs:line 164    at InvokeStub_WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles(Object, Span'1)    at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)   Passed RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles(roleNames: "MetadataDependency,UserInput", recursive: False, expectedGuard: True) [1 ms]   Passed RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles(roleNames: "AdditionalFile", recursive: False, expectedGuard: True) [1 ms]   Passed RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles(roleNames: "Generated",...

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Real_project_root_guard_count_excludes_pure_metadata_and_output_ancestor_chains`
   **Error:**

```text
Planned descriptors: 13; actual watchers: 25; extra roots: 13.
Extra: C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build
C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers
C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk
C:\Program Files\dotnet\sdk\10.0.402\Sdks
C:\Program Files\dotnet\sdk\10.0.402
C:\Program Files\dotnet\sdk
C:\Program Files\dotnet
C:\Program Files
C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.13\ref
C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.13
C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref
C:\Program Files\dotnet\packs
C:\Program Files\dotnet
Unsupported: C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.13\ref
C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.13
C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref
C:\Program Files\dotnet\packs
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Real_project_root_guard_count_excludes_pure_metadata_and_output_ancestor_chains() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 176    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Real_project_root_guard_count_excludes_pure_metadata_and_output_ancestor_chains() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 176 --- End of stack trace from previous location ---
   **StdOut:**

```text
Planned descriptors: 13; actual watchers: 25; extra roots: 13.
Extra: C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers\build
C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk\analyzers
C:\Program Files\dotnet\sdk\10.0.402\Sdks\Microsoft.NET.Sdk
C:\Program Files\dotnet\sdk\10.0.402\Sdks
C:\Program Files\dotnet\sdk\10.0.402
C:\Program Files\dotnet\sdk
C:\Program Files\dotnet
C:\Program Files
C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.13\ref
C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.13
C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref
C:\Program Files\dotnet\packs
C:\Program Files\dotnet
Unsupported: C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.13\ref
C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.13
C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref
C:\Program Files\dotnet\packs
Total tests: 13
Passed: 9
Failed: 4
Total time: 2.0609 Seconds
1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).
1>_VSTestConsole:
MSB4181: The "VSTestTask" task returned false but did not log an error.
1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED.
```

5. **TestName:** `RoslynMcpServer.Tests.Workspace.WorkspaceDiskWatcherStarterTests.Root_lifetime_guards_preserve_membership_and_non_metadata_roles`

[!] Showing first 5 failures only.

### Execution context
- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.402` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.402\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.402\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **SDK from MSBuild log paths:** `10.0.402`
- **WorkingDirectory:** `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests`
- **DOTNET_CLI_UI_LANGUAGE:** `en-US`
- **Configuration:** Release
- **Platform:** (SDK/solution default)
- **BuildArgs:** (none)
- **PreTestBuild:** yes (`dotnet build` then `dotnet test --no-build`)
- **TestOutput:** StdOut 100000 chars, StdErr 100000 chars (`includeFullOutput`)

### Build warnings

- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]


## Поздняя коррекция fixture obj path и повторный настоящий RED

Первый GREEN-кандидат после поведения: **145 выполнено,144 passed,1 failed**. Все123 SourceStructure и3 starter guards прошли, strict inventory16 не изменился. Единственный failure — precondition Assert.Single в новом obj scenario: смешанные разделители obj/Sub не совпали с каноническим baseline key. Поэтому этот obj failure в первоначальных18/3/15 и18/5/13 не считается behavioral R14 RED (MCP первые5 его скрыли). Помимо него initial15 failures включали2 избыточных no-journal assertions обычных каталогов; corrected13 включали1 obj fixture failure. Оставшиеся12 failures обоих каталогов behavioral; полный targeted5-failure прогон выше не затронут этой fixture ошибкой.

Same helper получил Path.GetFullPath, source fixture path также канонизирован до assertions/journal comparison. Это late harness correction, не новый behavior case. Для достоверного RED снят только собственный2-file production patch (SolutionManager + WorkspaceDiskWatcherStarter), остальной исходный1b617c4 production не менялся. До повторного применения того же patch выполнены2 targeted прогона obj/artifacts: **2/0/2 каждый**, включая окончательно канонический source path. Ниже оба фактических вывода; окончательный pair подтверждает missing descendant при Known/untouched предусловии и независимом native receipt. Затем patch восстановлен; последующие изменения production только style layout.

### Первый targeted repeat с каноническим Same helper

## Filtered test run

**Filter:** `FullyQualifiedName~.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`
**Match:** Name suffix `.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`

❌ 2 Tests Failed.

Total: **2** · Passed: **0** · Failed: **2**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85bdec2d98aa4759b8497a7c5b1cb5ba\src\App\obj\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85bdec2d98aa4759b8497a7c5b1cb5ba\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 180    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 68    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 68 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85bdec2d98aa4759b8497a7c5b1cb5ba\src\App\obj\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-85bdec2d98aa4759b8497a7c5b1cb5ba\Detached
[xUnit.net 00:00:17.98]       Mapped descendant was omitted without a baseline gap. Native sequence:
[xUnit.net 00:00:17.98]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-be6fe644fb2e422e8f58f2c6876b59e2\artifacts\src\App\Sub
[xUnit.net 00:00:17.98]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-be6fe644fb2e422e8f58f2c6876b59e2\Detached
[xUnit.net 00:00:17.98]       Stack Trace:
[xUnit.net 00:00:17.98]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(180,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination)
[xUnit.net 00:00:17.98]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(68,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment)
[xUnit.net 00:00:17.98]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(68,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment)
[xUnit.net 00:00:17.98]         --- End of stack trace from previous location ---
[xUnit.net 00:00:17.98]       Output:
[xUnit.net 00:00:17.98]         Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-be6fe644fb2e422e8f58f2c6876b59e2\artifacts\src\App\Sub
[xUnit.net 00:00:17.98]         Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-be6fe644fb2e422e8f58f2c6876b59e2\Detached
[xUnit.net 00:00:17.98]   Finished:    RoslynMcpServer.Tests
```

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-be6fe644fb2e422e8f58f2c6876b59e2\artifacts\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-be6fe644fb2e422e8f58f2c6876b59e2\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 180    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 68    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 68 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-be6fe644fb2e422e8f58f2c6876b59e2\artifacts\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-be6fe644fb2e422e8f58f2c6876b59e2\Detached
Total tests: 2
Failed: 2
Total time: 18.2802 Seconds
1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).
1>_VSTestConsole:
MSB4181: The "VSTestTask" task returned false but did not log an error.
1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED.
```

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`

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

### Окончательный targeted repeat с каноническим source path

## Filtered test run

**Filter:** `FullyQualifiedName~.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`
**Match:** Name suffix `.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`

❌ 2 Tests Failed.

Total: **2** · Passed: **0** · Failed: **2**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-828600d236a84e9e99c19064a5cfe83e\src\App\obj\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-828600d236a84e9e99c19064a5cfe83e\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 180    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 68    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 68 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-828600d236a84e9e99c19064a5cfe83e\src\App\obj\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-828600d236a84e9e99c19064a5cfe83e\Detached
[xUnit.net 00:00:17.93]       Mapped descendant was omitted without a baseline gap. Native sequence:
[xUnit.net 00:00:17.93]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-fc55000979984b829ad29ac0fa864e50\artifacts\src\App\Sub
[xUnit.net 00:00:17.93]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-fc55000979984b829ad29ac0fa864e50\Detached
[xUnit.net 00:00:17.93]       Stack Trace:
[xUnit.net 00:00:17.93]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(180,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination)
[xUnit.net 00:00:17.93]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(68,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment)
[xUnit.net 00:00:17.93]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(68,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment)
[xUnit.net 00:00:17.93]         --- End of stack trace from previous location ---
[xUnit.net 00:00:17.93]       Output:
[xUnit.net 00:00:17.93]         Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-fc55000979984b829ad29ac0fa864e50\artifacts\src\App\Sub
[xUnit.net 00:00:17.93]         Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-fc55000979984b829ad29ac0fa864e50\Detached
[xUnit.net 00:00:17.93]   Finished:    RoslynMcpServer.Tests
```

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-fc55000979984b829ad29ac0fa864e50\artifacts\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-fc55000979984b829ad29ac0fa864e50\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 180    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 68    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 68 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-fc55000979984b829ad29ac0fa864e50\artifacts\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-fc55000979984b829ad29ac0fa864e50\Detached
Total tests: 2
Failed: 2
Total time: 18.2241 Seconds
1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).
1>_VSTestConsole:
MSB4181: The "VSTestTask" task returned false but did not log an error.
1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED.
```

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`

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

## Фактический первый GREEN-кандидат с fixture failure

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDirectoryDisappearanceTests|FullyQualifiedName~WorkspaceDiskWatcherStarterTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

❌ 1 Tests Failed.

Total: **145** · Passed: **144** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`
   **Error:**

```text
Assert.Single() Failure: The collection did not contain any matching items
Expected:   (predicate expression)
Collection: [BaselineDocumentState { Key = BaselineDocumentKey { ProjectPath = C:\USERS\VUDZ\APPDATA\LOCAL\TEMP\ROSLYNMCPBASELINE-654F992F4CA847918DB394455D5C5B86\SRC\APP\APP.CSPROJ, ProjectContext = App#0, DocumentPath = C:\USERS\VUDZ\APPDATA\LOCAL\TEMP\ROSLYNMCPBASELINE-654F992F4CA847918DB394455D5C5B86\SRC\APP\OBJ\SUB\A.CS, LogicalPath = obj/Sub/A.cs, Occurrence = 0 }, Status = Known, Snapshot = BaselineDocumentSnapshot { TextChecksum = AEE2602BB2FEA4108316F378A7C6E3914A355C2B447146B57DD35D4308A60B99, ImportsAndNamespacesHash = FBF170D494C944956E3F091E2504B46BA78155732BDF0C61C10A9C58599E006D, GlobalUsingsHash = E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855, AssemblyAndModuleAttributesHash = E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855, DirectivesHash = E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855, HasUnparseableContent = False, Members = System.Collections.Immutable.ImmutableArray`1[RoslynMcpServer.Services.Testing.Baseline.Models.BaselineMemberSnapshot] } }]
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.LoadKnown(BaselineLifecycleFixture fixture, String solution, String source) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 231    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 67    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(String segment) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 68 --- End of stack trace from previous location ---   Passed RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments(segment: "artifacts") [506 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_directory_disappearance_is_observed_with_and_without_a_parent_subscription(parentSubscri...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_disappearance_keeps_mapped_inputs_under_ignored_segments`

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

Итоговый каталог19 cases:17 native и2 deterministic. Дополнительный linked recursive-delete guard добавлен после первоначального RED, но до первой production правки; он прошёл на исходном коде в targeted7/2/5. Новых behavior cases после реализации не добавлялось. Все runtime вызовы исполнителя — Windows Release Roslyn MCP; Linux runtime не запускался, parent final roots не использовались.


## Исправленный guard и фактический RED до production

Повтор всего18-case каталога после ограничения no-journal assertion ignored paths: **18 выполнено,5 passed,13 failed**. Обычные Empty/Unmapped guards теперь проверяют только no-gap; избыточные требования удалены до реализации.

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDirectoryDisappearanceTests`
**Match:** Class name contains `BaselineDirectoryDisappearanceTests`

❌ 13 Tests Failed.

Total: **18** · Passed: **5** · Failed: **13**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-193ee2d580e84831863045faf2212bbf\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-193ee2d580e84831863045faf2212bbf\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 178    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-193ee2d580e84831863045faf2212bbf\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-193ee2d580e84831863045faf2212bbf\Detached
[xUnit.net 00:00:17.88]       Mapped descendant was omitted without a baseline gap. Native sequence:
[xUnit.net 00:00:17.88]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-02343005527b4ba7ae251207b5f60c97\src\App\Sub
[xUnit.net 00:00:17.88]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-02343005527b4ba7ae251207b5f60c97\Detached
[xUnit.net 00:00:17.88]       Stack Trace:
[xUnit.net 00:00:17.88]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(178,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination)
[xUnit.net 00:00:17.88]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(27,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile)
[xUnit.net 00:00:17.88]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(27,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile)
[xUnit.net 00:00:17.88]         --- End of stack trace from previous location ---
[xUnit.net 00:00:17.88]       Output:
[xUnit.net 00:00:17.88]         Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-02343005527b4ba7ae251207b5f60c97\src\App\Sub
[xUnit.net 00:00:17.88]         Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-02343005527b4ba7ae251207b5f60c97\Detached
```

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-02343005527b4ba7ae251207b5f60c97\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-02343005527b4ba7ae251207b5f60c97\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 178    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-02343005527b4ba7ae251207b5f60c97\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-02343005527b4ba7ae251207b5f60c97\Detached
[xUnit.net 00:00:26.40]       Mapped descendant was omitted without a baseline gap. Native sequence:
[xUnit.net 00:00:26.40]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b449282141a04f2d9d81c3f8fce3ecfc\src\App\Sub
[xUnit.net 00:00:26.40]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b449282141a04f2d9d81c3f8fce3ecfc\Detached
[xUnit.net 00:00:26.40]       Stack Trace:
[xUnit.net 00:00:26.40]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(178,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination)
[xUnit.net 00:00:26.40]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(27,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile)
[xUnit.net 00:00:26.40]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(27,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile)
[xUnit.net 00:00:26.40]         --- End of stack trace from previous location ---
[xUnit.net 00:00:26.40]       Output:
[xUnit.net 00:00:26.40]         Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b449282141a04f2d9d81c3f8fce3ecfc\src\App\Sub
[xUnit.net 00:00:26.40]         Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b449282141a04f2d9d81c3f8fce3ecfc\Detached
```

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b449282141a04f2d9d81c3f8fce3ecfc\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b449282141a04f2d9d81c3f8fce3ecfc\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 178    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b449282141a04f2d9d81c3f8fce3ecfc\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-b449282141a04f2d9d81c3f8fce3ecfc\Detached
[xUnit.net 00:00:34.95]       Mapped descendant was omitted without a baseline gap. Native sequence:
[xUnit.net 00:00:34.96]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-efaedce0a18e4bf1a2c1c83ed394f3a6\src\App\Sub
[xUnit.net 00:00:34.96]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-efaedce0a18e4bf1a2c1c83ed394f3a6\Detached
[xUnit.net 00:00:34.96]       Stack Trace:
[xUnit.net 00:00:34.96]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(178,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination)
[xUnit.net 00:00:34.96]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(27,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile)
[xUnit.net 00:00:34.96]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(27,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile)
[xUnit.net 00:00:34.96]         --- End of stack trace from previous location ---
[xUnit.net 00:00:34.96]       Output:
[xUnit.net 00:00:34.96]         Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-efaedce0a18e4bf1a2c1c83ed394f3a6\src\App\Sub
[xUnit.net 00:00:34.96]         Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-efaedce0a18e4bf1a2c1c83ed394f3a6\Detached
```

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-efaedce0a18e4bf1a2c1c83ed394f3a6\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-efaedce0a18e4bf1a2c1c83ed394f3a6\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 178    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_move_outside_the_project_journals_glob_and_explicit_descendants(Boolean nestedSolution, Boolean explicitCompile) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 27 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-efaedce0a18e4bf1a2c1c83ed394f3a6\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-efaedce0a18e4bf1a2c1c83ed394f3a6\Detached
Passed RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap [503 ms]
Passed RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry(relative: "node_modules/Unmapped", payload: True) [513 ms]
[xUnit.net 00:00:36.47]       Assert.DoesNotContain() Failure: Item found in collection
[xUnit.net 00:00:36.47]                    ↓ (pos 0)
[xUnit.net 00:00:36.47]       Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
[xUnit.net 00:00:36.47]       Found:      "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
[xUnit.net 00:00:36.47]       Stack Trace:
[xUnit.net 00:00:36.47]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(150,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry(String relative, Boolean payload)
[xUnit.net 00:00:36.47]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(150,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry(String relative, Boolean payload)
[xUnit.net 00:00:36.47]         --- End of stack trace from previous location ---
```

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry`
   **Error:**

```text
Assert.DoesNotContain() Failure: Item found in collection
↓ (pos 0)
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
Found:      "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry(String relative, Boolean payload) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 150    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry(String relative, Boolean payload) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 150 --- End of stack trace from previous location ---   Passed RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry(relative: ".git/Unmapped", payload: True) [497 ms]   Passed RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_unmapped_directory_disappearance_does_not_create_a_baseline_gap_or_journal_entry(relative: "Unmapped", payload: True) [527 ms] [xUnit.net 00:00:37.99]       Assert.DoesNotContain() Failure: Item found in collection [xUnit.net 00...

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

## Targeted native sequence, linked roots и deterministic orders

До production добавлен ещё один guard: recursive deletion linked directory с собственным point watcher, чтобы root lifetime wiring не создало ложный gap. Targeted прогон7 cases: **7 выполнено,2 passed,5 failed**. Оба recursive-delete guards проходят до исправления. Два deterministic order cases и оба linked-root cases падают на отсутствие descendant; cross-parent native case подтверждает Windows последовательность Deleted исходного каталога, Created Other/Sub, без Renamed. Операции — Directory.Move/Directory.Delete, mv/git mv не запускались. Это наблюдение этой платформы/раскладки, не универсальная гарантия всех платформ.

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDirectoryDisappearanceTests.Native_cross_parent|FullyQualifiedName~BaselineDirectoryDisappearanceTests.Native_linked|FullyQualifiedName~BaselineDirectoryDisappearanceTests.Deleted_and_created|FullyQualifiedName~BaselineDirectoryDisappearanceTests.Native_recursive`
**Match:** Caller-supplied VSTest filter

❌ 5 Tests Failed.

Total: **7** · Passed: **2** · Failed: **5**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Deleted_and_created_directory_callbacks_journal_all_mapped_descendants_in_either_order`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Deleted_and_created_directory_callbacks_journal_all_mapped_descendants_in_either_order(Boolean createFirst) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 97    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Deleted_and_created_directory_callbacks_journal_all_mapped_descendants_in_either_order(Boolean createFirst) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 100 --- End of stack trace from previous location --- [xUnit.net 00:00:01.87]       Assert.Contains() Failure: Item not found in collection [xUnit.net 00:00:01.87]       Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···] [xUnit.net 00:00:01.87]       Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"··· [xUnit.net 00:00:01.87]       Stack Trace: [xUnit.net 00:00:01.87]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(97,0): at RoslynMcpServer.Tests....

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Deleted_and_created_directory_callbacks_journal_all_mapped_descendants_in_either_order`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···, "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Deleted_and_created_directory_callbacks_journal_all_mapped_descendants_in_either_order(Boolean createFirst) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 97    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Deleted_and_created_directory_callbacks_journal_all_mapped_descendants_in_either_order(Boolean createFirst) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 100 --- End of stack trace from previous location --- [xUnit.net 00:00:10.40]       Mapped descendant was omitted without a baseline gap. Native sequence: [xUnit.net 00:00:10.40]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-29d9b77e22854973bef03111cf76b7e4\src\App\Sub [xUnit.net 00:00:10.40]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-29d9b77e22854973bef03111cf76b7e4\src\App\Other\Sub [xUnit.net 00:00:10.40]       Stack Trace: [xUnit.net 00:00:10.40]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.c...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_cross_parent_move_records_the_actual_platform_event_sequence`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-29d9b77e22854973bef03111cf76b7e4\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-29d9b77e22854973bef03111cf76b7e4\src\App\Other\Sub
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 180    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_cross_parent_move_records_the_actual_platform_event_sequence() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 57    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_cross_parent_move_records_the_actual_platform_event_sequence() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 57 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-29d9b77e22854973bef03111cf76b7e4\src\App\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-29d9b77e22854973bef03111cf76b7e4\src\App\Other\Sub
[xUnit.net 00:00:18.94]       Mapped descendant was omitted without a baseline gap. Native sequence:
[xUnit.net 00:00:18.94]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-63facd17650c4e55a1218e03f031c6b0\Shared\Sub
[xUnit.net 00:00:18.94]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-63facd17650c4e55a1218e03f031c6b0\Detached
[xUnit.net 00:00:18.94]       Stack Trace:
[xUnit.net 00:00:18.94]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(180,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination)
[xUnit.net 00:00:18.94]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(46,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_directory_disappearance_is_observed_with_and_without_a_parent_subscription(Boolean parentSubscription)
[xUnit.net 00:00:18.94]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(46,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_directory_disappearance_is_observed_with_and_without_a_parent_subscription(Boolean parentSubscription)
[xUnit.net 00:00:18.94]         --- End of stack trace from previous location ---
[xUnit.net 00:00:18.94]       Output:
[xUnit.net 00:00:18.94]         Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-63facd17650c4e55a1218e03f031c6b0\Shared\Sub
[xUnit.net 00:00:18.94]         Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-63facd17650c4e55a1218e03f031c6b0\Detached
```

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_directory_disappearance_is_observed_with_and_without_a_parent_subscription`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-63facd17650c4e55a1218e03f031c6b0\Shared\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-63facd17650c4e55a1218e03f031c6b0\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 180    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_directory_disappearance_is_observed_with_and_without_a_parent_subscription(Boolean parentSubscription) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 46    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_directory_disappearance_is_observed_with_and_without_a_parent_subscription(Boolean parentSubscription) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 46 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-63facd17650c4e55a1218e03f031c6b0\Shared\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-63facd17650c4e55a1218e03f031c6b0\Detached
[xUnit.net 00:00:27.47]       Mapped descendant was omitted without a baseline gap. Native sequence:
[xUnit.net 00:00:27.47]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-89d5ec79ea7049b29885beab4fa3bde0\Shared\Sub
[xUnit.net 00:00:27.47]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-89d5ec79ea7049b29885beab4fa3bde0\Detached
[xUnit.net 00:00:27.47]       Stack Trace:
[xUnit.net 00:00:27.47]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(180,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination)
[xUnit.net 00:00:27.47]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(46,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_directory_disappearance_is_observed_with_and_without_a_parent_subscription(Boolean parentSubscription)
[xUnit.net 00:00:27.47]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(46,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_directory_disappearance_is_observed_with_and_without_a_parent_subscription(Boolean parentSubscription)
[xUnit.net 00:00:27.47]         --- End of stack trace from previous location ---
[xUnit.net 00:00:27.47]       Output:
[xUnit.net 00:00:27.47]         Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-89d5ec79ea7049b29885beab4fa3bde0\Shared\Sub
[xUnit.net 00:00:27.47]         Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-89d5ec79ea7049b29885beab4fa3bde0\Detached
```

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_directory_disappearance_is_observed_with_and_without_a_parent_subscription`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-89d5ec79ea7049b29885beab4fa3bde0\Shared\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-89d5ec79ea7049b29885beab4fa3bde0\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 180    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_directory_disappearance_is_observed_with_and_without_a_parent_subscription(Boolean parentSubscription) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 46    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_linked_directory_disappearance_is_observed_with_and_without_a_parent_subscription(Boolean parentSubscription) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 46 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-89d5ec79ea7049b29885beab4fa3bde0\Shared\Sub
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-89d5ec79ea7049b29885beab4fa3bde0\Detached
[xUnit.net 00:00:28.51]   Finished:    RoslynMcpServer.Tests
Passed RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap(linked: False) [518 ms]
Passed RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Native_recursive_delete_journals_the_document_without_a_gap(linked: True) [511 ms]
Total tests: 7
Passed: 2
Failed: 5
Total time: 28.8098 Seconds
1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).
1>_VSTestConsole:
MSB4181: The "VSTestTask" task returned false but did not log an error.
1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED.
```

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
