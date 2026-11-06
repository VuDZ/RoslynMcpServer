# Task-11 — сквозная приёмка и документация уровня 0

- Дата подготовки: **2026-11-06** (Europe/Moscow).
- Исполнитель: **Codex**, `/root/task11_implementer`; точный runtime model ID средой не предоставлен.
- База: `0e4d3fa`; последний production-коммит task-09 — `8e103a6`.
- Статус: **accepted повторно, 2026-11-06**, версия **1.7.0**, **2 раунда / 1 исправление**. R1-001 закрыто тем же исполнителем; [раунд 2 — ACCEPT](evidence/task-11-review-round-2.md), [docs-only validation](evidence/task-11-final-validation-round-2.md). Приёмка VSTest-scope эпохи1 восстановлена; task-10 deferred, эпохи2–4 не активированы. Прежние runtime-проверки той же версии кода не выдаются за новые; T09-C1 не исправлялось.
- Scope: существующие сквозные сценарии task-03/08/09, документация уровня 0 и ограничения T09-C1, workflow агента, CHANGELOG и версия **1.7.0**. Task-10 остаётся deferred, эпохи 2–4 не активируются.
- Изменений production C#, тестов, схемы/description инструмента и зависимостей нет. Новых тестов приёмки после реализации нет: обязательные сценарии уже есть в задачах-источниках. Искусственная RED-фаза task-11 по R-16 не создавалась.

## Изменения

- Корневой [README](../../../README.md): обе существующие EN/RU записи инструмента, краткая возможность и история 1.7.0. Указаны baseline сессии, уровень 0 без графа, прямой выбор тестов/проектное расширение, default estimate-only, `run`/timeout/исходы, runtime-имена/надмножество, MTP и обязательная полная валидация.
- [AGENTS.md.sample](../../../AGENTS.md.sample): рабочий цикл «save → estimate → причины/Not covered → run или фильтры»; сохранение baseline после reset/reload; ограничение после сборки; полный suite/CI и отдельные требования проекта. Настоящий `AGENTS.md` не изменялся.
- [ARCHITECTURE](../../ARCHITECTURE.md#rapid-tia-level-0): baseline capture/поколения, estimator/mapper/composer/inventory/planner/executor, отсутствие TIA-графа/его сохранения, ограничения и связь workspace reset с сохранённым baseline. Прежнее общее утверждение об игнорировании build output уточнено для mapped output assemblies.
- [CHANGELOG](../../../CHANGELOG.md): аддитивные `run`/`timeoutSeconds`, ограничение вывода сборки, документация и приёмочная карта.
- [RoslynMcpServer.csproj](../../../RoslynMcpServer.csproj): только `Version=1.7.0`, `AssemblyVersion=1.7.0.0`, `FileVersion=1.7.0.0`. Координатор сначала подтвердил зелёную Release solution build на базе. Minor требуется для optional parameters по [политике 1.x](../../mcp-tool-surface-evolution/README.md#фиксированные-ограничения-1x), хотя код task-11 сам их не добавляет: task-09 оставила версию 1.6.1. `PackageVersion` не добавлялась и не изменялась.
- В строке R-18 [программы](../README.md#решения-владельца-2026-10-29) уточнён первый пункт owner-замера по уже принятому решению: частота post-run whole-project и его стоимость. Требования и критерии возврата не менялись.

Публичная поверхность остаётся **55 tools / 46 902 bytes full**, **16 / 20 506 lite**, minified UTF-8 `tools/list` по task-09. Это прежние измерения; task-11 не заявляет новый замер схемы. Exact inventories SourceStructure (16 известных identities/kinds/paths) не менялись и не ослаблялись.

## Карта обязательных сквозных сценариев и RED provenance

Все имена ниже — текущие тесты в `RoslynMcpServer.Tests`; приёмочная группа запускает их вместе. Реальные SDK-проекты/solution загружаются через `SolutionManager`/MSBuild. Прямой вызов handler в сценарном тесте не выдаётся за protocol transport; настоящий transport назван отдельно.

### Baseline — task-03

Источник: [BaselineSessionIntegrationTests.cs](../../../RoslynMcpServer.Tests/Testing/BaselineSessionIntegrationTests.cs), [реализация task-03](task-03-implementation-report.md#tdd-и-фактические-проверки).

- `External_edit_before_first_read_is_unknown_and_immediately_touched`: настоящий watcher, внешняя правка при before-read barrier, путь Unknown и в touched.
- `Mcp_and_delivered_disk_edits_have_the_same_pending_baseline_outcome(bool mcp)`: оба пути записи с детерминированной доставкой события, без зависимости от гонки watcher.
- `Timestamp_guard_works_without_any_watcher_event`: watcher подавлен, timestamp после публикации отдельно приводит к Unknown.
- `Event_guard_works_when_the_original_mtime_is_preserved`: прежний timestamp сохранён, доставленное событие отдельно приводит к Unknown.
- `Write_between_hashing_and_commit_cannot_publish_the_old_version`: before-commit barrier исключает публикацию старой версии.
- `Csproj_reload_keeps_known_hashes_touched_paths_and_stable_document_keys`: настоящий MSBuild reopen с новыми Roslyn IDs, прежние ключи/снимок и touched сохранены.
- `Same_solution_reopen_rejects_late_tail_but_preserves_already_published_hashes`, `Reset_returns_without_waiting_and_same_solution_reload_does_not_recapture_unknown_tail`, `Reset_before_first_read_freezes_the_published_inventory_as_unknown`: Known сохраняется, Pending становится Unknown, старый worker не дописывает хвост.
- `Different_solution_discards_old_inventory_and_old_generation_completion`: новый solution отделён от завершения старого поколения.
- `External_edit_in_the_reset_watcher_gap_is_a_rescan_candidate_with_preserved_baseline`: изменение Known source в окне reset не стирает original baseline.

**RED provenance:** эти сценарии входят в [первоначальные 16 cases task-03](evidence/task-03-red.md): **0 passed / 16 expected failed**, после успешного реального MSBuild load, `NotImplementedException` компилируемого stub. Поздние linked-context equivalence/opt-in tests отдельно помечены в отчёте источника; task-11 не приписывает их первоначальному RED.

### Публичная оценка — task-08

Источник: [EstimateAffectedTestsTests.cs](../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsTests.cs), [отчёт task-08](task-08-implementation-report.md#tdd-и-проверки).

- `Real_MSBuild_disk_and_Mcp_edits_select_the_dependent_test_project(bool mcp)`: обе настоящие записи выбирают зависимый тестовый проект через публичный tool handler.
- `Pending_baseline_is_unknown_and_project_expanded_even_before_capture_finishes`: запрос не ждёт capture и честно расширяет Unknown.
- `Restored_source_and_trivia_only_edits_are_unchanged`: отсутствие ложной C# дельты при возврате/только trivia.
- `Empty_result_has_warning_zero_changes_not_covered_and_project_level_state`, `No_workspace_and_cancelled_responses_begin_with_the_warning`: обязательный формат в пустом/раннем ответе.

**RED provenance:** исходный [task-08 RED](evidence/task-08-red.md): **49 failed / 0 passed**, 47 ожидаемых missing-behavior failures и 2 отсутствующие регистрации; compile/fixture failures в счётчик не входили.

Дополнительная реальная reload-регрессия: [EstimateAffectedTestsReloadTests.cs](../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsReloadTests.cs). `Native_new_source_reload_returns_to_filters_without_independent_projects`, `Native_new_source_deleted_and_test_restored_have_an_empty_plan_after_real_reload`, `Native_project_edit_reload_keeps_its_owner_closure_without_independent_projects` и оба cases `Same_solution_reopen_preserves_CSharp_edits_in_the_window_and_after_reload` проверяют настоящий reopen (`LastLoadReopenedGraph`, не cache hit). `True_observation_gap_still_expands_every_project_after_a_real_native_reload` и `Snapshot_capture_failure_remains_a_true_gap_after_a_real_reload` оставляют настоящие gaps консервативными. Это **RED перед исправлением R2-001**, а не первоначальные тесты task-08: [RED раунда 3](evidence/task-08-review-round-3-red.md), [причины и результаты](task-08-implementation-report.md#r2-001--исправление-для-последнего-раунда-3).

[EstimateAffectedTestsNonSourceReloadTests.cs](../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsNonSourceReloadTests.cs) отдельно сохраняет удалённый original AdditionalFile с расширением `.cs` и unmapped journal path после reload. Эти три cases написаны **перед исправлением R3-001**, [RED раунда 4](evidence/task-08-review-round-4-red.md); они не объявляются первоначальными RED cases.

### Фильтр, свежая сборка, исходы и отмена — task-09

Источник: [EstimateAffectedTestsRunTests.cs](../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsRunTests.cs), [TestImpactRunFixture.cs](../../../RoslynMcpServer.Tests/Testing/TestImpactRunFixture.cs), [отчёт task-09](task-09-implementation-report.md#tdd-и-проверки).

- `Real_filter_and_run_execute_the_same_expected_tests_with_only_name_substring_extras`: возвращённый фильтр действительно выполняется через `RunTestByFilter` (`run_test_by_filter` handler), затем тот же набор через `run: true`. Маркеры доказывают First1/First2 и допустимый FirstExtra, без Other/Second. Ручной replay использует `noBuild: true`, чтобы до `run` не создать известное расширение T09-C1.
- `Run_builds_changed_sources_instead_of_passing_on_stale_binaries_and_continues_after_test_failure`: старые DLL реально проходят вручную; `run` исполняет новый failing body, получает Failed:2 и продолжает второй проект.
- `Build_failure_does_not_start_old_tests_and_the_next_project_runs`: build failed с CS0103, старые тесты не запущены, следующий проект выполнен.
- `Whole_project_plan_runs_without_a_filter`, `Timed_out_project_is_not_a_failed_test_and_the_next_project_runs`, `Empty_run_has_no_process_reports_and_keeps_the_warning_and_limitations`: отдельные whole-project/timeout/empty исходы; timeout проверяет остановку testhost и его дочернего процесса.
- [EstimateAffectedTestsCancellationTests.cs](../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsCancellationTests.cs): `Tools_call_cancellation_kills_the_process_tree_skips_later_projects_and_logs_all_outcomes` использует настоящий MCP client/server transport из текущей production assembly, `tools/call` ID и `notifications/cancelled`. Проверяются оба PID, отсутствие второго проекта, единичный outcome log и живой последующий запрос. Доставка ответа на отмену не требуется.
- [DotNetTestExecutorTests.cs](../../../RoslynMcpServer.Tests/TestRunner/DotNetTestExecutorTests.cs): `Execution_returns_an_unambiguous_outcome` отдельно покрывает build/test timeout/cancel, failure и zero-test outcome; это проверки исполнителя с runner seam, не отдельные реальные двухпроектные build-timeout/cancel интеграции.

**RED provenance:** [исходный task-09 RED](evidence/task-09-red.md): **20 failed / 0 passed**, 11 executor cases, 6 MSBuild run cases, 1 MCP cancellation и 2 schema cases. Пустой исполнитель/игнорируемые `run`/timeout и прежний readOnly descriptor давали ожидаемые failures. Поздние 3 legacy cancellation checks и 2 early-run checks помечены в [отчёте источника](task-09-implementation-report.md#tdd-и-проверки), не приписываются первоначальному RED. `Truncated_failure_output_keeps_its_budget_without_suggesting_unsupported_parameters` добавлен **перед R1-001 fix**: [отдельный RED](evidence/task-09-review-round-1-red.md).

## Совместный focused-прогон

Окончательный фильтр для `run_test_by_filter` на `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`, `configuration: Release`, `noBuild: true`, после финальной solution build:

```text
FullyQualifiedName~RoslynMcpServer.Tests.Testing.BaselineSessionIntegrationTests|FullyQualifiedName~RoslynMcpServer.Tests.Testing.EstimateAffectedTests|FullyQualifiedName~RoslynMcpServer.Tests.TestRunner.DotNetTestExecutorTests|FullyQualifiedName~RoslynMcpServer.Tests.Testing.VstestTestInventoryTests|FullyQualifiedName~RoslynMcpServer.Tests.Testing.VstestRunPlannerTests|FullyQualifiedName~RoslynMcpServer.Tests.Hosting.EstimateAffectedTestsCatalogTests
```

Он дополнительно включает публичные adapter replay cases, runtime inventory/planner и schema/descriptor `core/lite`. Replay использует сохранённые реальные adapter FQN task-06 и source fixtures, но не заменяет новый живой запуск четырёх адаптеров: прежний [живой замер task-08](evidence/task-08-live-validation.md) остаётся историческим evidence.

Совместный focused на окончательном восстановленном коде **1.7.0 — 145/145 PASS**. Координатор повторно собрал Release solution после owner-пробы и выполнил SourceStructure **123/123** (16 прежних limits), весь Windows main **2004/2004** и opted-in AnalyzerLifecycle **13/13**; [финальная validation](evidence/task-11-final-validation.md). Все прогоны ненулевые, без failures/skips/timeouts. Историческая [task-09 validation](evidence/task-09-final-validation.md) и [Linux1996/1996](evidence/task-09-review-round-2-claude.md) относятся к1.6.1/8e103a6; Linux/macOS на1.7.0 не запускались. Независимое ревью — [ACCEPT](evidence/task-11-review-round-1.md), финальные task/epoch/program/index статусы согласованы координатором.

## T09-C1 и замер владельца R-18

Владелец выбрал **DOCUMENT/MEASURE**, без исправления механизма и без возврата task-09. [Исходное воспроизведение второго рецензента](evidence/task-09-review-round-2-claude.md#t09-c1--после-собственного-run-проект-до-конца-baseline-идёт-целиком): на двухпроектной fixture `run` исполнил 3 теста, следующий estimate выбрал First целиком по пути DLL, настоящий reload и откат этого не сняли. Это proof огрубления на fixture, не измерение цены на реальном проекте владельца. Упомянутые там «около 2000 тестов и 10 минут» сами по себе не считаются новым измерением task-11.

Первый пункт R-18: доля последовательностей, где после `run` появляется whole-project из-за перезаписанного output assembly, и цена последующего прогона. [Ограниченная owner-проба](evidence/task-11-owner-measurement.md), [полные ответы и timings](evidence/task-11-owner-measurement.json) на этом репозитории выполнена после зелёной сборки 1.7.0: отдельный stdio MCP **1.7.0.0**, исходный estimate **0/0/0**, точная обратимая правка выражения существующего `Saved_adapter_names_select_every_measured_case`, фильтр только этого метода, один `run: true` — **16/16 PASS за 5.164 s** целого вызова со сборкой. Следующий estimate расширился до whole-project по test DLL; reset/reload и откат этого не сняли. После отката **0 members / 1 file / 1 project**. Измеренная доля — **1/1**, только в этой последовательности; по другим owner solutions и рабочим сессиям вывода нет. Восстановление bytes/hash/mtime и shutdown сервера exit 0 подтверждены; driver удалён, source helpers не добавлялись. Координатору передан runner slot для rebuild исходных bytes и финальных проверок.

Нефильтрованный whole-project через TIA не запускался: он включил бы также категории, исключённые main CI; его стоимость **не измерена**. Цена main CI из [финальной validation](evidence/task-11-final-validation.md) — **202.731s** wallclock полного MCP call для2004/2004; она не равна цене нефильтрованного whole-project. Первый sandboxed initial load не подключился к BuildHost pipe до source mutation; успешная повторная проба вне sandbox и исходная неуспешная попытка обе сохранены в evidence.

## Границы приёмки

- Не измерены recall, частота пропуска тестов на истории, долговременная частота gaps/расширений, влияние на память/первый поиск и другие owner solutions. Это не запускает эпоху 2.
- Нет нового живого adapter sweep или отдельной реальной интеграции отмены во время build / timeout build с двумя проектами / пользовательских buildArgs; исходные task-09 limits не скрываются executor unit cases.
- Автоматическое protocol notification при одном лишь локальном client cancellation token не подтверждено. Настоящий тест отправляет notification явно.
- SourceStructure inventory не редактировался. Публичные tools/counts/schema и ограничения runtime names не расширялись. MTP остаётся на существующем пути, task-10 deferred.
- Commit/push/release исполнитель не выполняет; посторонняя untracked `.agents/skills/prepare-github-release/` не изменялась. Финальная приёмка и согласованные статусы принадлежат координатору.

## Исправление R1-001 перед раундом 2

[Второй рецензент раунда 1](evidence/task-11-review-round-1-claude.md#r1-001--фильтр-через-run_test_by_filter-по-инструкции-идёт-на-старых-бинарниках) обнаружил неверную инструкцию после сохранённых правок: ручной `run_test_by_filter(project, filter)` по умолчанию пропускает сборку и может пройти на старой DLL. Владелец переоткрыл task-11; исправление на базе `45ea344` выполнил тот же Codex `/root/task11_implementer` (точный runtime model ID средой не предоставлен).

Исправлены ровно три workflow-места: существующие EN/RU записи `estimate_affected_tests` в README и `AGENTS.md.sample` → During edits. Теперь ручной запуск возвращённого фильтра требует явный `noBuild: false` для сборки текущих исходников; сказано, что omit пропускает сборку, а такая ручная сборка может вызвать T09-C1, как `run`. ARCHITECTURE, JIT help и tool description проверены: аналогичной неверной ручной инструкции в них нет, C# строки не менялись. Источник умолчания, существующая stale-DLL регрессия и результаты doc checks — [evidence исправления](evidence/task-11-review-round-2-fix.md).

Изменены только README/sample и Markdown-отчёты. Повторных build/tests, искусственного RED, bump версии или нового runtime-замера нет; прежняя validation не объявляется новым запуском. Свежий [раунд 2 — ACCEPT](evidence/task-11-review-round-2.md) закрыл R1-001; координатор [повторно оформил приёмку](evidence/task-11-final-validation-round-2.md) task-11 и VSTest-scope эпохи1. Счётчики — **2 раунда / 1 исправление**, код/settings и версия1.7.0 сохранены.
