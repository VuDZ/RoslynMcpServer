# Task-09 — focused-проверки исполнителя

2026-11-06; Codex, runtime model ID средой не предоставлен. **Это не финальная acceptance validation.**

До реализации: [RED — 20/20 expected failed](task-09-red.md), после успешной сборки, не compile/fixture failure. Неизменённые legacy tests:17/17 passed.

После реализации последовательно получены:

- 37 tests:31 passed /6 failed (SDK nullable read-only hint, fixture rebuild causing known T04-C3 expansion, optional parser error message, timeout/cancel process tree survivors).
- 43 tests:41 passed /2 failed (старые measured bytes и отсутствие cancellation notification при одном local client CancelAsync). Реальный timeout с остановкой host+ping уже passed.
- Настоящий явный MCP notifications/cancelled через client:1/1 passed, host+ping stopped, second not run, единичный server log.
- 105 tests:103 passed /2 failed (дополнительный catalog byte literal, необязательное сообщение Error из parser). Freshness fixture теперь проверяет маркер нового failing body и Failed:2.
- Расширенный focused:182 tests,181 passed /1 failed /0 skipped. Все task09 execution/cancel/timeout cases и прежние runner/parser/Hosting tests кроме одного catalog assertion прошли. Единственный fail: `McpToolCatalogTests.Surface_sizes_match_recorded_release_numbers`, строка497: expected25234, actual25562. Literal исправлен после этого запуска. Затем добавлены явные asserts PASS/старого инвентаря в stale-DLL case. Финальный код после этих двух последних assertion-правок ещё должен быть собран и проверен координатором.

Точная команда последнего расширенного focused — Roslyn MCP run_test_by_filter, workspacePath=`E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj`, configuration=`Release`, noBuild=false, timeoutSeconds=600, includeFullOutput=true:

```text
FullyQualifiedName~DotNetTestExecutorTests|FullyQualifiedName~EstimateAffectedTestsRunTests|FullyQualifiedName~EstimateAffectedTestsCancellationTests|FullyQualifiedName~RoslynMcpServer.Tests.Hosting|FullyQualifiedName~TestToolsDllBuildTests|FullyQualifiedName~TestToolsRunTestByFilterTests|FullyQualifiedName~TestToolsCancellationOutputTests|FullyQualifiedName~DotNetCliRunner|FullyQualifiedName~VstestOutputParserTests
```

Фактический measured minified tools/list: full55/46902; lite16/20506; lite+files24/25562; lite+editing24/25776; lite+decompile20/23268; lite+nuget22/23885; lite+project19/22026; lite+runtime19/23244; lite+operations20/22267; runtime-enabled files24/25562. Это +328 байт к каждой прежней поверхности.

5 case добавлены после реализации по ручной проверке координатора:3 прежних OCE outputs,2 ранних run responses. Они отдельно отмечены в [отчёте](../task-09-implementation-report.md#tdd-и-проверки).

MCP cancellation проверена реальным tools/call и явным notifications/cancelled с наблюдённым typed request ID. Автоматическая отправка notification на local CancelAsync в pipe-harness не наблюдалась и не объявляется проверенной. Падение теста из-за сообщения Error заменено более прямым freshness proof; прежний parser не менялся. В Windows устранён реально обнаруженный survivor grandchild при root-first tree kill; Linux/macOS не запускались.

SourceStructure/main/AnalyzerLifecycle/Release solution build выполняет координатор после freeze. Counts/limits не ослаблялись; версия1.6.1 и task/index statuses не менялись исполнителем.

После freeze координатор подтвердил Release solution build PASS (3 прежних предупреждения) и более широкий focused **201/201 PASS**, включающий все EstimateAffectedTests классы, parser/runner/Hosting catalogs. Это подтверждает финальные assertions после последнего прогона исполнителя. SourceStructure/main/lifecycle/review на момент этого сообщения ещё выполнялись; их исторический итог — в [проверках до R1](task-09-validation-before-round-2.md). Окончательный код после R1-001 принят по [раунду 2](task-09-review-round-2.md) и [финальной validation](task-09-final-validation.md): focused220/220, SourceStructure123/123, main2004/2004, AnalyzerLifecycle13/13.
