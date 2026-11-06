# Task-09 — финальная validation и приёмка

- Дата: **2026-11-06**. Координатор: **Codex**, точный runtime model ID средой не предоставлен. Windows, SDK **10.0.402**, runtime **10.0.13**. База `2c16c9e`; версии **1.6.1 / 1.6.1.0 / 1.6.1.0** не менялись: task-09 не запрашивает version bump; release/push не выполнялись.
- **ACCEPT**: [свежий раунд 2](task-09-review-round-2.md); **2 раунда / 1 исправленное замечание**, R1-001 закрыто. [Отчёт реализации](../task-09-implementation-report.md).
- Принятый scope: флаг `run` для существующего VSTest-плана task-07, общий структурированный исполнитель, обязательная свежая сборка, отдельный таймаут каждого build/test процесса, продолжение после failed/build failed/timeout, настоящая MCP-отмена с остановкой дерева и однострочным логом, ограниченный диагностический вывод без неподдерживаемых follow-up параметров. Вывод и значения по умолчанию прежних `run_*` сохранены.
- После последнего исправления C#/tests координатор последовательно выполнил Roslyn MCP **Release solution build PASS**, **focused 220/220**, **SourceStructure 123/123**, **полный main 2004/2004**, **opted-in AnalyzerLifecycle 13/13**. Failed/skipped/timeouts нет; все прогоны ненулевые. Main filter: `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`, как Windows CI. Тесты запускаются с `noBuild=true`, `noRestore=true` на последней Release-сборке.
- Lifecycle вызван через Roslyn MCP `execute_dotnet_command`, поскольку specialized test tools не задают окружение: `--environment ROSLYN_MCP_ANALYZER_LIFECYCLE=1`. TRX `TestResults/task-09-final-lifecycle/lifecycle.trx` отдельно проверен: **total=executed=passed=13**, failed/error/timeout/aborted/notExecuted=0.
- SourceStructure: сохранён **точный инвентарь 16 известных limits**, identities/kinds/paths не менялись, ожидания не ослаблялись. Проверка tracked diff до staging прошла с `core.whitespace=cr-at-eol` (исходное CRLF в `Tools/TestTools.cs`). После staging выявлен единственный formatting nit: дополнительная пустая строка EOF в новом `Services/Build/DotNetTestExecutor.cs`; это не failure build/tests и не основание возврата по R-18. Проверенные C# bytes сохранены.
- TDD: [первоначальный RED](task-09-red.md) — **20/20 expected failed** после успешной компиляции; [R1-001 RED](task-09-review-round-1-red.md) — **2/2 expected failed**, [GREEN](task-09-review-round-1-green.md) — **93/93**. Пять поздних guards и коррекции fixtures раскрыты в отчёте. В main добавлены **24 новых cases**, ещё 2 существующих surface cases обновлены. [Предыдущие зелёные проверки до R1](task-09-validation-before-round-2.md) сохранены как история, не заменяют эту validation.
- Measured minified `tools/list`: full **55 / 46 902**, lite **16 / 20 506**, прирост **328 байт**; бюджеты **50/22 KiB** сохранены, точные assertions прошли в main. Минимальная schema: `includeDetails`, `run`, `timeoutSeconds`.
- Код и тесты после последней сборки не менялись. После validation меняются только evidence/status/index docs. Посторонний untracked `.agents/skills/prepare-github-release/` не включается в commit. Task-10/task-11 не запускались; эпоха целиком не принята.

## Ограничения

Linux/macOS не запускались. Windows snapshot потомков закрывает наблюдённый orphan-сценарий; остановка при OS access-denied/динамических гонках остаётся best effort. Настоящий MCP `tools/call` отменён явным `notifications/cancelled` через MCP client с наблюдённым типизированным ID: оба PID остановлены, следующий проект не стартовал, итог один раз в логе; доставка ответа не требуется. Автоматическая отправка notification при одном client `CancelAsync` в pipe-harness не подтверждена и здесь не заявляется. Существующий parser иногда не извлекает Error message при правильных failed outcome/counters; эта граница сохранена. MTP и последующие эпохи вне scope.

## Release solution build

## Build succeeded

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]
- **warning** `CS8601` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\HostSession.cs(1179,34)` — Possible null reference assignment. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.LifecycleTestHost\RoslynMcpServer.LifecycleTestHost.csproj]
- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Focused

## Filtered test run

**Filter:** `FullyQualifiedName~EstimateAffectedTests|FullyQualifiedName~DotNetTestExecutorTests|FullyQualifiedName~TestTools|FullyQualifiedName~DotNetCliRunner|FullyQualifiedName~VstestOutputParser|FullyQualifiedName~McpToolCatalogTests|FullyQualifiedName~McpToolHelpTests|FullyQualifiedName~BuildOutputReportTests|FullyQualifiedName~TestOutputReportOptions`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **220** · Passed: **220** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Build.DotNetCliRunnerHangTests.FormatHangHints_mentions_timeout_and_zombie_dotnet', 'RoslynMcpServer.Tests.Build.DotNetCliRunnerEnvironmentTests.Missing_processor_architecture_is_filled_only_on_Windows', 'RoslynMcpServer.Tests.Build.DotNetCliRunnerLocaleTests.BuildRunMetadata_uses_the_current_platform_when_the_pin_is_missing', 'RoslynMcpServer.Tests.Build.DotNetCliRunnerLocaleTests.CreateProcessStartInfo_sets_english_cli_locale_and_disables_msbuild_node_reuse', 'RoslynMcpServer.Tests.TestRunner.TestToolsRunTestByFilterTests.RunTestByFilter_rejects_empty_filter`

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

Total: **2004** · Passed: **2004** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.TestRunner.TestFilterHelperTests.BuildFilter_methodOnly_shortName_usesLeadingDotContains', 'RoslynMcpServer.Tests.Workspace.WorkspaceLoadGuidanceTests.IsRoslynMsBuildBuildHostFailure_detects_type_initializer_exception', 'RoslynMcpServer.Tests.Build.MsBuildInstanceSelectorTests.FindLatestSdkDirectory_skips_roots_without_msbuild_and_picks_the_highest_folder_name', 'RoslynMcpServer.Tests.Workspace.WorkspaceLoadGuidanceTests.FormatRoslynMsBuildBuildHostFailureMessage_is_not_sdk_mismatch', 'RoslynMcpServer.Tests.Build.MsBuildInstanceSelectorTests.SelectBest_on_64bit_process_skips_Program_Files_x86_sdk`

## Opted-in AnalyzerLifecycle

````text
## dotnet command


### StdOut
```text
Test run for E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\bin\Release\net10.0\RoslynMcpServer.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
Results File: E:\Devel\RoslynMcpServer-rapid-tia\TestResults\task-09-final-lifecycle\lifecycle.trx

Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 48 s - RoslynMcpServer.Tests.dll (net10.0)
```

### StdErr
(empty)
````

## SHA-256 окончательных C#/test файлов

```text
011487D33F403251BAD125EF6CAE88D84871B978167C8CCDED1153EA532AA16D  Diagnostics/BuildOutputReport.cs
20D72B3F740AFCE772FE49BAF0E20FF5BBFC388A242F35EC195BA3CCA04C7804  Diagnostics/TestOutputReportOptions.cs
DF59A57F4167AC7FAA587A8668083601CA922E0A33B2992221C85222E5D7CD0E  Diagnostics/VstestOutputParser.cs
A8026DD1C0E3AE5E9A736BB63368178B0457EF4909290F7AD2FD81453AE05F96  Hosting/McpToolCatalog.cs
14C4483DF20BC1E8C141A169D1826813BDC945EE9FEE9FF1A4A381A080686CF8  Hosting/McpToolHelpCatalog.cs
F467E9250EE8AD1322C6157517A4399F3D1A9090560CED97841093287675626F  RoslynMcpServer.Tests/Hosting/EstimateAffectedTestsCatalogTests.cs
EEEA4CA816BEFEDA25F2C526F980645EB3EAB86DF2F8F29FA1F473309B3178A6  RoslynMcpServer.Tests/Hosting/McpToolCatalogTests.cs
B73595DF390EE206D730DDF15E3F15B5AA279F65134D5EBE433B18D59F849FFB  Services/Build/DotNetCliRunner.cs
E0FDF98D7CC75078C362DA2A1BDD9321F716307383801B28BC5282A43707F194  Services/Testing/TestImpactFormatter.cs
3DB0D5DDC903CD5C7153486A9AB6B6CADEC9B4A221C0D3897FA1C478C4551C9D  Tools/TestTools.cs
C66F2E6E7C586DBDD07E48D8AC164381AF96A60F0DC03CC265A7E166E8AE4E2D  RoslynMcpServer.Tests/TestRunner/DotNetTestExecutorTests.cs
D70A9552630A8AE0A3365324917CF8B7B2FD9004CBA29CC22A58195DD07CD22E  RoslynMcpServer.Tests/TestRunner/TestToolsCancellationOutputTests.cs
5CA6E93E73B453B5BEED6755BA9ADB10D17CCB0ED44BFF56C380712BCE0ABF85  RoslynMcpServer.Tests/Testing/EstimateAffectedTestsCancellationTests.cs
14D6942E8D392DDD6A179F24BB0D3C89713A164C40768CACEEFA3BD5276E320C  RoslynMcpServer.Tests/Testing/EstimateAffectedTestsRunTests.cs
7C1A62EB87FF33DB2EF08A3791D2AC6FA85A27DC8620424AC6DDCD664360234D  RoslynMcpServer.Tests/Testing/TestImpactRunFixture.cs
08887B7A0FD220590BAB88CB0192C8B07D674F16E54B05598698947E1F5F1C9C  Services/Build/DotNetTestExecutor.cs
969FDF432FD9FEAC55D5D7D4201DAEC98B989715557215D4886EBCB2D484B970  Services/Build/Models/DotNetTestResult.cs
A9A22D4ACC6E5A471B18E248AEAC1E3F56FC9597633FFB58BCCFC0CA6EC83E3A  Services/Build/Models/TestProcessOutcome.cs
```
