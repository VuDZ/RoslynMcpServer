# Task-09 R1-001 — GREEN

2026-11-06; Codex (runtime model ID средой не предоставлен). [RED:2/2 expected failed](task-09-review-round-1-red.md), исправление после RED.

Исправлена только presentation-возможность общих formatter helpers: BuildOutputReport по умолчанию сохраняет attachment/cursor; TIA передаёт attachFullReport:false. TestOutputReportOptions по умолчанию сохраняет run_* hints; TIA передаёт internal SuppressOutputOptionHints:true. VstestOutputParser меняет только rendering этой action-подсказки, разбор output не меняется. Бюджеты и сообщение о truncation сохранены. Публичная schema estimate_affected_tests по-прежнему ровно includeDetails/run/timeoutSeconds; unsupported instructions не выдаются. Нет markdown text surgery и изменения пользовательского stdout.

Regression guards: прежний настоящий build-failure scenario запрещает reportCursor; новый настоящий xUnit/ITestOutputHelper scenario печатает BEGIN-OUTPUT +6000 символов+END-OUTPUT, падает и проверяет failed outcome, бюджет2500/head+tail, сохранность обоих краёв, отсутствие неподдерживаемых output parameters и отсутствие непрерывного блока3000 символов. Fixture лишь получает ITestOutputHelper через обычный constructor.

Roslyn MCP run_test_by_filter, Release, noBuild=false, timeout600: **93/93 passed,0failed,0skipped**. Включены все EstimateAffectedTestsRunTests, BuildOutputReportTests (включая прежний cursor retrieval), VstestOutputParserTests (включая прежние includeFullOutput hints/budgets), неизменённые TestToolsDllBuildTests/RunTestByFilterTests, поздние точные OCE outputs и schema обеих profiles.

````text
## Filtered test run

**Filter:** `FullyQualifiedName~EstimateAffectedTestsRunTests|FullyQualifiedName~BuildOutputReportTests|FullyQualifiedName~VstestOutputParserTests|FullyQualifiedName~TestToolsDllBuildTests|FullyQualifiedName~TestToolsRunTestByFilterTests|FullyQualifiedName~TestToolsCancellationOutputTests|FullyQualifiedName~EstimateAffectedTestsCatalogTests`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **93** · Passed: **93** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.TestRunner.TestToolsRunTestByFilterTests.RunTestByFilter_rejects_empty_filter', 'RoslynMcpServer.Tests.TestRunner.VstestOutputParserTests.BuildMarkdownReport_includes_partial_status', 'RoslynMcpServer.Tests.TestRunner.TestToolsDllBuildTests.Missing_loaded_solution_does_not_start_a_process', 'RoslynMcpServer.Tests.TestRunner.TestToolsCancellationOutputTests.Runner_cancellation_preserves_the_existing_response_even_without_a_signalled_request_token', 'RoslynMcpServer.Tests.TestRunner.TestToolsCancellationOutputTests.Runner_cancellation_preserves_the_existing_response_even_without_a_signalled_request_token`
````

После GREEN код/тесты заморожены. Финальные Release build, SourceStructure, complete main, AnalyzerLifecycle и независимое ревью следующего раунда — у координатора. До R1 исправления они прошли: [предыдущая validation](task-09-validation-before-round-2.md); это не заменяет проверок исправленного кода. Один новый case повышает ожидаемый main inventory с2003 до2004; прежние expectations не ослаблялись. Version/status/commit не менялись.
