# task-03 — review18 optional exact entry RED

Дата:2026-11-04, база1b617c4, прежний исполнитель. Scope только refinement R14-001 из [review18](task-03-review-round-18.md). Production shortcut ещё map.Find(path)isnull во всех приведённых RED.

До production написаны4 real-MSBuild cases: Directory.Build.props/targets как физические каталоги внутри project App, Known source под ними; exact candidate entry Present=false, Directory.Exists=true, File.Exists=false. Native Directory.Move за пределы App recursive subtree подтверждается независимым observer; Windows фактически передал Deleted исходного каталога + Created Detached, безRenamed. Два deterministic cases отключают native delivery и вызывают Deleted после физического move. Actual4/0/4 ниже — все failures на отсутствующем C# descendant journal при опущенном gap, не на фикстуре/доставке.

## Initial actual4/0/4

## Filtered test run

**Filter:** `FullyQualifiedName~.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`
**Match:** Name suffix `.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`

❌ 4 Tests Failed.

Total: **4** · Passed: **0** · Failed: **4**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-45b21dbf22ca4bf8b527cbb08b974511\src\App\Directory.Build.props
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-45b21dbf22ca4bf8b527cbb08b974511\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination, String originalDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 305    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 261    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 273 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-45b21dbf22ca4bf8b527cbb08b974511\src\App\Directory.Build.props
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-45b21dbf22ca4bf8b527cbb08b974511\Detached
[xUnit.net 00:00:11.29]       Assert.Contains() Failure: Item not found in collection
[xUnit.net 00:00:11.29]       Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
[xUnit.net 00:00:11.29]       Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
[xUnit.net 00:00:11.29]       Stack Trace:
[xUnit.net 00:00:11.29]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(273,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native)
[xUnit.net 00:00:11.29]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(273,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native)
[xUnit.net 00:00:11.29]         --- End of stack trace from previous location ---
```

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 273    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 273 --- End of stack trace from previous location --- [xUnit.net 00:00:19.99]       Mapped descendant was omitted without a baseline gap. Native sequence: [xUnit.net 00:00:19.99]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-94bde6b991bc4db09bb79283890fa209\src\App\Directory.Build.targets [xUnit.net 00:00:19.99]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-94bde6b991bc4db09bb79283890fa209\Detached [xUnit.net 00:00:19.99]       Stack Trace: [xUnit.net 00:00:19.99]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirec...

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-94bde6b991bc4db09bb79283890fa209\src\App\Directory.Build.targets
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-94bde6b991bc4db09bb79283890fa209\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination, String originalDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 305    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 261    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 273 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-94bde6b991bc4db09bb79283890fa209\src\App\Directory.Build.targets
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-94bde6b991bc4db09bb79283890fa209\Detached
[xUnit.net 00:00:20.71]       Assert.Contains() Failure: Item not found in collection
[xUnit.net 00:00:20.71]       Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
[xUnit.net 00:00:20.71]       Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
[xUnit.net 00:00:20.71]       Stack Trace:
[xUnit.net 00:00:20.71]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(273,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native)
[xUnit.net 00:00:20.71]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(273,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native)
[xUnit.net 00:00:20.71]         --- End of stack trace from previous location ---
[xUnit.net 00:00:20.72]   Finished:    RoslynMcpServer.Tests
```

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 273    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 273 --- End of stack trace from previous location --- Total tests: 4      Failed: 4  Total time: 21.1062 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).      1>_VSTestConsole:          MSB4181: The "VSTestTask" task returned false but did not log an error.      1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED. Build FAILED.     0 Warning(s)     0 Error(s) Time Elapsed 00:00:21.61

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`

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

## Nested optional candidate guard до production

Перед production добавлен5-й deterministic case с двумя реальными MSBuild projects и вложенным optional Directory.Build.targets directory. Outer source и nested source должны получить по одному EventRevision за один admitted Deleted; outer enumeration уже покрывает весь subtree, поэтому nested synthetic calls не должны снова запускать map scan. Final5/0/5 ниже: прежние4 rows повторены, новыйnestedcase также genuine missingjournalRED. Ни одного case послеproduction не добавлено. Это regression для bounded expansion, без исходнотекстовыхassertions и без benchmark framework.

## Filtered test run

**Filter:** `FullyQualifiedName~.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`
**Match:** Name suffix `.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`

❌ 5 Tests Failed.

Total: **5** · Passed: **0** · Failed: **5**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 298    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 303 --- End of stack trace from previous location --- [xUnit.net 00:00:10.50]       Mapped descendant was omitted without a baseline gap. Native sequence: [xUnit.net 00:00:10.50]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-39922e6a9b10467e88d597b6cd299f51\src\App\Directory.Build.props [xUnit.net 00:00:10.50]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-39922e6a9b10467e88d597b6cd299f51\Detached [xUnit.net 00:00:10.50]       Stack Trace: [xUnit.net 00:00:10.50]         E:\Devel\RoslynMcpServer-rapid...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-39922e6a9b10467e88d597b6cd299f51\src\App\Directory.Build.props
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-39922e6a9b10467e88d597b6cd299f51\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination, String originalDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 336    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 284    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 303 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-39922e6a9b10467e88d597b6cd299f51\src\App\Directory.Build.props
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-39922e6a9b10467e88d597b6cd299f51\Detached
[xUnit.net 00:00:11.29]       Assert.Contains() Failure: Item not found in collection
[xUnit.net 00:00:11.29]       Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
[xUnit.net 00:00:11.29]       Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
[xUnit.net 00:00:11.29]       Stack Trace:
[xUnit.net 00:00:11.29]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(298,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate)
[xUnit.net 00:00:11.29]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(303,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate)
[xUnit.net 00:00:11.29]         --- End of stack trace from previous location ---
```

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 298    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 303 --- End of stack trace from previous location --- [xUnit.net 00:00:19.98]       Mapped descendant was omitted without a baseline gap. Native sequence: [xUnit.net 00:00:19.98]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-486b9418b77b46afb19c047fbf2a84ae\src\App\Directory.Build.targets [xUnit.net 00:00:19.98]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-486b9418b77b46afb19c047fbf2a84ae\Detached [xUnit.net 00:00:19.98]       Stack Trace: [xUnit.net 00:00:19.98]         E:\Devel\RoslynMcpServer-rap...

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-486b9418b77b46afb19c047fbf2a84ae\src\App\Directory.Build.targets
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-486b9418b77b46afb19c047fbf2a84ae\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination, String originalDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 336    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 284    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 303 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-486b9418b77b46afb19c047fbf2a84ae\src\App\Directory.Build.targets
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-486b9418b77b46afb19c047fbf2a84ae\Detached
[xUnit.net 00:00:20.68]       Assert.Contains() Failure: Item not found in collection
[xUnit.net 00:00:20.68]       Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
[xUnit.net 00:00:20.68]       Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
[xUnit.net 00:00:20.68]       Stack Trace:
[xUnit.net 00:00:20.68]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(298,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate)
[xUnit.net 00:00:20.68]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(303,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate)
[xUnit.net 00:00:20.68]         --- End of stack trace from previous location ---
[xUnit.net 00:00:20.68]   Finished:    RoslynMcpServer.Tests
```

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 298    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Optional_exact_candidate_directory_does_not_hide_deleted_mapped_descendants(String candidateName, Boolean native, Boolean nestedCandidate) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 303 --- End of stack trace from previous location --- Total tests: 5      Failed: 5  Total time: 21.0595 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).      1>_VSTestConsole:          MSB4181: The "VSTestTask" task returned false but did not log an error.      1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED. Build FAILED.     0 Warning(s)     0 Erro...

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

Plannedminimalfix послеRED: skip expansion только для exact Present=true file; strict self exclusion; private synthetic-call expansion flag false, чтобы missing optional descendants не запускали повторный tree scan. API/activation/version/task04 не меняются.
