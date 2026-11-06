# task-08 — живая validation координатора

- Дата: **2026-11-06**, Windows, SDK **10.0.402**, runtime **10.0.13**, Release server **1.6.0** после исправлений R1-001/R1-002/R1-003.
- Дополнительные проверки [T07-C1](../task-08-estimate-tool-hi.md#передача-из-task-07-обязательные-условия) и R-18; не заменяют [final build/main](task-08-final-validation.md).
- Новый stdio MCP-server вызван native runner без Computer Use/IDE UI: `load_workspace` → обратимая правка тела метода на диске → `estimate_affected_tests(includeDetails=true)` → восстановление исходных bytes/mtime в finally. Полученные планы реально исполнены **через Roslyn MCP**: `run_test_by_filter` для фильтра либо `run_dotnet_test` для whole project, Release, `noBuild=true`, `noRestore=true`.
- SDK-проекты адаптеров заранее собраны. Правка добавляет семантически нейтральный `GC.KeepAlive(42)` либо эквивалентное true-выражение. Проверяется отбор планом; исполнение новых бинарников — scope task-09.
- Версии исходных fixtures: xUnit v2 2.9.3/adapter3.1.0; xUnit v3 3.2.2/MTP off/adapter3.1.5; NUnit4.3.2/adapter5.0.0; MSTest3.8.3; Test SDK17.14.1. Frozen task-06 каталог и исходники не изменены.
- **19 планов / 61 успешное test execution**: 9 фильтров, 10 whole-project запусков. Это сумма выполнений, не число уникальных тестов. В каждом total=passed=expected, failed=0; нулевой запуск не считается pass.

## Планы и фактические счётчики

- **XunitV2 / nested**: `FullyQualifiedName~AdapterNames.Xunit.Outer+Inner.` → **1/1/0** (total/passed/failed).
- **XunitV2 / generic**: whole project → **5/5/0** (total/passed/failed).
- **XunitV2 / inherited**: whole project → **5/5/0** (total/passed/failed).
- **XunitV2 / parameters**: `FullyQualifiedName~AdapterNames.Xunit.ParameterTests.` → **2/2/0** (total/passed/failed).
- **XunitV3 / nested**: `FullyQualifiedName~AdapterNames.Xunit.Outer+Inner.` → **1/1/0** (total/passed/failed).
- **XunitV3 / generic**: whole project → **5/5/0** (total/passed/failed).
- **XunitV3 / inherited**: whole project → **5/5/0** (total/passed/failed).
- **XunitV3 / parameters**: `FullyQualifiedName~AdapterNames.Xunit.ParameterTests.` → **2/2/0** (total/passed/failed).
- **NUnit / nested**: `FullyQualifiedName~AdapterNames.NUnit.Outer+Inner.` → **1/1/0** (total/passed/failed).
- **NUnit / generic**: `FullyQualifiedName~AdapterNames.NUnit.GenericTests<Int32>.` → **1/1/0** (total/passed/failed).
- **NUnit / inherited**: whole project → **5/5/0** (total/passed/failed).
- **NUnit / parameters**: `FullyQualifiedName~AdapterNames.NUnit.ParameterTests.` → **2/2/0** (total/passed/failed).
- **MSTest / nested**: `FullyQualifiedName~AdapterNames.MSTest.Outer+Inner.` → **1/1/0** (total/passed/failed).
- **MSTest / generic**: whole project → **5/5/0** (total/passed/failed).
- **MSTest / inherited**: whole project → **5/5/0** (total/passed/failed).
- **MSTest / parameters**: `FullyQualifiedName~AdapterNames.MSTest.ParameterTests.` → **2/2/0** (total/passed/failed).
- **NUnitArguments / constructor-source-genericconstructor**: whole project → **9/9/0** (total/passed/failed).
- **NUnitInherited / inherited-constructor**: whole project → **2/2/0** (total/passed/failed).
- **NUnitInherited / inherited-source**: whole project → **2/2/0** (total/passed/failed).

## Дополнительные NUnit fixtures

Проект `spikes/AdapterTestNames/artifacts/task08-NUnitArguments` содержит пять исходных cases и четыре cases аргументов: два `[TestFixture("a")]/[TestFixture("b")]`, один `TestFixtureSource`, один `[TestFixture(typeof(int), 1)]`. Baseline и выбранный целиком проект дают **9/9**. Фактический discovery показывает `ArgumentFixture("a").Constructor`, `ArgumentFixture("b").Constructor`, `SourceFixture(1).Source`, `GenericArgumentFixture<Int32>(1).GenericConstructor`.

Отдельный `task08-NUnitInherited` содержит `[TestFixture(42)]` и `[TestFixtureSource]` на абстрактных базах, с собственным `[Test] Own` у конкретных наследников. Discovery: `DerivedArgument(42).Own`, `DerivedSource(1).Own`. Правка каждого теста проверена отдельно: два whole-project плана, каждый **2/2**. Старые `Derived.` фильтры выбирали 0 — это [RED дефекта](task-08-review-round-1-red.md), а не pass. Локальные JSON/ответы теперь содержат GREEN; RED-фильтры и исходный MCP zero-match вывод сохранены в отчёте RED.

Drivers, discovery, полные ответы и JSON планов находятся в ignored `TestResults/task-08-smoke/`; дополнительные SDK-проекты — в ignored `spikes/AdapterTestNames/artifacts/`. Первый stdio run в sandbox не подключил MSBuild build host к named pipe; повтор вне sandbox успешно загрузил проекты и выполнил проверки.

## Проект владельца: RoslynMcpServer.sln

Чистый новый процесс загрузил **5 проектов**, `lite` содержит **16 tools**. Три последовательных estimate-запроса после всех source edits адаптеров: начальный, правка одного тестового метода, точное восстановление. Во всех трёх **Not covered: none**, observation gap **0/3**; потери выбранных тестов в этой пробе нет. Это один короткий сценарий, не длительный замер на других проектах.

Предварительный owner run одновременно с spike source edits видел чужие изменения; он **не принят** как доказательство чистого baseline/restore. Следующий повтор из нового процесса требует ноль членов/файлов/проектов в начале и после восстановления.

### Начальный ответ

```text
Approximate result (rapid TIA): not a substitute for the full test suite or CI.
Changed since load: 0 members in 0 files. Directly selected: 0 tests. Run plan: 0 projects.
Not covered: none.
State: project-level result only.
```

### После правки

```text
Approximate result (rapid TIA): not a substitute for the full test suite or CI.
Changed since load: 1 members in 1 files. Directly selected: 1 tests. Run plan: 1 projects.
Run: `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj` with filter `FullyQualifiedName~RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Saved_adapter_names_select_every_measured_case`
Reasons: Changed test: E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestRunPlannerTests.cs; VSTest contains filters select a superset; names containing a selected name also run.
Not covered: none.
State: project-level result only.
```

Именно полученный фильтр исполнен координатором через Roslyn MCP: **16/16**, failed=0. Это все ожидаемые data cases данного source test method; инструмент считает один source method, адаптер исполняет 16 cases.

### После восстановления

```text
Approximate result (rapid TIA): not a substitute for the full test suite or CI.
Changed since load: 0 members in 0 files. Directly selected: 0 tests. Run plan: 0 projects.
Not covered: none.
State: project-level result only.
```

Время трёх estimate-вызовов: **2.02 / 1.54 / 0.71 s**. Загрузка — 1.58 s; до первого запроса выдержано 15 s для фонового baseline capture. Это наблюдения текущей машины, не performance benchmark.
