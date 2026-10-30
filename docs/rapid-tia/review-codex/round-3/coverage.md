# Coverage — round 3

## Reviewed target

- Полностью прочитан docs/rapid-tia/README.md: **699 строк**, включая новые run/MTP/failure sections и все эпохи.
- Дата: **2026-10-30**. Версия: **185d3e1bcaa1c876a5f5ce3964ee218ced542da0**. Рабочий diff перед review пуст; git pull --ff-only origin rapid-tia подтвердил актуальность.
- SHA-256 README до учётных правок: **B6EB024A4DA3DED818375D9E2E7C1BCECB2207C5C77454ECA72F6BD007C080C8**.
- Рецензент: **Codex; точный runtime model ID средой не предоставлен**.
- Scope: полный третий review specification и перепроверка собственных семи IDs с учётом разрешённых авторских ответов. Это продолжение review, не fresh blind session.

## Context inspected

- AGENTS.md, docs/code-style.md, правила статусов/счётчиков в docs/README.md.
- Собственные review-codex раундов 1 и 2, особенно F-005/F-006 и прежние API-пробы.
- Ответы автора раундов 1/2: общие решения владельца и секции Codex; validation-2026-10-30.md — авторский контекст.
- docs/ARCHITECTURE.md: workspace/disk boundary, project reload, build/test pipeline, cancellation, bounded output и catalog ownership.
- docs/test-runner-selection/epoch-1-explicit-runner.md целиком; README соседней программы — planned status и границы. Существующие ограничения MTP приняты, резервная дизъюнкция не предполагается доступной.
- docs/mcp-tool-surface-evolution/README.md: политика добавления tools/optional parameters и minor version.
- docs/user-data-directory/README.md, PRIVACY.md: локальные данные и зависимость эпохи 4, без приёмки другой ветки.
- Services/Testing/TestFilterHelper.cs, TestAttributeMatcher.cs, релевантные участки TestDiscoveryHelper.cs.
- Tools/TestTools.cs: noBuild defaults, RunTestByFilter, общий executor, pre-build, test output и cancellation.
- Services/Build/TestBuildRoute.cs, Models/TestBuildPolicy.cs, DotNetTestArguments.cs и DotNetCliRunner.cs — соответствующие build/cancel paths.
- Services/Workspace/WorkspaceInputMapBuilder.cs, WorkspaceDocumentDiskSync.cs, Services/Models/InputOccurrence.cs, LoadedProjectInstance.cs и StateCache/Models/SnapshotProjectKey.cs — membership и owner identity.
- Services/Navigation/SearchIndexPrewarm.cs и SearchIndexPrewarmTests.cs — источник фонового паттерна и cancel contract.
- RoslynMcpServer.Tests/TestRunner/DotNetTestArgumentsTests.cs, TestToolsRunTestByFilterTests.cs, TestToolsDllBuildTests.cs; Build/DotNetCliRunnerHangTests.cs — соответствующие cases и контракты.
- RoslynMcpServer.Tests/Support/McpInitializeHandshake.cs и версии пакетов RoslynMcpServer.csproj.

## Context not inspected

- **Ни один каталог review-grok* не открывался**, включая их findings, result, coverage и execution markers.
- Упоминания/ссылки/счётчики Grok в общих README сохранены как существующая история и не использованы для получения findings или закрытия чужих замечаний.
- Чужие defence/arbitration/decision ledgers не читались; сведения reviewers не объединялись.
- Не проверялись production-код другой ветки usage-statistics, весь upstream fork и весь репозиторий.

## Source/code verification

- Все семь прежних замечаний перепроверены по текущему нормативному тексту; F-005/F-006 закрыты на уровне specification.
- Проверены все направления плана: requirements/scope, acquisition baseline, new/deleted roots, member/binding invalidation, RTA/CHA, cached freshness, generation/reset, persistence, tool surface, filtering, MTP execution/failures и validation/epochs.
- Выполнена in-memory Roslyn 5.9.0 проба instance-over-extension между App и Tests с двумя App syntax trees. Во всех компиляциях 0 ошибок, проверки владельца target прошли. Вывод и методика сохранены в [recheck.md](recheck.md).
- F-009 проверен по точным source/defaults и существующему unit-test contract; тесты не запускались. Отсутствующая политика нового run не приписывается уже написанной реализации.
- F-010 проверен по [MCP cancellation 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/cancellation) и [C# SDK v2.2.0](https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/v2.2.0/src/ModelContextProtocol.Core/McpSessionHandler.cs). Точная версия SDK закреплена в проекте; поддержка названной версии MCP отражена локальным handshake helper.
- Неподтверждённые кандидаты не превращались в findings: MTP cost constants уже помечены оценкой с планом замера; accepted unknown baseline и reflection limits не переоткрывались; отложенные O-* не объявлены дефектами сами по себе.

### Проверка каталога и учёта

- Проверены обязательные секции и уникальные новые IDs F-008/F-009/F-010: 2 major, 1 minor.
- Семь первоначальных IDs в recheck.md имеют закрытый статус; подтверждения F-005/F-006 отделены от новых замечаний.
- Проверены 15 относительных ссылок в собственных report-файлах, отсутствие trailing whitespace и git diff --check.
- Нормативный текст README построчно совпадает с 185d3e1. Изменены только шапка статуса и раздел учёта, плюс строка темы в docs/README.md.
- Scope подготовки коммита — только собственный каталог round-3 и два учётных README; исторические и чужие review-файлы не включаются в изменения.

## Coverage limitations

- Rapid TIA ещё не реализован. Полный review документа не означает полный runtime-прогон всех заявленных сценариев.
- Не проводились end-to-end MSBuild/TIA/MTP или MCP cancellation tests, проверка старых binaries под новым run и измерения recall/CPU/памяти.
- API-проба F-008 проверяет compiler binding, а предполагаемый пропуск TIA — логический walkthrough описанного алгоритма.
- C#-файлы, tests, project/build settings не создавались и не менялись. Release build, SourceStructure, main CI и AnalyzerLifecycle не запускались; эти проверки остаются обязательными для будущей реализации.
- Изменения этого раунда ограничены собственным review-каталогом и учётными полями README/индекса. Нормативный план и авторские ответы сохранены.
- Из статистики других reviewers не выводится новое суммарное число уникальных исправлений: без их чтения/сведения дедупликация не выполняется.
