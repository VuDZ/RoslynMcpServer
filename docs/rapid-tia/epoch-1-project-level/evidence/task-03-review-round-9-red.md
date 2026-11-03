# task-03 — R9-001 RED

Дата: 2026-11-04. База bfbe27c, Windows Release, специализированный Roslyn MCP. Production до этих прогонов не менялся.

Первый каталог: 29 cases, 16 passed, 13 failed. Это все 12 прежних cases и 17 добавленных. Native .cs/directory tests под artifacts/bin сначала подтверждают admission через Session.EventRevision/DirectoryRenamePaths и лишь затем проверяют baseline journal/gap; ожидания не зависят от исправляемого baseline. Mapped obj сценарии выполняют реальное File.Move и явную доставку rename callback; исходные data имеют прошлые mtimes и до операции не находятся в journal.

Git add/commit под обоими ignored ancestors выполняются реально с silenced native watchers, затем доставляется index.lock→index rename. Это изоляция rename-only контракта; существующие Changed/Created Consult.Unknown под такими ancestors не меняются и не объявляются отфильтрованными. Прежние Git guards обычного root сохраняют native watchers.

## Дополнение каталога до production-правки

До изменения поведения добавлены ещё 3 cases: mapped .git input→unmapped destination и два native directory renames под obj с mapped descendants. Усилены явные Known/fresh-journal предусловия. Расширенный результат — 32 выполнено, 16 passed, 16 failed. Отдельный native filter — 6 выполнено, 6 failed на journal/gap assertions после успешного receipt, без delivery timeout. MCP показывает только первые пять failures в каждом отчёте.

## Расширенный RED до production-правки

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineRenameJournalTests`
**Match:** Class name contains `BaselineRenameJournalTests`

❌ 16 Tests Failed.

Total: **32** · Passed: **16** · Failed: **16**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: []
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 159    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 161 --- End of stack trace from previous location ---

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: []
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 159    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 161 --- End of stack trace from previous location --- [xUnit.net 00:00:03.38]       Assert.Contains() Failure: Item not found in collection [xUnit.net 00:00:03.39]       Collection: [] [xUnit.net 00:00:03.39]       Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"··· [xUnit.net 00:00:03.39]       Stack Trace: [xUnit.net 00:00:03.39]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(159,0): at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) [xUnit.net 00:00:03.39]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynM...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: []
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 159    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 161 --- End of stack trace from previous location ---

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: []
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 159    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 161 --- End of stack trace from previous location --- [xUnit.net 00:00:04.75]       Assert.Contains() Failure: Item not found in collection [xUnit.net 00:00:04.75]       Collection: [] [xUnit.net 00:00:04.75]       Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"··· [xUnit.net 00:00:04.75]       Stack Trace: [xUnit.net 00:00:04.75]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(159,0): at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) [xUnit.net 00:00:04.75]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynM...

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: []
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 159    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 161 --- End of stack trace from previous location ---

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

## Отдельный native RED: доставка подтверждена, потеряны journal/gap

## Filtered test run

**Filter:** `FullyQualifiedName~Ignored_ancestor`
**Match:** Caller-supplied VSTest filter

❌ 6 Tests Failed.

Total: **6** · Passed: **0** · Failed: **6**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap(String ancestor, String subdirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 101    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap(String ancestor, String subdirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 101 --- End of stack trace from previous location ---

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap(String ancestor, String subdirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 101    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap(String ancestor, String subdirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 101 --- End of stack trace from previous location ---

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap(String ancestor, String subdirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 101    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap(String ancestor, String subdirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 101 --- End of stack trace from previous location --- [xUnit.net 00:00:03.60]       Assert.True() Failure [xUnit.net 00:00:03.60]       Expected: True [xUnit.net 00:00:03.60]       Actual:   False [xUnit.net 00:00:03.60]       Stack Trace: [xUnit.net 00:00:03.60]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(101,0): at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap(String ancestor, String subdirectory) [xUnit.net 00:00:03.60]         E:\Devel\RoslynMcpServer-rapid-tia\Roslyn...

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap(String ancestor, String subdirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 101    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_directory_rename_is_received_by_native_watcher_and_marks_gap(String ancestor, String subdirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 101 --- End of stack trace from previous location ---

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_source_rename_is_received_by_native_watcher_and_journaled`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: []
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_source_rename_is_received_by_native_watcher_and_journaled(String ancestor) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 71    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_source_rename_is_received_by_native_watcher_and_journaled(String ancestor) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 73 --- End of stack trace from previous location --- [xUnit.net 00:00:04.89]       Assert.Contains() Failure: Item not found in collection [xUnit.net 00:00:04.89]       Collection: [] [xUnit.net 00:00:04.89]       Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"··· [xUnit.net 00:00:04.89]       Stack Trace: [xUnit.net 00:00:04.89]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(71,0): at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Ignored_ancestor_source_rename_is_received_by_native_watcher_and_journaled(String ancestor) [xUnit.net 00:00:04.89]         E:\Devel\RoslynMcpServer-rapid...

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

## Первый RED

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineRenameJournalTests`
**Match:** Class name contains `BaselineRenameJournalTests`

❌ 13 Tests Failed.

Total: **29** · Passed: **16** · Failed: **13**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: []
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 151    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 153 --- End of stack trace from previous location ---

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: []
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 151    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 153 --- End of stack trace from previous location --- [xUnit.net 00:00:03.68]       Assert.Contains() Failure: Item not found in collection [xUnit.net 00:00:03.68]       Collection: [] [xUnit.net 00:00:03.68]       Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"··· [xUnit.net 00:00:03.68]       Stack Trace: [xUnit.net 00:00:03.68]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(151,0): at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) [xUnit.net 00:00:03.68]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynM...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: []
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 151    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 153 --- End of stack trace from previous location ---

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: []
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 151    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 153 --- End of stack trace from previous location --- [xUnit.net 00:00:05.12]       Assert.Contains() Failure: Item not found in collection [xUnit.net 00:00:05.12]       Collection: [] [xUnit.net 00:00:05.12]       Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"··· [xUnit.net 00:00:05.12]       Stack Trace: [xUnit.net 00:00:05.12]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs(151,0): at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) [xUnit.net 00:00:05.12]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynM...

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: []
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 151    at RoslynMcpServer.Tests.Testing.BaselineRenameJournalTests.Mapped_ignored_input_renames_preserve_both_endpoints(String ancestor, String scenario) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineRenameJournalTests.cs:line 153 --- End of stack trace from previous location ---

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
