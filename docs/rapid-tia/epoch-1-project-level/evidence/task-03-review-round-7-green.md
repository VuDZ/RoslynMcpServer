# task-03 — R7-001: проверка порядка публикации без UTC-сравнения

Дата: 2026-11-03. Test-only исправление прежним исполнителем эпохи Codex; точный runtime model ID недоступен.

## Исправление

`Normal_publication_waits_until_watcher_startup_finishes` больше не сравнивает `PublishedAtUtc` с отдельным `DateTime.UtcNow`: ранее наблюдалась инверсия UTC, поэтому такое сравнение не доказывает порядок исполнения.

Существующие hooks задают детерминированные точки:

- при удержанном before-start: identity отсутствует, inventory пуст, PublishedAtUtc равен default;
- в after-start hook: те же факты, публикация ещё не произошла;
- после Load и capture: identity есть, timestamp не default, оба документа Known.

Чтения текущего UTC в этой проверке нет; timestamp всё ещё проверяется как часть состояния публикации. Production и test hooks не менялись. Новый искусственный runtime RED не заявляется: это исправление способа доказательства порядка после review finding, а не изменение поведения продукта.

Аудит task-03 baseline tests: оставшийся UtcNow используется в fixture для явно исторического mtime. Сравнения mtime с PublishedAtUtc в отдельных timestamp/event tests проверяют контролируемые данные; они не доказывают порядок двумя wall-clock readings. Других UTC order assertions не найдено.

## Focused GREEN после исправления

Windows Release, специализированный Roslyn MCP run_test_by_filter, noBuild=false. **385/385**, 0 failed: baseline254 + SourceStructure123 + watcher delivery8. Exact inventory16 без изменений.

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **385** · Passed: **385** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes', 'RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary`

### Build warnings

- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Граница validation

CODE FROZEN сообщён сразу после результата; дальше только документация. По сообщению координатора, до этой правки Windows main прошёл1687/1687, Linux main1 и2 — по1683/1683; третий был явно отменён без TRX после выявления R7-001. Эти результаты относятся к прежней заморозке и не объявляются тремя финальными Linux прогонами. Координатор выполняет полную validation повторно после данного freeze. Исполнитель runtime на parent final Linux checkout не запускал.

Изменён единственный C# файл относительно предыдущей заморозки: RoslynMcpServer.Tests/Testing/BaselineObservationGapTests.cs. Версия1.5.18, статусы/spec/README, production и посторонний untracked skill не менялись, commit/push не выполнялись.
