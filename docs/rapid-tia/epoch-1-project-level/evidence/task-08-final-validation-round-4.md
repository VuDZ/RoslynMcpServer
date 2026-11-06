# task-08 — финальная validation после раунда 4

- Дата: **2026-11-06**, Windows, SDK **10.0.402**, runtime **10.0.13**; база исправлений `1f11b47`, версия **1.6.1**.
- Принят повторно estimate-only scope task-08: исправлены R2-001 (вечное расширение после ordinary reload) и R3-001 (потеря исходного AdditionalFile .cs / неизвестного journal path). `run`, MTP и будущий граф не входят.
- [Раунд 4 — ACCEPT](task-08-review-round-4.md), R2-001/R3-001 закрыты. Владелец явно разрешил исправление и четвёртое ревью ответом «Исправить и проверить ещё раз» от 2026-11-06; это точечное исключение из R-18.
- После последней C#/project freeze координатор выполнил Roslyn MCP Release solution build, `noIncremental=true`. Затем **focused 68/68, SourceStructure 123/123, полный main 1980/1980 и opted-in AnalyzerLifecycle 13/13** на этой сборке, `noBuild=true`, `noRestore=true`. Все финальные tests ненулевые, total=passed, failed=0, без skips/timeouts.
- Main Windows filter: `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`, как CI. Linux на окончательном fix не запускался; предыдущее внешнее Linux validation относится к `98773ad`, не к 1.6.1.
- Lifecycle обязателен здесь из-за baseline/load-state поведения: raw Roslyn MCP `execute_dotnet_command` с `--filter "Category=AnalyzerLifecycle" --environment "ROSLYN_MCP_ANALYZER_LIFECYCLE=1" --logger "trx;LogFileName=lifecycle.trx" --results-directory TestResults/task-08-round-4-lifecycle`. XML TRX отдельно проверен: **total=executed=passed=13**, failed/error/timeout/aborted/notExecuted=0.
- Exact SourceStructure inventory **16 известных limits**, identities/kinds/paths не менялись и не ослаблялись. Исполнитель также получил **349/349** Baseline; прежние assertions сохранены.
- Первый main на коде R4 дал **1979/1980**: новый native case получил `HasObservationGap=true` в проверке precondition. Это **не** выдано за PASS. Два новых native-класса включены в существующую непараллельную `BaselineObservationGap` collection, используемую для process-wide watcher startup-failure seam. Точный callback источника флага в том запуске не трассировался; подтверждены общий механизм и подходящая изоляция, а не точная причина единичного флага. Ни один assertion и production-файл при этом не изменён. После metadata delta повторены build, focused, SourceStructure, **весь main** и Lifecycle с указанным выше PASS.
- История TDD: [R3 RED](task-08-review-round-3-red.md) — 8 executed/3 guards passed/5 expected failures; [R4 RED](task-08-review-round-4-red.md) — 3/3 ожидаемых failures, включая intermediate Additional→Compile роль и неизвестный MCP journal path. Исправления и fixture corrections явно раскрыты в [R3 GREEN](task-08-review-round-3-green.md), [R4 GREEN](task-08-review-round-4-green.md).
- Всего task-08: **4 раунда / 5 исправлений** (R1-001/R1-002/R1-003/R2-001/R3-001); два pre-review guards и исправление изоляции учитываются отдельно, не как новые finding IDs.
- Public schema/descriptions не менялись: full **55 / 46 574**, lite **16 / 20 178**, byte budgets **50/22 KiB** сохранены, точные assertions проходят в main. Версии **1.6.1 / 1.6.1.0 / 1.6.1.0** согласованы; PackageVersion не добавлена.
- Computer Use/IDE UI не применялись. Живые пробы ниже выполнены последовательно, их временные tracked bytes/mtime восстановлены и новый source удалён **до** финального main. После финальной freeze меняются только evidence/status docs.

## Release solution build

## Build succeeded

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Focused

## Filtered test run

**Filter:** `FullyQualifiedName~EstimateAffectedTests|FullyQualifiedName~VstestTestInventoryTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **68** · Passed: **68** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests.Estimate_is_read_only_core_tool_in_both_profiles_with_only_details_parameter', 'RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests.Estimate_is_read_only_core_tool_in_both_profiles_with_only_details_parameter', 'RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.Cancellation_does_not_publish_a_partial_inventory', 'RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.NUnit_generic_fixture_only_uses_measured_closed_form', 'RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.NUnit_generic_fixture_only_uses_measured_closed_form`

## SourceStructure

## Filtered test run

**Filter:** `FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **123** · Passed: **123** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Unresolved_call_makes_reachability_undecidable', 'RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Crlf_and_lf_texts_produce_the_same_compilation_and_line_numbers', 'RoslynMcpServer.Tests.SourceStructure.WriteBoundaryChecksTests.Comment_and_string_mention_are_not_call_sites', 'RoslynMcpServer.Tests.SourceStructure.WriteBoundaryChecksTests.Second_call_site_fails', 'RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Delegate_assignment_to_a_lambda_body_is_followed`

## Полный main CI suite

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1980** · Passed: **1980** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Logging.HarnessSessionIdTests.Resolve_maps_immediate_parent', 'RoslynMcpServer.Tests.Analyzers.AnalyzerShadowPublicationPlannerTests.Confirmed_overlay_excludes_foreign_and_unconfirmed', 'RoslynMcpServer.Tests.Logging.HarnessSessionIdTests.Current_process_session_id_has_harness_prefix_and_suffix', 'RoslynMcpServer.Tests.TestRunner.TestDllEnsureTests.Missing_solution_does_not_produce_a_test_command', 'RoslynMcpServer.Tests.Logging.HarnessSessionIdTests.Windows_reads_this_process_image_and_command_line`

## Opted-in AnalyzerLifecycle

## dotnet command


### StdOut
```text
Test run for E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\bin\Release\net10.0\RoslynMcpServer.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
Results File: E:\Devel\RoslynMcpServer-rapid-tia\TestResults\task-08-round-4-lifecycle\lifecycle.trx

Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 48 s - RoslynMcpServer.Tests.dll (net10.0)
```

### StdErr
(empty)

## Живое повторение R3-001

Новый stdio server из `bin/Release/net10.0`, `get_mcp_server_info` подтвердил **v1.6.1.0**, lite16. Изолированный `Specs.csproj`: `EnableDefaultCompileItems=false`, единственный source `Cases.cs`, исходный `AdditionalFiles Include="Data/*.cs"`; `Payload.cs` присутствует до baseline. Начальный план пуст. После наблюдённого native удаления и настоящего Debug→Release reload **того же неизменённого project XML** проект остаётся выбранным с конкретным путём дельты. Первичные ответы/driver: `TestResults/task-08-round-4-additional/`, fixture — `spikes/AdapterTestNames/artifacts/task08-r4-live-probe`. Restore assets fixture неполны: это проверка публичной оценки, не запуск адаптера; MSBuild workspace успешно загружен.

### До reload после удаления

```text
Approximate result (rapid TIA): not a substitute for the full test suite or CI.
Changed since load: 0 members in 1 files. Directly selected: 0 tests. Run plan: 1 projects.
Run: `E:\Devel\RoslynMcpServer-rapid-tia\spikes\AdapterTestNames\artifacts\task08-r4-live-probe\Specs.csproj` whole project (project-level).
Reasons: Project expansion without a call graph: E:\Devel\RoslynMcpServer-rapid-tia\spikes\AdapterTestNames\artifacts\task08-r4-live-probe\Data\Payload.cs; Project-level expansion selects the whole project.
Not covered: none.
```

### После настоящего reload

```text
Approximate result (rapid TIA): not a substitute for the full test suite or CI.
Changed since load: 0 members in 1 files. Directly selected: 0 tests. Run plan: 1 projects.
Run: `E:\Devel\RoslynMcpServer-rapid-tia\spikes\AdapterTestNames\artifacts\task08-r4-live-probe\Specs.csproj` whole project (project-level).
Reasons: Project expansion without a call graph: E:\Devel\RoslynMcpServer-rapid-tia\spikes\AdapterTestNames\artifacts\task08-r4-live-probe\Data\Payload.cs; Project-level expansion selects the whole project.
Not covered:
- Reload/reopen observation window: non-C# changes may have been missed. C# sources are rescanned; this non-C# limitation persists for this baseline.
```

## Повторная owner-проба R2-001

Отдельный новый stdio process загрузил настоящий `RoslynMcpServer.sln`, **5 проектов**. После правки существующего теста и создания нового namespaced source с обычным using до reload выбирается whole project с советом reload; после настоящего reload — фильтры. После точного отката и удаления нового файла, с повторным reload, — пустой план. Последующая правка снова даёт фильтр, ещё один откат — пустой план. Не-C# оговорка сохраняется. Drivers/полные ответы: `TestResults/task-08-round-4-owner/`. Большой includeDetails список тестов из предшествовавшего whole-project ответа здесь опущен.

### После reload нового файла

```text
Approximate result (rapid TIA): not a substitute for the full test suite or CI.
Changed since load: 3 members in 2 files. Directly selected: 2 tests. Run plan: 1 projects.
Run: `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj` with filter `FullyQualifiedName~RoslynMcpServer.Tests.Testing.Task08ReloadSmokeTests.|FullyQualifiedName~RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Saved_adapter_names_select_every_measured_case`
Reasons: Changed test-class root: E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\Task08ReloadSmokeTests.cs; Changed test: E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\Task08ReloadSmokeTests.cs; Changed test: E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestRunPlannerTests.cs; VSTest contains filters select a superset; names containing a selected name also run.
Not covered:
- Reload/reopen observation window: non-C# changes may have been missed. C# sources are rescanned; this non-C# limitation persists for this baseline.
```

### После удаления/восстановления и второго reload

```text
Approximate result (rapid TIA): not a substitute for the full test suite or CI.
Changed since load: 0 members in 0 files. Directly selected: 0 tests. Run plan: 0 projects.
Not covered:
- Reload/reopen observation window: non-C# changes may have been missed. C# sources are rescanned; this non-C# limitation persists for this baseline.
```

### Последующая правка

```text
Approximate result (rapid TIA): not a substitute for the full test suite or CI.
Changed since load: 1 members in 1 files. Directly selected: 1 tests. Run plan: 1 projects.
Run: `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj` with filter `FullyQualifiedName~RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Saved_adapter_names_select_every_measured_case`
Reasons: Changed test: E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Testing\VstestRunPlannerTests.cs; VSTest contains filters select a superset; names containing a selected name also run.
Not covered:
- Reload/reopen observation window: non-C# changes may have been missed. C# sources are rescanned; this non-C# limitation persists for this baseline.
```

### Повторный откат

```text
Approximate result (rapid TIA): not a substitute for the full test suite or CI.
Changed since load: 0 members in 0 files. Directly selected: 0 tests. Run plan: 0 projects.
Not covered:
- Reload/reopen observation window: non-C# changes may have been missed. C# sources are rescanned; this non-C# limitation persists for this baseline.
```

Runtime naming/adapter runner не менялись; исторические [19 adapter plans / 61 executions](task-08-live-validation.md) сохранены, не выдаются за новый прогон этой версии.

## SHA-256 окончательной C#/project freeze

- `RoslynMcpServer.csproj`: `4FDBEED2278DBCA710A61BE4681C8111EF4DEE5AEB39E213CCB60F61C8C1B01D`.
- `RoslynMcpServer.Tests/Testing/EstimateAffectedTestsNonSourceReloadTests.cs`: `9D8A3B3AB205905382930D37501F5996E3632E70C97B2B698BDAAF3AD8C0EF37`.
- `RoslynMcpServer.Tests/Testing/EstimateAffectedTestsReloadTests.cs`: `5C4316471E9419C8AF2019D52EDE3FB32D70875D3857FE1D0416332047123412`.
- `RoslynMcpServer.Tests/Testing/EstimateAffectedTestsTests.cs`: `DC3D9AEAE9F04B7052AB7F82EB365BA8ADD425CB6F67F04F3F5871E7E014897D`.
- `RoslynMcpServer.Tests/Testing/TestImpactWatcherFixture.cs`: `02F26DF595D4EE20E4A846E2DC8B681069C716BFDA07BB510317A90BC5F1B3F4`.
- `Services/Testing/Baseline/BaselineSession.cs`: `106F9E3B697351294CFC1B855A53F8A99167F7E8A77371926183AF4D99E114F2`.
- `Services/Testing/Baseline/Models/BaselineSessionSnapshot.cs`: `4EA75497A37BF2CC05E24C98427F04C2AFE915874C9ACD15284076671C72DCB7`.
- `Services/Testing/TestImpactEstimator.cs`: `CF9612D717B079DB0E633C1D044811B73CA01535F6961FFF72A57E0087A5F96B`.
