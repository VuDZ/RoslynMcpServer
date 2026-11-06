# Task-11 / эпоха 1 — финальная validation и приёмка

> Историческая runtime-validation первого раунда на `13ded97`. Приёмка затем переоткрывалась по R1-001; текущая повторная приёмка — [docs-only раунд 2](task-11-final-validation-round-2.md), **2 раунда / 1 исправление**, код/settings и версия1.7.0 не менялись. Приведённые ниже suites не перезапускались после документационной правки.

- Дата: **2026-11-06**, Europe/Moscow. Координатор: **Codex**, точный runtime model ID средой не предоставлен. Windows, SDK **10.0.402**, runtime **10.0.13**. База `0e4d3fa`.
- **ACCEPT**, task-11 **1 раунд / 0 исправлений**: [независимое ревью](task-11-review-round-1.md). Этот же review закрывает VSTest-scope эпохи 1; отдельный дополнительный раунд не создаётся. [Карта сквозной приёмки](../task-11-implementation-report.md).
- Принятый scope эпохи: baseline и обе защиты его фиксации, same-solution reset/reload, уровень 0, синтаксический состав тестов и VSTest-план, публичный estimate/run, обязательная свежая сборка, отдельные исходы/таймауты/отмена, документация и известные ограничения. Приняты task-01–09 и task-11. Task-10 **deferred**, MTP отдельно не распознаётся; эпохи 2–4 не запускались, TIA call graph/persistence не заявлены.
- Версии **1.7.0 / 1.7.0.0 / 1.7.0.0**. Minor по политике optional parameters для `run`/`timeoutSeconds`, добавленных task-09; bump разрешён после зелёной исходной Release build. В csproj изменены только три version fields; `PackageVersion`, dependencies и tool schema не менялись. Tag/release/push не выполнялись.
- После owner-пробы исходный source точно восстановлен, отдельный сервер завершён; координатор **повторно пересобрал восстановленное решение** и последовательно выполнил Roslyn MCP Release `noIncremental=true` build, **focused 145/145**, **SourceStructure 123/123**, **полный Windows main 2004/2004**, **opted-in AnalyzerLifecycle 13/13**. Все прогоны ненулевые, passed=total, failed/skipped/timeouts=0. Тесты идут `noBuild=true`, `noRestore=true` на окончательной сборке.
- Main filter: `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`, как Windows CI. Полный main MCP call — **202.731 s** wallclock, включая накладные расходы инструмента; это **не** время нефильтрованного whole-project TIA.
- Lifecycle дополнительно проверяет host на новой assembly identity. Roslyn MCP `execute_dotnet_command` использован для `--environment ROSLYN_MCP_ANALYZER_LIFECYCLE=1`, которого нет в специализированных test tools. TRX `TestResults/task-11-lifecycle/lifecycle.trx` отдельно сверен: **total=executed=passed=13**, failed/error/timeout/aborted/notExecuted=0.
- Точный SourceStructure inventory: **16 прежних limits**, identities/kinds/paths сохранены, ожидания не ослаблялись. Новых C#/test изменений и новых tests нет; task-11 по R-16 не создаёт искусственный RED. [Отчёт](../task-11-implementation-report.md#карта-обязательных-сквозных-сценариев-и-red-provenance) разделяет исходные RED задач-источников и поздние guards.
- Measured catalog assertions прошли в main: full **55 / 46 902 bytes**, lite **16 / 20 506**, бюджеты **50/22 KiB**. Это прежняя схема task-09; новых tools/parameters/descriptions task-11 не добавляет.
- SHA-256 окончательного `RoslynMcpServer.csproj`: `9829E84D55AF0172295F6EB0451CD93ED623AB6E0B1DBF89135DE3F5020DD8BB`. После финальной сборки/проверок C#, tests и project settings не менялись; последующие edits — только evidence/status/index docs. Посторонняя untracked `.agents/skills/prepare-github-release/` исключена.

## R-18 / T09-C1: фактический замер и границы

[Owner measurement](task-11-owner-measurement.md) и [полные ответы JSON](task-11-owner-measurement.json): отдельный stdio MCP **1.7.0.0** загрузил настоящий owner repository с пятью проектами. Исходный estimate — 0/0/0. Эквивалентная временная правка одного existing planner test дала фильтр только этого метода; `run` выполнил **16/16 PASS за 5.164 s** monotonic полного MCP call со сборкой. Следующий estimate выбрал test project целиком по переписанному DLL; reset/reload и точный откат source этого не сняли. Наблюдение **1/1** относится только к этой последовательности, без вывода о популяционной/рабочей частоте. T09-C1 остаётся принятым ограничением выбора и стоимости, mechanism fix не выполнялся.

SHA-256 source до/после: `25ab0649527040144545c5843921cf209d87efe6b02cd333b1856090010a7677`; `mtime_ns` до/после **1793961299408103400**. Проба восстановила source в `finally`, независимый рецензент подтвердил bytes/mtime/совпадение с базой; driver и backup удалены, сервер exit 0. Первичная sandbox named-pipe ошибка MSBuild load была **до** mutation/test run и сохранена отдельно, не выдана за PASS/второй sample.

Нефильтрованный whole-project не выполнялся: он включил бы специальные категории, исключённые main CI. Его цена не измерена. **202.731 s / 2004 main cases** — отдельный ориентир стоимости CI, не подмена цены TIA whole-project. Долговременная частота gaps/whole-project, recall на истории, другие owner solutions и новые живые sweeps четырёх адаптеров не измерены.

Linux/macOS на 1.7.0 не запускались. [Linux 1996/1996 второго рецензента task-09](task-09-review-round-2-claude.md) относится к `8e103a6` и остаётся историей. Автоматическая отправка notification по одному client CancelAsync не подтверждена; настоящий transport тест явно отправляет `notifications/cancelled`. OS termination остаётся best effort; parser иногда даёт failed counters без Error message. Эти ограничения не скрыты и не отменяют независимый полный suite/CI.

## Release solution build

## Build succeeded

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Совместный focused-прогон

## Filtered test run

**Filter:** `FullyQualifiedName~RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests|FullyQualifiedName~RoslynMcpServer.Tests.Testing.EstimateAffectedTests|FullyQualifiedName~RoslynMcpServer.Tests.TestRunner.DotNetTestExecutorTests|FullyQualifiedName~RoslynMcpServer.Tests.Testing.VstestTestInventoryTests|FullyQualifiedName~RoslynMcpServer.Tests.Testing.VstestRunPlannerTests|FullyQualifiedName~RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **145** · Passed: **145** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.NUnit_generic_type_arguments_alone_do_not_mean_fixture_constructor_arguments', 'RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Unresolved_runtime_identity_runs_the_whole_project_with_the_supplied_reason', 'RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Contains_filters_include_CheckExtra_and_union_names_and_reasons', 'RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Project_expansion_dominates_selected_tests_without_requiring_runtime_inventory', 'RoslynMcpServer.Tests.Testing.VstestRunPlannerTests.Class_collapse_happens_before_applying_the_filter_budget`

## SourceStructure

## Filtered test run

**Filter:** `FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **123** · Passed: **123** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Unresolved_call_makes_reachability_undecidable', 'RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Crlf_and_lf_texts_produce_the_same_compilation_and_line_numbers', 'RoslynMcpServer.Tests.SourceStructure.WriteBoundaryChecksTests.Comment_and_string_mention_are_not_call_sites', 'RoslynMcpServer.Tests.SourceStructure.WriteBoundaryChecksTests.Second_call_site_fails', 'RoslynMcpServer.Tests.SourceStructure.SourceSetAnalysisTests.Delegate_assignment_to_a_lambda_body_is_followed`

## Полный Windows main CI suite

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **2004** · Passed: **2004** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.SourceTextEncodingTests.ForDiskPath_returns_bom_free_utf8_for_absent_file', 'RoslynMcpServer.Tests.TestRunner.TestAssemblyPathResolverTests.TryResolve_fails_when_binaries_path_is_not_a_directory', 'RoslynMcpServer.Tests.NuGet.NuGetAuditReportParserTests.FormatMarkdownReport_empty', 'RoslynMcpServer.Tests.Build.BuildProgressIntegrationTests.Report_swallows_a_failing_progress_channel', 'RoslynMcpServer.Tests.Hosting.ServerGcConfigurationTests.Server_runtimeconfig_selects_server_gc`

Время полного MCP call: **202.731 s**.

## Opted-in AnalyzerLifecycle

````text
## dotnet command


### StdOut
```text
Test run for E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\bin\Release\net10.0\RoslynMcpServer.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
Results File: E:\Devel\RoslynMcpServer-rapid-tia\TestResults\task-11-lifecycle\lifecycle.trx

Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 48 s - RoslynMcpServer.Tests.dll (net10.0)
```

### StdErr
(empty)
````
