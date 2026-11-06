# task-08 — RED раунда 3, R2-001

- Дата: **2026-11-06**; база `1f11b47`, версия до исправления **1.6.0**.
- Решение владельца: [второй рецензент раунда 2](task-08-review-round-2-claude.md), третий и последний раунд по R-18. Это исправление избыточного вечного расширения после reload; прежние R1-001/R1-002/R1-003 и T07-C1 остаются закрыты.
- Исполнитель — прежний `/root/task08_implementer`, Codex (точный runtime model ID средой не предоставлен). До production-правок добавлены **7 cases** `EstimateAffectedTestsReloadTests`; существующий observation-gap тест усилен явным Release reload с проверками `LastLoadReopenedGraph=true`, `LastLoadWasCacheHit=false`.
- Все сценарии используют настоящий MSBuild/native watcher. Reload helper дополнительно проверяет рост baseline generation и сброс stale-состояния. Два guards проверяют C# правку в окне до старта watcher (с сохранённым mtime и event revision=0) и после reopen, сохранность Known snapshot и C# rescan candidates.
- Координатор выполнил Roslyn MCP `run_test_by_filter`, Release, `noBuild=false`, `noRestore=true`; компиляция успешна. **8 executed / 3 passed / 5 failed**. Затем тот же набор повторён через MCP `execute_dotnet_command` с `--no-build --no-restore --logger "trx;LogFileName=red.trx" --results-directory TestResults/task-08-round-3-red`.
- Полный локальный TRX `TestResults/task-08-round-3-red/red.trx`: total=8, executed=8, passed=3, failed=5, error=0, timeout=0, aborted=0, notExecuted=0. Все причины проверены: **5 ожидаемых failures в проверках публичного ответа**, без compile/fixture failures. Три защитных сценария настоящего gap/capture failure проходят уже до изменения.
- Failed: Fresh reload не даёт фильтр; после удаления Fresh и возврата теста остаётся непустой план; после .csproj reload включён независимый Isolated; два reopen cases не возвращают фильтр. Реальный .csproj DefineConstants delta намеренно должен сохранять своё проектное расширение после reload; это не требование забыть известное изменение входа.
- До production-fix Fresh fixture уточнена обычными using/namespace. Исполнитель выполнил Roslyn MCP `run_specific_test`, Release, `noBuild=false`, `noRestore=true`, method `Native_new_source_reload_returns_to_filters_without_independent_projects`: **1 executed / 1 failed**, ожидаемое отсутствие `FullyQualifiedName~` в публичном ответе, все preconditions настоящего reload прошли. Координатор прочитал исходный вывод `TestResults/task-08-round-3-red/namespace-red-mcp.txt`. Количество cases и assertions не менялись. Глобальные using/assembly attributes/extension/operator safeguards не ослабляются.

## Исходный focused-вывод координатора

## Filtered test run

**Filter:** `FullyQualifiedName~EstimateAffectedTestsReloadTests|FullyQualifiedName~EstimateAffectedTestsTests.Observation_gap_expands_projects`
**Match:** Caller-supplied VSTest filter

❌ 5 Tests Failed.

Total: **8** · Passed: **3** · Failed: **5**

1. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Same_solution_reopen_preserves_CSharp_edits_in_the_window_and_after_reload`
   **Error:**

```text
Assert.Contains() Failure: Sub-string not found
String:    "Approximate result (rapid TIA): not a sub"···
Not found: "FullyQualifiedName~"
```
   **Stack:** at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.AssertFilteredSpecs(String result) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsReloadTests.cs:line 194    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Same_solution_reopen_preserves_CSharp_edits_in_the_window_and_after_reload(Boolean inWindow) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsReloadTests.cs:line 123    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Same_solution_reopen_preserves_CSharp_edits_in_the_window_and_after_reload(Boolean inWindow) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsReloadTests.cs:line 124 --- End of stack trace from previous location ---   Passed RoslynMcpServer.Tests.Testing.EstimateAffectedTestsTests.Observation_gap_expands_projects_even_when_known_CSharp_is_unchanged_and_survives_reload [2 s] [xUnit.net 00:00:04.10]       Assert.Contains() Failure: Sub-string not found [xUnit.net 00:00:04.10]       String:    "Approximate result (rapid TIA): not a sub"··· [xUnit.net 00:00:04.10]       Not found: "FullyQu...

2. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Same_solution_reopen_preserves_CSharp_edits_in_the_window_and_after_reload`
   **Error:**

```text
Assert.Contains() Failure: Sub-string not found
String:    "Approximate result (rapid TIA): not a sub"···
Not found: "FullyQualifiedName~"
```
   **Stack:** at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.AssertFilteredSpecs(String result) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsReloadTests.cs:line 194    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Same_solution_reopen_preserves_CSharp_edits_in_the_window_and_after_reload(Boolean inWindow) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsReloadTests.cs:line 123    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Same_solution_reopen_preserves_CSharp_edits_in_the_window_and_after_reload(Boolean inWindow) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsReloadTests.cs:line 124 --- End of stack trace from previous location --- [xUnit.net 00:00:05.79]       Assert.Contains() Failure: Sub-string not found [xUnit.net 00:00:05.79]       String:    "Approximate result (rapid TIA): not a sub"··· [xUnit.net 00:00:05.79]       Not found: "FullyQualifiedName~" [xUnit.net 00:00:05.79]       Stack Trace: [xUnit.net 00:00:05.79]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\Estima...

3. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Native_new_source_reload_returns_to_filters_without_independent_projects`
   **Error:**

```text
Assert.Contains() Failure: Sub-string not found
String:    "Approximate result (rapid TIA): not a sub"···
Not found: "FullyQualifiedName~"
```
   **Stack:** at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.AssertFilteredSpecs(String result) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsReloadTests.cs:line 194    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Native_new_source_reload_returns_to_filters_without_independent_projects() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsReloadTests.cs:line 24    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Native_new_source_reload_returns_to_filters_without_independent_projects() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsReloadTests.cs:line 32 --- End of stack trace from previous location --- [xUnit.net 00:00:08.16]       Assert.DoesNotContain() Failure: Sub-string found [xUnit.net 00:00:08.16]                                         ↓ (pos 174) [xUnit.net 00:00:08.16]       String: ···" plan: 2 projects.\r\nRun: 'C:\\Users\\VuDZ\\A"··· [xUnit.net 00:00:08.16]       Found:  "Run:" [xUnit.net 00:00:08.16]       Stack Trace: [xUnit.net 00:00:08.16]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMc...

4. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Native_new_source_deleted_and_test_restored_have_an_empty_plan_after_real_reload`
   **Error:**

```text
Assert.DoesNotContain() Failure: Sub-string found
↓ (pos 174)
String: ···" plan: 2 projects.\r\nRun: `C:\\Users\\VuDZ\\A"···
Found:  "Run:"
```
   **Stack:** at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Native_new_source_deleted_and_test_restored_have_an_empty_plan_after_real_reload() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsReloadTests.cs:line 55    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Native_new_source_deleted_and_test_restored_have_an_empty_plan_after_real_reload() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsReloadTests.cs:line 57 --- End of stack trace from previous location ---   Passed RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.True_observation_gap_still_expands_every_project_after_a_real_native_reload [1 s] [xUnit.net 00:00:11.19]       Assert.DoesNotContain() Failure: Sub-string found [xUnit.net 00:00:11.19]                                         ↓ (pos 271) [xUnit.net 00:00:11.19]       String: ···"fc5b1feb3c\\Isolated\\Isolated.csproj' whol"··· [xUnit.net 00:00:11.19]       Found:  "Isolated.csproj" [xUnit.net 00:00:11.19]       Stack Trace: [xUnit.net 00:00:11.19]         E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedT...

5. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Native_project_edit_reload_keeps_its_owner_closure_without_independent_projects`
   **Error:**

```text
Assert.DoesNotContain() Failure: Sub-string found
↓ (pos 271)
String: ···"fc5b1feb3c\\Isolated\\Isolated.csproj` whol"···
Found:  "Isolated.csproj"
```
   **Stack:** at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Native_project_edit_reload_keeps_its_owner_closure_without_independent_projects() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsReloadTests.cs:line 75    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Native_project_edit_reload_keeps_its_owner_closure_without_independent_projects() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsReloadTests.cs:line 78 --- End of stack trace from previous location --- [xUnit.net 00:00:12.66]   Finished:    RoslynMcpServer.Tests   Passed RoslynMcpServer.Tests.Testing.EstimateAffectedTestsReloadTests.Snapshot_capture_failure_remains_a_true_gap_after_a_real_reload [1 s] Total tests: 8      Passed: 3      Failed: 5  Total time: 12.9761 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).      1>_VSTestConsole:          MSB4181: The "VSTestTask" task returned false but did not log an error.      1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpSe...

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
