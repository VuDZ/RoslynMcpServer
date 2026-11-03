# task-03 — GREEN исправлений раунда 5

Дата: 2026-11-03. База 31cc8d8, Windows Release, специализированный Roslyn MCP run_test_by_filter, noBuild=false.

После первого freeze полные прогоны координатора выявили ещё два недостатка тестовых предусловий. Они исправлены без production changes; повторный focused GREEN **385/385** приведён в разделе ниже. Предыдущие фазы сохранены как история и не объявляют финальные main suite успешными.

## Повторный GREEN после final-validation доработки

Общие Known-предусловия перенесены в BaselineLifecycleFixture.WriteBaselineSource: A/B всех проектов, включая OtherProject, и Shared.cs получают прошлый mtime. Дублировавшая это настройка только cached-test удалена; BaselineSessionIntegrationTests.cs вновь совпадает с базой31cc8d8. Future/preserved timestamp сценарии явно переопределяют свои времена как прежде.

Directory_rename_inside_a_membership_region_stays_unknown_after_re_read теперь ожидает обе стороны новой уникальной пары area/moved в DirectoryRenamePaths. Standing reason предыдущей операции не служит квитанцией нового события. Текст AfterRenameRead проверяется без ослабления; ordered regression после Semantic_sync повторяет directory step, чтобы stale reason и cached BomFreeAfter заведомо сосуществовали.

После этих edits — **385/385** (baseline254 + SourceStructure123 + watcher delivery8), без падений. Production и exact inventory16 не менялись в этой доработке; CODE RE-FROZEN сообщён сразу после результата. Координатор повторяет полные suites на новой финальной копии.

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **385** · Passed: **385** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash', 'RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary', 'RoslynMcpServer.Tests.Testing.BaselineNarrowingSafetyTests.Only_private_static_initializers_allow_root_narrowing`

### Build warnings

- **warning** `xUnit1031` `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Workspace\WpfTemporaryProjectCallbackTests.cs(195,32)` — Test methods should not use blocking task operations, as they can cause deadlocks. Use an async test method and await instead. (https://xunit.net/xunit.analyzers/rules/xUnit1031) [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj]

## Неудачные полные прогоны первого freeze и дополнительная диагностика

Linux main run1: **1683 executed,1682 passed,1 failed,0 skipped** — Startup_gap_survives_first_publication(missingDirectory:false), оба Other/A.cs,B.cs Unknown. [Полный raw log](task-03-review-round-5-linux/r6-main-run-1-failed.log), [TRX counters и failed result](task-03-review-round-5-linux/r6-main-run-1-failed-trx-summary.xml). TRX start23:27:33.6081288+03, end23:27:33.5162422+03 при duration1.749s: наблюдается инверсия времени. В исходном failure нет timestamp-branch trace, поэтому точная ветка этого конкретного запуска не выдаётся за установленную.

Повторная natural startup диагностика на отдельной Linux-копии: **30 запусков,60/60 cases passed**, исходный main failure не повторился. [Summary](task-03-review-round-5-linux/r6-startup-natural-summary.log). Один последующий контролируемый future-mtime прогон дал **2/2 RED**, с явным timestamp rejected=True для Other/A.cs,B.cs; gap был записан только до публикации. [Полный log](task-03-review-round-5-linux/r6-startup-future-red.log), [диагностический patch](task-03-review-round-5-linux/r6-startup-future-diagnostic.patch). Этот эксперимент подтверждает механизм и необходимое Known-предусловие, но не доказывает ветку исходного main failure; production guard не изменён. Patch исключает только шум NTFS executable bits и EOL из diff, не строки instrumentation.

Windows main первого freeze также упал: **1687 executed,1686 passed,1 failed** — ordered pre-read regression через Directory_rename helper ожидал AfterRenameRead, увидел прежний BomFreeAfter. Standing DirectoryRenameInsideMembership reason мог существовать от другого fact общей fixture, поэтому прежний wait возвращался до новой rename delivery. Сырой MCP output сохраняет координатор. Эти реальные main failures — RED данной поздней test validation доработки, не результаты финальной валидации.

## Итог исполнителя

**385/385**, без падений: 254 baseline cases + все 123 SourceStructure + 8 WorkspaceInputWatcherDelivery cases. Filter: `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`.

Исходный строгий exact inventory SourceStructure — 16 identities/kinds/paths — не менялся; новых limits нет. Все C# и test edits заморожены после этого прогона. Полные Windows build/main/AnalyzerLifecycle и три Linux main suite выполняет координатор после CODE FROZEN; этот документ не объявляет их результат.

## Filtered test run

**Filter:** `FullyQualifiedName~Baseline|FullyQualifiedName~WorkspaceInputWatcherDeliveryTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **385** · Passed: **385** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Valid_documents_without_members_are_not_marked_unparseable', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Moving_declarations_between_import_scopes_changes_member_hash', 'RoslynMcpServer.Tests.Testing.BaselineDocumentSnapshotBuilderTests.Outside_changes_affect_only_their_bucket_and_leave_member_hashes', 'RoslynMcpServer.Tests.Testing.BaselineAddedRootSafetyTests.Nested_type_access_keeps_outer_initializers_and_constructors_ordinary`

### Build warnings

- **warning** `CS8603` `E:\Devel\RoslynMcpServer-rapid-tia\Services\Resolve\NuGetFallbackAssemblyResolver.cs(72,9)` — Possible null reference return. [E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.csproj]

## Linux: R5-002 и контролируемый mtime

Отдельная диагностическая копия Ubuntu-24.04 / SDK10.0.113 на базе31cc8d8, путь /home/runner/roslyn-mcp-rapid-tia-r5-20261103. Использован явно разрешённый WSL integration fallback: специализированный Roslyn MCP runner не выражает Linux/WSL окружение. Final checkout координатора не менялся.

- До коррекции теста: исходный cached-test упал на попытке6 (5 прошли), с диагностикой — на17 (16 прошли), с фазовым trace — на25 (24 прошли). Каждый запуск — ровно1 тест; прогоны завершались при первом падении. [Manifest](task-03-review-round-5-linux/r5-cached-repeat-manifest.log).
- Доказанный путь: cache-hit и generation assertions прошли; оба документа Unknown, RequiresFullScan=false, worker exception отсутствует, watcher Changed/Renamed отсутствуют, исходные mtimes не менялись. Создано: UTC20:17:15.8397766Z и mtime20:17:15.8311164Z. После load: UTC20:17:15.6152615Z, Published20:17:15.6044919Z, mtime по-прежнему20:17:15.8311164Z. Наблюдаемое UTC-время пошло назад; timestamp guard правильно отверг файлы. Источник изменения часов/clock source не устанавливался и не объявляется известным. [Полный фазовый failure log](task-03-review-round-5-linux/r5-cached-phase-25.log).
- Отдельный детерминированный RED: mtime обоих исходников задан на5 минут в будущем до load, прежний Assert.All(Known) падает, cache/generation проходят. [Полный вывод](task-03-review-round-5-linux/r5-cached-future-red.log).
- Первое исправление только cached-test: A/B получили контролируемый mtime на день в прошлом до load; cache/generation/Known assertions сохранены. Production timestamp guard не менялся. Диагностическая Linux-копия с этой коррекцией прошла **20/20** focused повторов; [полный вывод всех20](task-03-review-round-5-linux/r5-cached-past-all.log), [краткий manifest](task-03-review-round-5-linux/r5-cached-past-summary.log). Затем Known-предусловие перенесено в общую fixture, см. повторный GREEN выше. Это не три main suite и не финальная Linux validation.
- [Диагностический patch](task-03-review-round-5-linux/r5-diagnostic.patch) и [временный future-mtime patch](task-03-review-round-5-linux/r5-deterministic-future.patch) — только provenance scratch, не production changes. Полные raw failure logs и GREEN outputs скопированы в Windows evidence до дальнейшей синхронизации. Scratch сохранён.

## Промежуточные фазы и поздние guards

Исходный [RED](task-03-review-round-5-red.md) после очистки read-only Git objects:15 выполнено,1 passed,14 failed. До cleanup correction два Git failures были перекрыты fixture disposal; они не выдаются за поведенческий RED.

Первый combined focused: **382 passed из 383** — единственный failure был missing-directory startup fixture: восстановление переименованного project root само породило post-publication directory gap через parent watcher. Следующий прогон (**383 passed из 384**) выявил Windows IOException при немедленном обратном Directory.Move. Fixture заменён на внешний AdditionalFile point-watch directory: он удаляется до startup, C# файлы остаются доступны и не требуется обратное перемещение project root. Это поздняя fixture correction, не production workaround. После неё **384/384**.

Поздний guard mapped extensionless FILE добавлен после первой реализации strict evidence, чтобы equality в IsPathUnderDirectory не стала доказательством каталога. Он проходил до дополнительной правки, самостоятельный RED не заявляется.

Поздний missing dotted directory case (оба пути отсутствуют, под старым есть mapped document) дал **1/1 RED** до дополнительной baseline-only правки: строгий критерий должен работать независимо от прежней refresh-эвристики. После неё итог **385/385**. Старое refresh/classification поведение не менялось.

Тестовая missing-directory fixture и уточнение Git assert до .git subtree не меняют контракты: обычные bin/obj build-output события остаются в task-03 журнале для будущей task-04 фильтрации. Все шесть ignored rename сегментов покрыты отдельной theory с подавленными native watchers.
