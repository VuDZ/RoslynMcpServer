# task-05 — TDD RED

- Дата: **2026-11-05** (Europe/Moscow); база `9686c4c`.
- До production-реализации: `TestImpactComposer.Compose` — `NotImplementedException`; mapper сохраняет поведение task-04. Добавлены только компилируемые контракты и параметры; старые callers передают оба stale-флага.
- Тесты написаны первыми: `TestImpactComposerTests`, `TestImpactWatcherTests`, fixture настоящего watcher. Последовательность координируется с исполнителем, до GO поведение не редактировалось.
- Roslyn MCP `run_test_by_filter`, Release `noBuild=false`; затем тот же фильтр через `execute_dotnet_command` с `--no-build --no-restore` и TRX logger, поскольку специализированный runner не принимает logger и показывает первые пять failures.
- TRX: ignored `TestResults/task-05-red/red.trx`. Все 37 tests executed/failed, 0 passed, 0 skipped/error/timeout/aborted/notExecuted. Ниже полный проверенный перечень ошибок вне stub; ошибки наблюдения watcher/фикстур отсутствуют.

## TRX и причины

```text
<Counters total="37" executed="37" passed="0" failed="37" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" />
StubFailures=30; BehavioralFailures=7
TEST: RoslynMcpServer.Tests.Testing.TestImpactWatcherTests.Native_directory_membership_changes_remain_visible_without_expanding_unrelated_tests(operation: "out")
Assert.DoesNotContain() Failure: Item found in collection
             ↓ (pos 0)
Collection: ["Isolated", "Specs"]
Found:      "Isolated"
TEST: RoslynMcpServer.Tests.Testing.TestImpactWatcherTests.Native_directory_membership_changes_remain_visible_without_expanding_unrelated_tests(operation: "restore")
Assert.DoesNotContain() Failure: Item found in collection
             ↓ (pos 0)
Collection: ["Isolated", "Specs"]
Found:      "Isolated"
TEST: RoslynMcpServer.Tests.Testing.TestImpactWatcherTests.Native_new_test_file_is_directly_included_before_and_after_reload_without_unrelated_projects
Assert.Equal() Failure: Collections differ
           ↓ (pos 0)
Expected: ["Specs"]
Actual:   ["Isolated", "Specs"]
           ↑ (pos 0)
TEST: RoslynMcpServer.Tests.Testing.TestImpactWatcherTests.Native_unmapped_non_CSharp_file_is_uncovered_with_reload_and_does_not_expand_tests
Assert.Empty() Failure: Collection was not empty
Collection: [(ProjectId, #88cbdb6e-55dd-4bc7-b101-e35baa1885ee - C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpImpact-fc2ec7d5c78942e6897e424e17b2e4b7\Isolated\Isolated.csproj), (ProjectId, #12b54c6b-441a-4ed0-bf09-06f3dd4ceab4 - C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpImpact-fc2ec7d5c78942e6897e424e17b2e4b7\Specs\Specs.csproj)]
TEST: RoslynMcpServer.Tests.Testing.TestImpactWatcherTests.Native_deleted_or_excluded_source_does_not_request_a_second_reload(excluded: True)
Assert.False() Failure
Expected: False
Actual:   True
TEST: RoslynMcpServer.Tests.Testing.TestImpactWatcherTests.Native_deleted_or_excluded_source_does_not_request_a_second_reload(excluded: False)
Assert.False() Failure
Expected: False
Actual:   True
TEST: RoslynMcpServer.Tests.Testing.TestImpactWatcherTests.Native_directory_membership_changes_remain_visible_without_expanding_unrelated_tests(operation: "into")
Assert.DoesNotContain() Failure: Item found in collection
             ↓ (pos 0)
Collection: ["Isolated", "Specs"]
Found:      "Isolated"
[{"Path":"E:\\Devel\\RoslynMcpServer-rapid-tia\\RoslynMcpServer.Tests\\Testing\\TestImpactComposerTests.cs","Hash":"7069D0CE1E2198B787B671D693F787988AD871F48420FF05AECC722B7D1B81C6"},{"Path":"E:\\Devel\\RoslynMcpServer-rapid-tia\\RoslynMcpServer.Tests\\Testing\\TestImpactWatcherTests.cs","Hash":"44C1AA596EFCC8CA2E2543ACF6CBD4F3953FAC5F1A882ECF8DE6D83FEBFDDDE9"},{"Path":"E:\\Devel\\RoslynMcpServer-rapid-tia\\RoslynMcpServer.Tests\\Testing\\TestImpactWatcherFixture.cs","Hash":"A12E1B316CEC2893A0F5C7DADC962A98E0299651755A5C5E53D66E9153C9E60A"}]

```

## Исходный ответ специализированного MCP

## Filtered test run

**Filter:** `FullyQualifiedName~TestImpactComposerTests|FullyQualifiedName~TestImpactWatcherTests`
**Match:** Caller-supplied VSTest filter

❌ 37 Tests Failed.

Total: **37** · Passed: **0** · Failed: **37**

1. **TestName:** `RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Direct_tests_and_own_roots_are_unioned_with_required_project_expansion`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.TestImpactComposer.Compose(Solution solution, ProjectImpactResult projectImpact, IReadOnlyList'1 documents, IReadOnlyList'1 graphTests, IReadOnlySet'1 baseTypeNames, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\TestImpactComposer.cs:line 20    at RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Compose(ProjectImpactFixture fixture, ProjectImpactResult mapped, DocumentImpact[] changes) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\TestImpactComposerTests.cs:line 182    at RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Direct_tests_and_own_roots_are_unioned_with_required_project_expansion(String before, String current, String expectedNames, Boolean expands) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\TestImpactComposerTests.cs:line 40 --- End of stack trace from previous location ---

2. **TestName:** `RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Direct_tests_and_own_roots_are_unioned_with_required_project_expansion`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.TestImpactComposer.Compose(Solution solution, ProjectImpactResult projectImpact, IReadOnlyList'1 documents, IReadOnlyList'1 graphTests, IReadOnlySet'1 baseTypeNames, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\TestImpactComposer.cs:line 20    at RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Compose(ProjectImpactFixture fixture, ProjectImpactResult mapped, DocumentImpact[] changes) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\TestImpactComposerTests.cs:line 182    at RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Direct_tests_and_own_roots_are_unioned_with_required_project_expansion(String before, String current, String expectedNames, Boolean expands) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\TestImpactComposerTests.cs:line 40 --- End of stack trace from previous location ---

3. **TestName:** `RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Direct_tests_and_own_roots_are_unioned_with_required_project_expansion`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.TestImpactComposer.Compose(Solution solution, ProjectImpactResult projectImpact, IReadOnlyList'1 documents, IReadOnlyList'1 graphTests, IReadOnlySet'1 baseTypeNames, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\TestImpactComposer.cs:line 20    at RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Compose(ProjectImpactFixture fixture, ProjectImpactResult mapped, DocumentImpact[] changes) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\TestImpactComposerTests.cs:line 182    at RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Direct_tests_and_own_roots_are_unioned_with_required_project_expansion(String before, String current, String expectedNames, Boolean expands) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\TestImpactComposerTests.cs:line 40 --- End of stack trace from previous location ---

4. **TestName:** `RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Direct_tests_and_own_roots_are_unioned_with_required_project_expansion`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.TestImpactComposer.Compose(Solution solution, ProjectImpactResult projectImpact, IReadOnlyList'1 documents, IReadOnlyList'1 graphTests, IReadOnlySet'1 baseTypeNames, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\TestImpactComposer.cs:line 20    at RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Compose(ProjectImpactFixture fixture, ProjectImpactResult mapped, DocumentImpact[] changes) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\TestImpactComposerTests.cs:line 182    at RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Direct_tests_and_own_roots_are_unioned_with_required_project_expansion(String before, String current, String expectedNames, Boolean expands) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\TestImpactComposerTests.cs:line 40 --- End of stack trace from previous location ---

5. **TestName:** `RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Direct_tests_and_own_roots_are_unioned_with_required_project_expansion`
   **Error:** System.NotImplementedException : The method or operation is not implemented.
   **Stack:** at RoslynMcpServer.Services.Testing.TestImpactComposer.Compose(Solution solution, ProjectImpactResult projectImpact, IReadOnlyList'1 documents, IReadOnlyList'1 graphTests, IReadOnlySet'1 baseTypeNames, CancellationToken cancellationToken) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Testing\TestImpactComposer.cs:line 20    at RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Compose(ProjectImpactFixture fixture, ProjectImpactResult mapped, DocumentImpact[] changes) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\TestImpactComposerTests.cs:line 182    at RoslynMcpServer.Tests.Testing.TestImpactComposerTests.Direct_tests_and_own_roots_are_unioned_with_required_project_expansion(String before, String current, String expectedNames, Boolean expands) in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\TestImpactComposerTests.cs:line 40 --- End of stack trace from previous location ---

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
