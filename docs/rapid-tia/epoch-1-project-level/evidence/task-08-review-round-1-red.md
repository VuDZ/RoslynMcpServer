# task-08 — RED по ревью раунда 1

- Дата: **2026-11-06**, база `14fe3ee`; production до исправлений R1-001/R1-002/R1-003.
- Координатор: Codex (точный runtime model ID средой не предоставлен).
- Это воспроизведения дефектов, не validation приёмки. Нулевые запуски ниже **не считаются pass**.

## Полный regression RED раунда 1

Roslyn MCP `execute_dotnet_command`: Release `dotnet test` с rebuild, `--no-restore`, фильтр четырёх новых regression methods (7 data cases), `--logger "trx;LogFileName=review-red.trx"`, `--results-directory TestResults/task-08-review-red`.

TRX `TestResults/task-08-review-red/review-red.trx`: **total=7, executed=7, failed=7, passed=0, error=0, timeout=0, aborted=0, notExecuted=0**. Все причины проверены координатором. Перед этим первые две cross-project cases падали на подготовке fixture; этот запуск не считается TDD RED. Fixture исправлена добавлением явной Specs→Library project reference; production до повторного RED не менялась.

### R1-002: проект только с унаследованными тестами

Реальная MSBuild-загрузка: Library содержит публичный abstract Base с `[Fact] Inherited`; Specs содержит только `Derived : Base`, без собственного test method. Явная project reference. До правки проверено, что runtime inventory уже содержит конкретный `Derived.Inherited` в Specs. Правка через MCP либо Base.Inherited, либо обычного Library.Old.Run вызывает публичный инструмент. Оба случая доходят до конечной проверки ответа и не находят `Specs.csproj`; подготовка fixture успешна.

### Причины всех семи падений

**RoslynMcpServer.Tests.Testing.EstimateAffectedTestsTests.Real_MSBuild_inherited_only_test_project_is_selected_after_base_or_production_edit(editBase: False)**

```text
Assert.Contains() Failure: Sub-string not found
String:    "Approximate result (rapid TIA): not a sub"···
Not found: "Specs.csproj"
```

**RoslynMcpServer.Tests.Testing.EstimateAffectedTestsAdapterTests.Inherited_NUnit_fixture_constructor_and_source_arguments_use_whole_project_through_tool(method: "Constructor")**

```text
Assert.Null() Failure: Value is not null
Expected: null
Actual:   "FullyQualifiedName~Ns.ArgumentDerived."
```

**RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.Inherited_NUnit_fixture_arguments_are_not_mistaken_for_a_plain_concrete_class(attribute: "[NUnit.Framework.TestFixtureSource(\"Cases\")]")**

```text
Assert.True() Failure
Expected: True
Actual:   False
```

**RoslynMcpServer.Tests.Testing.EstimateAffectedTestsTests.Native_deleted_known_source_selects_its_dependent_tests_before_reload**

```text
Assert.Contains() Failure: Sub-string not found
String:    "Approximate result (rapid TIA): not a sub"···
Not found: "Specs.csproj"
```

**RoslynMcpServer.Tests.Testing.EstimateAffectedTestsTests.Real_MSBuild_inherited_only_test_project_is_selected_after_base_or_production_edit(editBase: True)**

```text
Assert.Contains() Failure: Sub-string not found
String:    "Approximate result (rapid TIA): not a sub"···
Not found: "Specs.csproj"
```

**RoslynMcpServer.Tests.Testing.EstimateAffectedTestsAdapterTests.Inherited_NUnit_fixture_constructor_and_source_arguments_use_whole_project_through_tool(method: "Source")**

```text
Assert.Null() Failure: Value is not null
Expected: null
Actual:   "FullyQualifiedName~Ns.SourceDerived."
```

**RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.Inherited_NUnit_fixture_arguments_are_not_mistaken_for_a_plain_concrete_class(attribute: "[NUnit.Framework.TestFixture(42)]")**

```text
Assert.True() Failure
Expected: True
Actual:   False
```

## R1-001: удаление известного исходника до reload

Координатор выполнил `EstimateAffectedTestsTests.Native_deleted_known_source_selects_its_dependent_tests_before_reload` через Roslyn MCP, Release, `noBuild=true`, `noRestore=true`. Тест сначала проверяет Known baseline файла `Library/Old.cs`, удаляет его на диске, ждёт настоящее native watcher event и вызывает публичный инструмент **до reload**. Выполнен 1 тест, failed=1: в ответе отсутствует зависимый `Specs.csproj`. Это не ошибка подготовки фикстуры. Причина в рассматриваемом коде: disk sync оставляет отсутствующий документ cached, после чего estimator убирает путь из journal и сравнивает старое дерево с baseline как неизменённое.

## Filtered test run

**Filter:** `FullyQualifiedName~.EstimateAffectedTestsTests.Native_deleted_known_source_selects_its_dependent_tests_before_reload`
**Match:** Name suffix `.EstimateAffectedTestsTests.Native_deleted_known_source_selects_its_dependent_tests_before_reload`

❌ 1 Tests Failed.

Total: **1** · Passed: **0** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsTests.Native_deleted_known_source_selects_its_dependent_tests_before_reload`
   **Error:**

```text
Assert.Contains() Failure: Sub-string not found
String:    "Approximate result (rapid TIA): not a sub"···
Not found: "Specs.csproj"
```
   **Stack:** at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsTests.Native_deleted_known_source_selects_its_dependent_tests_before_reload() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsTests.cs:line 229    at RoslynMcpServer.Tests.Testing.EstimateAffectedTestsTests.Native_deleted_known_source_selects_its_dependent_tests_before_reload() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\EstimateAffectedTestsTests.cs:line 231 --- End of stack trace from previous location --- Total tests: 1      Failed: 1  Total time: 3.9192 Seconds      1>Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" on node 1 (VSTest target(s)).      1>_VSTestConsole:          MSB4181: The "VSTestTask" task returned false but did not log an error.      1>Done Building Project "E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj" (VSTest target(s)) -- FAILED. Build FAILED.     0 Warning(s)     0 Error(s) Time Elapsed 00:00:04.40

2. **TestName:** `RoslynMcpServer.Tests.Testing.EstimateAffectedTestsTests.Native_deleted_known_source_selects_its_dependent_tests_before_reload`

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

## R1-003: унаследованные аргументы NUnit

Изолированный реальный проект `spikes/AdapterTestNames/artifacts/task08-NUnitInherited/NUnit.csproj` (ignored): NUnit 4.3.2, adapter 5.0.0, Test SDK 17.14.1, net10.0. Абстрактная `ArgumentBase` имеет `[TestFixture(42)]`, конкретная `DerivedArgument(int value) : base(value)` — собственный `[Test] Own()`. Второй случай: `[TestFixtureSource(nameof(Cases))]` на `SourceBase`, конкретная `DerivedSource` с собственным `Own()`. Источник возвращает `new object[] { 1 }`.

Roslyn MCP Release build PASS. VSTest discovery `--list-tests --diag TestResults/task-08-smoke/nunit-inherited.discovery.log`: полный `TestDiscovery.Completed.Payload.LastDiscoveredTests` содержит ровно два фактических FQN:

- `AdapterNames.NUnit.DerivedArgument(42).Own`.
- `AdapterNames.NUnit.DerivedSource(1).Own`.

Затем новый Release stdio MCP-server загрузил этот проект. После обратимой правки тела каждого собственного `Own()` через native disk write инструмент `estimate_affected_tests(includeDetails=true)` вернул соответственно `FullyQualifiedName~AdapterNames.NUnit.DerivedArgument.` и `FullyQualifiedName~AdapterNames.NUnit.DerivedSource.`. Ответы сохранены локально в `TestResults/task-08-smoke/NUnitInherited-inherited-constructor.txt`, `NUnitInherited-inherited-source.txt`, `inherited-plans.json`. Точные исходные bytes/mtime восстановлены в finally. Первый запуск stdio в sandbox не смог подключить MSBuild build host к named pipe; повтор вне sandbox успешно загрузил проект и воспроизвёл ошибочные планы.

Координатор исполнил **именно эти фильтры** через Roslyn MCP `run_test_by_filter`, Release, `noBuild=true`, `noRestore=true`. Оба выбрали **0 тестов**, несмотря на существующие runnable cases. Исходный вывод:

## Filtered test run

**Filter:** `FullyQualifiedName~AdapterNames.NUnit.DerivedArgument.`
**Match:** Caller-supplied VSTest filter

**Status:** partial

Tests completed (exit 0); no VSTest/xUnit summary line was detected.


Raw output (last 2048 chars):

```text
Build started 11/6/2026 1:24:55 PM.
Test run for E:\Devel\RoslynMcpServer-rapid-tia\spikes\AdapterTestNames\artifacts\task08-NUnitInherited\bin\Release\net10.0\AdapterNames.NUnit.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
NUnit Adapter 5.0.0.0: Test execution started
Running selected tests in E:\Devel\RoslynMcpServer-rapid-tia\spikes\AdapterTestNames\artifacts\task08-NUnitInherited\bin\Release\net10.0\AdapterNames.NUnit.dll
   NUnit3TestExecutor discovered 0 of 0 NUnit test cases using Current Discovery mode, Explicit run
NUnit Adapter 5.0.0.0: Test execution complete
No test matches the given testcase filter `FullyQualifiedName~AdapterNames.NUnit.DerivedArgument.` in E:\Devel\RoslynMcpServer-rapid-tia\spikes\AdapterTestNames\artifacts\task08-NUnitInherited\bin\Release\net10.0\AdapterNames.NUnit.dll
```

### Execution context
- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.402` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.402\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.402\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **WorkingDirectory:** `E:\Devel\RoslynMcpServer-rapid-tia\spikes\AdapterTestNames\artifacts\task08-NUnitInherited`
- **DOTNET_CLI_UI_LANGUAGE:** `en-US`
- **Configuration:** Release
- **Platform:** (SDK/solution default)
- **BuildArgs:** (none)
- **PreTestBuild:** skipped (`noBuild=true`)
- **TestOutput:** StdOut 2500 chars, StdErr 1000 chars
Full report: pass reportCursor=`013b84b6d05643b1bbbe96665968a04e` to this same tool.

## Filtered test run

**Filter:** `FullyQualifiedName~AdapterNames.NUnit.DerivedSource.`
**Match:** Caller-supplied VSTest filter

**Status:** partial

Tests completed (exit 0); no VSTest/xUnit summary line was detected.


Raw output (last 2048 chars):

```text
Build started 11/6/2026 1:25:01 PM.
Test run for E:\Devel\RoslynMcpServer-rapid-tia\spikes\AdapterTestNames\artifacts\task08-NUnitInherited\bin\Release\net10.0\AdapterNames.NUnit.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
NUnit Adapter 5.0.0.0: Test execution started
Running selected tests in E:\Devel\RoslynMcpServer-rapid-tia\spikes\AdapterTestNames\artifacts\task08-NUnitInherited\bin\Release\net10.0\AdapterNames.NUnit.dll
   NUnit3TestExecutor discovered 0 of 0 NUnit test cases using Current Discovery mode, Explicit run
NUnit Adapter 5.0.0.0: Test execution complete
No test matches the given testcase filter `FullyQualifiedName~AdapterNames.NUnit.DerivedSource.` in E:\Devel\RoslynMcpServer-rapid-tia\spikes\AdapterTestNames\artifacts\task08-NUnitInherited\bin\Release\net10.0\AdapterNames.NUnit.dll
```

### Execution context
- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.402` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.402\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.402\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **WorkingDirectory:** `E:\Devel\RoslynMcpServer-rapid-tia\spikes\AdapterTestNames\artifacts\task08-NUnitInherited`
- **DOTNET_CLI_UI_LANGUAGE:** `en-US`
- **Configuration:** Release
- **Platform:** (SDK/solution default)
- **BuildArgs:** (none)
- **PreTestBuild:** skipped (`noBuild=true`)
- **TestOutput:** StdOut 2500 chars, StdErr 1000 chars
Full report: pass reportCursor=`4561e316b88744cc98729c5925497e32` to this same tool.
