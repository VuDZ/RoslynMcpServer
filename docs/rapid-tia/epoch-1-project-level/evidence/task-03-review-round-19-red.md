# task-03 — review19 WPF prefilter RED

Дата:2026-11-04, база1b617c4, прежний исполнитель. Scope refinement R14-001 из review19. Все8 новых cases написаны до production; IDE edit отсутствует, разрешён local file fallback. Изменения только existing internal baseline/watcher flow, без task04/public activation.

## Actual RED8/2/6

На реальной MSBuild загрузке подтверждён Known Compile Item_wpftmp.csproj и mapped occurrence.DocumentId. Baseline.Inventory не фильтрует filename extension. Пять rows покрывают WPF-named directory + ordinary source, ordinary directory + WPF-named source, combo;2native +3deterministic. Direct mapped WPF-named file deletion — ещё1fact, требующий baseline journal при прежнем semantic WPF suppression. Два guard rows удаляют настоящий unmapped temporary XML project file с map и без map, suppression должна сохраниться.

Actual8/2/6 до production: оба настоящих temporary-file guards уже PASS, все6 mapped cases FAIL на missing source journal. Known/mapped preconditions прошли, это не harness failure. Два native Directory.Move дали Windows Deleted original + Created Detached при независимом observer; gap опущен, mapped descendant потерян. MCP cap первых5 скрывает полную direct-file карточку, но его failure/stack присутствуют в captured stdout шестого failure. Никакие cases после production не добавлялись.

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineDirectoryDisappearanceTests.Wpf_named|FullyQualifiedName~BaselineDirectoryDisappearanceTests.Deleted_wpf_named|FullyQualifiedName~BaselineDirectoryDisappearanceTests.Actual_unmapped_wpf`
**Match:** Caller-supplied VSTest filter

❌ 6 Tests Failed.

Total: **8** · Passed: **2** · Failed: **6**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: []
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost(String directoryName, String sourceName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 339    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost(String directoryName, String sourceName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 339 --- End of stack trace from previous location --- [xUnit.net 00:00:02.99]       Assert.Contains() Failure: Item not found in collection [xUnit.net 00:00:02.99]       Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···] [xUnit.net 00:00:02.99]       Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"··· [xUnit.net 00:00:02.99]       Stack Trace: [xUnit.net 00:00:02.99]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(339,0): at RoslynMcpServer.Tests.Testing.BaselineDirectory...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: ["C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···]
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost(String directoryName, String sourceName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 339    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost(String directoryName, String sourceName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 339 --- End of stack trace from previous location ---

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost`
   **Error:**

```text
Assert.Contains() Failure: Item not found in collection
Collection: []
Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost(String directoryName, String sourceName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 339    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost(String directoryName, String sourceName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 339 --- End of stack trace from previous location --- [xUnit.net 00:00:12.37]       Mapped descendant was omitted without a baseline gap. Native sequence: [xUnit.net 00:00:12.37]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-754ba8155e5c48e784c68fef4a4c75de\src\App\Scratch_wpftmp.csproj [xUnit.net 00:00:12.37]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-754ba8155e5c48e784c68fef4a4c75de\Detached [xUnit.net 00:00:12.37]       Stack Trace: [xUnit.net 00:00:12.37]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testi...

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-754ba8155e5c48e784c68fef4a4c75de\src\App\Scratch_wpftmp.csproj
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-754ba8155e5c48e784c68fef4a4c75de\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination, String originalDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 439    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost(String directoryName, String sourceName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 327    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost(String directoryName, String sourceName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 339 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-754ba8155e5c48e784c68fef4a4c75de\src\App\Scratch_wpftmp.csproj
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-754ba8155e5c48e784c68fef4a4c75de\Detached
[xUnit.net 00:00:21.02]       Mapped descendant was omitted without a baseline gap. Native sequence:
[xUnit.net 00:00:21.02]       Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c7d10dcc608b4411a731e735891c3205\src\App\Scratch
[xUnit.net 00:00:21.02]       Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c7d10dcc608b4411a731e735891c3205\Detached
[xUnit.net 00:00:21.02]       Stack Trace:
[xUnit.net 00:00:21.02]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(439,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination, String originalDirectory)
[xUnit.net 00:00:21.02]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(327,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost(String directoryName, String sourceName, Boolean native)
[xUnit.net 00:00:21.02]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(339,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost(String directoryName, String sourceName, Boolean native)
[xUnit.net 00:00:21.02]         --- End of stack trace from previous location ---
[xUnit.net 00:00:21.02]       Output:
[xUnit.net 00:00:21.02]         Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c7d10dcc608b4411a731e735891c3205\src\App\Scratch
[xUnit.net 00:00:21.02]         Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c7d10dcc608b4411a731e735891c3205\Detached
```

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost`
   **Error:**

```text
Mapped descendant was omitted without a baseline gap. Native sequence:
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c7d10dcc608b4411a731e735891c3205\src\App\Scratch
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c7d10dcc608b4411a731e735891c3205\Detached
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.MoveAndAssert(BaselineLifecycleFixture fixture, String source, String destination, String originalDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 439    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost(String directoryName, String sourceName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 327    at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Wpf_named_directories_and_mapped_compile_descendants_are_not_lost(String directoryName, String sourceName, Boolean native) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs:line 339 --- End of stack trace from previous location ---
   **StdOut:**

```text
Deleted C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c7d10dcc608b4411a731e735891c3205\src\App\Scratch
Created C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpBaseline-c7d10dcc608b4411a731e735891c3205\Detached
Passed RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Actual_unmapped_wpf_temporary_file_keeps_baseline_and_semantic_suppression(mapless: True) [648 ms]
Passed RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Actual_unmapped_wpf_temporary_file_keeps_baseline_and_semantic_suppression(mapless: False) [632 ms]
[xUnit.net 00:00:22.96]       Assert.Contains() Failure: Item not found in collection
[xUnit.net 00:00:22.96]       Collection: []
[xUnit.net 00:00:22.96]       Not found:  "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
[xUnit.net 00:00:22.96]       Stack Trace:
[xUnit.net 00:00:22.96]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(357,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Deleted_wpf_named_compile_file_is_journaled_without_expanding_wpf_semantic_behavior()
[xUnit.net 00:00:22.96]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineDirectoryDisappearanceTests.cs(360,0): at RoslynMcpServer.Tests.Testing.BaselineDirectoryDisappearanceTests.Deleted_wpf_named_compile_file_is_journaled_without_expanding_wpf_semantic_behavior()
[xUnit.net 00:00:22.96]         --- End of stack trace from previous location ---
[xUnit.net 00:00:22.97]   Finished:    RoslynMcpServer.Tests
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

## Проверка остальных prefilters

Для Deleted путь обязан сначала быть валидным canonical path (invalid path не является valid map key), затем map-authoritative descendant stage. Changed-existing-directory и own-write content deferral не применяются к Deleted. После WPF suppression известные map entries проходят ignored-path authority; Known document classification не возвращает ProvenIrrelevant. Mapless behavior не расширяется. Узкая WPF baseline exception относится только к Deleted mapped TextDocument occurrence, не к Changed semantic support необычного extension.
