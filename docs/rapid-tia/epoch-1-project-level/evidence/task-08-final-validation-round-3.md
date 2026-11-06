# task-08 — validation координатора после R2-001, раунд 3

- Дата: **2026-11-06**, Windows, SDK 10.0.402, runtime 10.0.13; база `1f11b47`, рабочая версия **1.6.1**.
- **Приёмка не объявлена**: [раунд 3 — MUST-FIX R3-001](task-08-review-round-3.md). Исходное R2-001 закрыто; новая потеря удалённого исходного AdditionalFile с расширением .cs остаётся открыта. Дальнейшее исправление/ревью сверх трёх раундов требует решения владельца по R-18.
- Frozen Release solution build PASS, `noIncremental=true`; последующие tests `noBuild=true`, `noRestore=true`. **Focused 65/65, SourceStructure 123/123, main 1977/1977, opted-in AnalyzerLifecycle 13/13**. Три прежних warnings сохранены. Успешные tests не заменяют независимое ревью реальных сценариев.
- Main Windows filter `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`; Linux на этом изменении не запускался. Предыдущая Linux-проверка `98773ad` относится к прежней версии, не к этому fix.
- Lifecycle выполнен дополнительно из-за изменения baseline/load-state поведения. Специализированные test tools не принимают окружение; Roslyn MCP `execute_dotnet_command` с `--environment "ROSLYN_MCP_ANALYZER_LIFECYCLE=1"`, `--filter "Category=AnalyzerLifecycle"`, TRX в `TestResults/task-08-round-3-lifecycle/lifecycle.trx`. Координатор проверил XML counters: total=executed=passed=13, failed/error/timeout/aborted/notExecuted=0.
- SourceStructure exact inventory известных **16** limits, identities/kinds/paths не менялись и не ослаблялись. Прежние Baseline assertions сохранены; [исполнитель](task-08-review-round-3-green.md) дополнительно получил **349/349** Baseline.
- TDD: [RED](task-08-review-round-3-red.md), 7 новых cases + усиленный прежний gap test: 8 executed, 3 passed, 5 ожидаемых failures; namespace-variant 1/1 RED до production fix. Fixture generation noise изолирован только в новых 7 tests, default старых callers сохранён.
- Schema/tool descriptions и каталожные бюджеты не менялись: full 55/46 574, lite 16/20 178. Точные assertions проходят в main. Patch 1.6.1/1.6.1.0/1.6.1.0 согласован после зелёной сборки; PackageVersion не добавлена.
- Computer Use/IDE UI не применялись. Исторические reports прежней приёмки сохранены.

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

Total: **65** · Passed: **65** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests.Estimate_is_read_only_core_tool_in_both_profiles_with_only_details_parameter', 'RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests.Estimate_is_read_only_core_tool_in_both_profiles_with_only_details_parameter', 'RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.Cancellation_does_not_publish_a_partial_inventory', 'RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.NUnit_generic_fixture_only_uses_measured_closed_form', 'RoslynMcpServer.Tests.Testing.VstestTestInventoryTests.NUnit_generic_fixture_only_uses_measured_closed_form`

## SourceStructure

## Filtered test run

**Filter:** `FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **123** · Passed: **123** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Unresolved_call_makes_reachability_undecidable', 'RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Crlf_and_lf_texts_produce_the_same_compilation_and_line_numbers', 'RoslynMcpServer.Tests.SourceStructure.WriteBoundaryChecksTests.Comment_and_string_mention_are_not_call_sites', 'RoslynMcpServer.Tests.SourceStructure.WriteBoundaryChecksTests.Second_call_site_fails', 'RoslynMcpServer.Tests.SourceStructure.SanitizedEntryChecksTests.Task_wait_any_in_sync_entry_fails`

## Main

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1977** · Passed: **1977** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Build.BuildProgressIntegrationTests.Report_swallows_a_failing_progress_channel', 'RoslynMcpServer.Tests.Analyzers.AnalyzerShadowPublicationPlannerTests.Confirmed_overlay_excludes_foreign_and_unconfirmed', 'RoslynMcpServer.Tests.Build.MsBuildInstanceSelectorTests.FindLatestSdkDirectory_skips_roots_without_msbuild_and_picks_the_highest_folder_name', 'RoslynMcpServer.Tests.Build.BuildProgressIntegrationTests.TryCreate_treats_the_sdk_no_op_progress_instance_as_no_progress', 'RoslynMcpServer.Tests.Build.DotNetCliRunnerLocaleTests.BuildRunMetadata_uses_the_current_platform_when_the_pin_is_missing`

## Opted-in AnalyzerLifecycle

## dotnet command


### StdOut
```text
Test run for E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\bin\Release\net10.0\RoslynMcpServer.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
Results File: E:\Devel\RoslynMcpServer-rapid-tia\TestResults\task-08-round-3-lifecycle\lifecycle.trx

Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 48 s - RoslynMcpServer.Tests.dll (net10.0)
```

### StdErr
(empty)

## Живая проверка текущего solution

Новый stdio сервер из `bin/Release/net10.0`, `get_mcp_server_info` подтвердил **Assembly v1.6.1.0** и lite16; загружен настоящий `RoslynMcpServer.sln`, 5 проектов. Новый namespaced `Task08ReloadSmokeTests.cs` с обычным using и правка тела существующего теста доставлены native watcher. До reload — whole project/reload required; после настоящего MSBuild reload — фильтры. После удаления нового файла и восстановления существующего выполнен второй reload — пустой план. Новая последующая правка снова даёт фильтр; повторное восстановление — пустой план. Не-C# оговорка остаётся. Все временные source bytes/mtime восстановлены в finally, новый файл удалён. Полные локальные ответы/driver: `TestResults/task-08-round-3-live/`; ниже сводки без большого includeDetails списка тестов.

### После первого настоящего reload

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

### Повторное восстановление

```text
Approximate result (rapid TIA): not a substitute for the full test suite or CI.
Changed since load: 0 members in 0 files. Directly selected: 0 tests. Run plan: 0 projects.
Not covered:
- Reload/reopen observation window: non-C# changes may have been missed. C# sources are rescanned; this non-C# limitation persists for this baseline.
```

Это подтверждает R2-001, но не отменяет [новый R3-001](task-08-review-round-3.md): исходный globbed AdditionalFile не является новым source, а текущая ветка ошибочно стирает его journal delta после reload. Эта отдельная реальная проба рецензента удерживает задачу в reopened до решения владельца.
