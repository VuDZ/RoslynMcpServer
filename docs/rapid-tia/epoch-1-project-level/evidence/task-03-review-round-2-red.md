# task-03 — RED исправлений раунда 2

Дата: 2026-11-03. Все прогоны выполнены до исправления поведения production. В первом RED добавлена тестовая точка перед стартом watcher; перед повтором — также точка после старта для точной доставки события. Все 13 сценариев — реальные MSBuild-загрузки. 11 упали, 2 guard-сценария прошли. Фикстуры и сборка исправны. Вывод MCP ограничивает список первыми пятью падениями.

Event-after-start проверяется как guard: на прежнем коде он может пройти или упасть в зависимости от завершения worker и не заявляется как независимое детерминированное доказательство дефекта. Детерминированное доказательство преждевременной публикации — удержанный startup boundary, отдельный вывод которого приведён ниже; sleep не используется.

## Повтор полного RED после добавления точки после старта

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineObservationGapTests`
**Match:** Class name contains `BaselineObservationGapTests`

❌ 11 Tests Failed.

Total: **13** · Passed: **2** · Failed: **11**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Actual_directory_move_retains_known_source_as_a_rescan_candidate`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Actual_directory_move_retains_known_source_as_a_rescan_candidate() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 168    at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Actual_directory_move_retains_known_source_as_a_rescan_candidate() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 170 --- End of stack trace from previous location ---

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_first_publication`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_first_publication(Boolean missingDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 76    at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_first_publication(Boolean missingDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 77 --- End of stack trace from previous location ---

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_first_publication`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_first_publication(Boolean missingDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 76    at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_first_publication(Boolean missingDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 77 --- End of stack trace from previous location --- [xUnit.net 00:00:04.33]       Assert.True() Failure [xUnit.net 00:00:04.33]       Expected: True [xUnit.net 00:00:04.33]       Actual:   False [xUnit.net 00:00:04.33]       Stack Trace: [xUnit.net 00:00:04.33]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs(29,0): at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes(String gap) [xUnit.net 00:00:04.33]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs(39,0): at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_g...

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes(String gap) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 29    at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes(String gap) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 39 --- End of stack trace from previous location --- [xUnit.net 00:00:04.83]       Assert.True() Failure [xUnit.net 00:00:04.83]       Expected: True [xUnit.net 00:00:04.83]       Actual:   False [xUnit.net 00:00:04.83]       Stack Trace: [xUnit.net 00:00:04.83]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs(29,0): at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes(String gap) [xUnit.net 00:00:04.83]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs(39,0): at RoslynMcpServer.Tests.Testing.BaselineObser...

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes(String gap) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 29    at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes(String gap) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 39 --- End of stack trace from previous location ---

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

## Отдельный RED окна публикации и opt-in gap

## Filtered test run

**Filter:** `FullyQualifiedName~BaselineObservationGapTests.Normal_publication|FullyQualifiedName~BaselineObservationGapTests.Event_admitted|FullyQualifiedName~BaselineObservationGapTests.Opt_in`
**Match:** Caller-supplied VSTest filter

❌ 2 Tests Failed.

Total: **3** · Passed: **1** · Failed: **2**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Normal_publication_waits_until_watcher_startup_finishes`
   **Error:**

```text
Assert.Null() Failure: Value is not null
Expected: null
Actual:   "C:\\Users\\VuDZ\\AppData\\Local\\Temp\\RoslynMcpBa"···
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Normal_publication_waits_until_watcher_startup_finishes() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 106    at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Normal_publication_waits_until_watcher_startup_finishes() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 110 --- End of stack trace from previous location ---

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Opt_in_prepublication_gap_is_persistent`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Opt_in_prepublication_gap_is_persistent() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 145    at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Opt_in_prepublication_gap_is_persistent() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 146 --- End of stack trace from previous location --- Total tests: 3      Passed: 1      Failed: 2  Total time: 2.6600 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).      1>_VSTestConsole:          MSB4181: The "VSTestTask" task returned false but did not log an error.      1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED. Build FAILED.     0 Warning(s)     0 Error(s) Time Elapsed 00:00:03.00

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Normal_publication_waits_until_watcher_startup_finishes`

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Opt_in_prepublication_gap_is_persistent`

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

**Filter:** `FullyQualifiedName~BaselineObservationGapTests`
**Match:** Class name contains `BaselineObservationGapTests`

❌ 11 Tests Failed.

Total: **13** · Passed: **2** · Failed: **11**

1. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Actual_directory_move_retains_known_source_as_a_rescan_candidate`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Actual_directory_move_retains_known_source_as_a_rescan_candidate() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 167    at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Actual_directory_move_retains_known_source_as_a_rescan_candidate() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 169 --- End of stack trace from previous location --- [xUnit.net 00:00:01.91]       Assert.True() Failure [xUnit.net 00:00:01.91]       Expected: True [xUnit.net 00:00:01.91]       Actual:   False [xUnit.net 00:00:01.91]       Stack Trace: [xUnit.net 00:00:01.91]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs(76,0): at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_first_publication(Boolean missingDirectory) [xUnit.net 00:00:01.91]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs(77,0): at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_...

2. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_first_publication`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_first_publication(Boolean missingDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 76    at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_first_publication(Boolean missingDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 77 --- End of stack trace from previous location ---

3. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_first_publication`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_first_publication(Boolean missingDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 76    at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Startup_gap_survives_first_publication(Boolean missingDirectory) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 77 --- End of stack trace from previous location --- [xUnit.net 00:00:03.89]       Assert.True() Failure [xUnit.net 00:00:03.89]       Expected: True [xUnit.net 00:00:03.89]       Actual:   False [xUnit.net 00:00:03.89]       Stack Trace: [xUnit.net 00:00:03.89]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs(29,0): at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes(String gap) [xUnit.net 00:00:03.89]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs(39,0): at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_g...

4. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes(String gap) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 29    at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes(String gap) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 39 --- End of stack trace from previous location ---

5. **TestName:** `RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes`
   **Error:**

```text
Assert.True() Failure
Expected: True
Actual:   False
```
   **Stack:** at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes(String gap) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 29    at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes(String gap) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs:line 39 --- End of stack trace from previous location --- [xUnit.net 00:00:04.85]       Assert.True() Failure [xUnit.net 00:00:04.85]       Expected: True [xUnit.net 00:00:04.85]       Actual:   False [xUnit.net 00:00:04.85]       Stack Trace: [xUnit.net 00:00:04.85]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs(29,0): at RoslynMcpServer.Tests.Testing.BaselineObservationGapTests.Observation_gap_freezes_pending_documents_and_retains_known_hashes(String gap) [xUnit.net 00:00:04.85]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\BaselineObservationGapTests.cs(39,0): at RoslynMcpServer.Tests.Testing.BaselineObser...

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
