# Current baseline audit — E0/task-00

Дата: 2026-10-03. Это аудит **текущего production кода**, отдельно от исторического
основания spec и результатов будущего hydrate experiment. `implemented` означает
наличие ordinary path, `partial` — готовое ядро с незакрытой cache delta,
`missing` — отсутствие требуемого path. Code presence не означает test pass.

## Зафиксированные основания

- Исторический base truth: commit `9867318ddb5294ce144bf024b9a61a1a2e3814c3`,
  source **1.3.21**. Он не описывает текущий SolutionManager.
- Audited HEAD: `07859db586c68452771c88ba76409bb78e0e289f`.
  [RoslynMcpServer.csproj](../../../../RoslynMcpServer.csproj): Version **1.5.4**,
  AssemblyVersion/FileVersion **1.5.4.0**, net10.0, Roslyn **5.9.0**,
  Microsoft.Build **18.6.3**, Locator **1.11.2**.
- Начальный `git status --short` **до правок task-00**:
  `?? .agents/skills/prepare-github-release/`,
  `?? .cursor/rules/roslyn-mcp.mdc`, `?? iotester.py`, `?? scripts/`.
  Это пользовательские untracked entries; tracked C# diff отсутствовал.
- Running MCP установлен отдельным `get_mcp_server_info` координатора:
  Assembly **RoslynMcpServer 1.5.4.0**; executable
  `E:\Devel\RoslynMcpServer\bin\Release\net10.0\win-x64\publish\RoslynMcpServer.exe`;
  modified `2026-10-01T17:41:09.1475207+03:00`; cwd `E:\Devel\RoslynMcpServer`;
  workspace loaded **no**; profile **full**, 54 tools. Совпадение версии не
  доказывает совпадения binary с audited HEAD.
- Среда координатора: SDK **10.0.300**, MSBuild **18.6.3+caa81fa49**, Windows
  **10.0.26200**, win-x64, runtime **10.0.8**; VUDZ-PC, Ryzen 7 9800X3D,
  16 logical processors. Physical RAM не установлена (CIM Access denied).
  Это не офисный benchmark bondarev. Подробности и runner output:
  [execution report](task-00-execution-report.md), [test run](task-00-test-run.md).

## A-LOAD — partial; hydrate host — missing

[WorkspaceTools.LoadWorkspace](../../../../Tools/WorkspaceTools.cs) вызывает
[SolutionManager.LoadAndPrepareAsync](../../../../Services/Workspace/SolutionManager.cs#L807).
`EnsureWorkspaceFromConfigAsync` использует тот же path для lazy load.
Один `_workspaceLock` покрывает load, provenance check и prepare; under-lock методы
не захватывают его повторно. Отмена ожидающего load не отменяет владельца.

`LoadCoreAsync` проверяет `MsBuildWorkspaceProperties.MatchesPassedLoadArguments`
и `_projectGraphStale`. На RAM hit вызывает `FlushDirtyDocumentsUnderLockAsync`,
оставляет session и возвращает published snapshot либо raw current solution.
На ordinary reopen **сначала** `StopDiskWatcherUnderLock`, `_workspace?.Dispose()`,
очистка solution/key/dirty queues/non-CSharp text/publication/provenance, новый
LoadSessionId; **затем** MSBuildWorkspace.Create и OpenAndCaptureAsync.
При ошибке open новый workspace dispose, старый уже потерян.

[SolutionManager._workspace](../../../../Services/Workspace/SolutionManager.cs#L3001)
имеет тип **MSBuildWorkspace?**. `CollectDiagnostics` также принимает MSBuildWorkspace.
Production hydrate/DTO/store reader отсутствуют. `GetPublishedSolutionAsync`
получает `_solution` под lock; `GetPublishedSolutionAfterDiskSyncAsync` добавляет
flush в ту же acquisition; `GetSanitizedPublishedSolutionAsync` возвращает search
копию, не заменяя published admission/health. Возврат Solution после release ещё
не является reader lease для старого/candidate host и lazy reads.

**Reuse:** accessors, lock, under-lock seams, lazy/explicit entry, ordinary diagnostics.
**Change:** разделить host ownership/MSBuild operations; строить candidate до
promotion; сохранять старое состояние при same-key failure/cancel согласно
transition table, не выдавать другой key как fallback; переносить session/key/
dirty/publication согласованно. Замена типа поля не устраняет dispose-before-open.
Cache request identity также сохраняет absent/explicit, тогда как текущий RAM
matcher намеренно сохраняет loaded override при omitted.

Anchors в [SolutionManagerPassedLoadArgsTests.cs](../../../../RoslynMcpServer.Tests/Workspace/SolutionManagerPassedLoadArgsTests.cs):
`LoadAndPrepare_omitted_platform_does_not_reopen_or_clear_x64`,
`LoadAndPrepare_different_configuration_reopens_and_keeps_platform`,
`Concurrent_load_waits_and_does_not_cancel_in_progress_load`.
Это ordinary/RAM assertions, не candidate/disk-hit proof.

## A-WRITE — implemented ordinary boundary, partial для hydrate

`ApplySolutionChangesToDiskAsync` берёт manager lock → `ApplyWorkspaceWriteUnderLockAsync`.
До первой записи [WorkspaceWriteBoundary.Preflight](../../../../Services/Workspace/WorkspaceWriteBoundary.cs#L17)
проверяет verified operation context, session/path, raw revision/snapshot identity,
held base, admission/mapping/exclusions. `ClassifyAndInvert` использует
`AnalyzerShadowMapping.InvertKnownReplacements`, восстанавливает excluded references,
отвергает неизвестный analyzer diff. Это exact inverse без temp-prefix эвристики.

Далее `PersistDocumentChangesAsync` → saved paths/texts → единственный production
`TryApplyWorkspaceChanges` → `SetPublishedSolution`. Отмена/ошибка persistence либо
отказ TryApply идут через `FinishAfterSideEffectsAsync`: partial сообщает успешно
записанные paths, файловой транзакции нет. `UpdateDocumentInMemoryUnderLockAsync`
меняет все user memberships физического пути с единым text/encoding; missing path
не создаётся вновь. `SetPublishedSnapshot` → `CreateVerifiedWriteContext` →
`_operationContexts` stamps publication; неизвестный old snapshot даёт unverified.

**Reuse:** preflight/inverse/stamping, partial results, shared update, persistence.
**Change:** Adhoc TryApplyChanges не пишет диск; add/remove/rename `.cs` при доказанном
SDK glob требуют adapter, unsupported explicit inclusion — отказа до записи.
Свой `.csproj` writer не вводить; existing project helpers сохраняют ответственность
и stale behavior. Проверить capabilities, all memberships и cache coverage gate.

[WorkspaceWriteBoundaryTests.cs](../../../../RoslynMcpServer.Tests/Workspace/WorkspaceWriteBoundaryTests.cs):
`Exact_inverse_restores_only_mapped_shadow_and_keeps_unrelated_order_and_multiplicity`,
`Stale_session_is_rejected_before_inverse`,
`Stale_raw_snapshot_identity_is_rejected_even_when_revision_matches`,
`Publication_admission_change_without_raw_mutation_is_rejected`,
`Unknown_operation_context_is_rejected_before_inverse`,
`Production_TryApplyChanges_has_single_SolutionManager_call_site`.
Lifecycle anchor (not-run):
[AnalyzerLifecycleHostTests.Regression.cs](../../../../RoslynMcpServer.Tests/AnalyzerLifecycle/AnalyzerLifecycleHostTests.Regression.cs),
`V3_R1_same_session_stale_candidate_is_rejected_before_any_write`.

## A-STICKY — implemented RAM/session, partial для disk/new process

На cached `shadowCopyInSolutionAnalyzers=false` LoadAndPrepareAsync возвращает
результат без отключения mapping/overlay и без prepare. True проходит existing
prepare/gate; reset и новый key очищают session state. `ClearWorkspaceAsync`
не заменяет manager-owned analyzer loader и не выгружает CLR assemblies.
`CompleteFailedPrepare` сохраняет restart ban либо явно маркирует разрешённую
прежнюю mapping stale; это не blanket success.

**Reuse:** sticky RAM behavior, loader/restart rules. **Missing:** disk base reuse
с независимым overlay mode и новой session; session IDs/mapping не переносить из
DTO. Нужны true→false/omitted/reset/new key/fresh PID assertions на hydrate.
Отдельный current test всей sticky matrix не найден. Lifecycle anchor (not-run):
[AnalyzerLifecycleHostTests.cs](../../../../RoslynMcpServer.Tests/AnalyzerLifecycle/AnalyzerLifecycleHostTests.cs),
`V1_to_V2_cached_and_reset_refuse_execution_process_restart_runs_V2`.

## A-ADMISSION — implemented provenance/publication, missing portable admission

[AnalyzerProvenanceCaptureService.OpenAndCaptureAsync](../../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs#L181)
оборачивает ordinary OpenSolutionAsync/OpenProjectAsync с binary logger.
Replay собирает contexts и только TaskOutput item Analyzer, связывает с текущим
graph; binlog directory удаляет в finally. Это не sdk-project-v1 dependency collector.

[AnalyzerProvenanceCaptureGate.TryGetUnsuitableReason](../../../../Services/Analyzers/AnalyzerProvenanceCaptureGate.cs)
отвергает missing/failed/incomplete/foreign session. Далее manager
`PrepareInSolutionAnalyzerReferencesUnderLock` → planner/gate →
`ApplyPublicationPlan` → `SetPublishedSolution`. `EnterUnavailablePublication`
withholds snapshot; `SetFailClosedPublishedSolution` публикует banned/excluded
состояние, а не произвольный raw graph. `RestoreSafePublishedSnapshotAfterOptInFailure`
работает с текущим host, disposed old не восстанавливает.

**Reuse:** capture binding, execution/publication gates, banned/unavailable behavior,
loader. **Missing:** versioned dependency detector; portable project-path/inner-TFM/
DLL evidence; независимый SDK/pack resolution и повторный DLL content hash перед
новым session-bound snapshot. Замена LoadSessionId в старом snapshot недопустима.
Analyzer capture completeness не равно cache completeness или load health.

Unit anchors (не входят в Workspace filter):
[AnalyzerProvenanceCaptureGateTests.cs](../../../../RoslynMcpServer.Tests/Analyzers/AnalyzerProvenanceCaptureGateTests.cs),
`Session_mismatch_is_unsuitable_even_when_complete`, `Complete_same_session_is_suitable`.
Lifecycle anchors (not-run):
[AnalyzerLifecycleHostTests.Publication.cs](../../../../RoslynMcpServer.Tests/AnalyzerLifecycle/AnalyzerLifecycleHostTests.Publication.cs),
`Production_snapshot_selects_each_loaded_inner_tfm_for_rollout`,
`Replay_failures_publish_fail_closed_status_and_delete_temporary_files`;
Regression `V3_R3_corrupt_capture_does_not_publish_or_execute_real_output`.

## Watcher/shared input — implemented foundation, partial coverage/retention

[WorkspaceInputSession](../../../../Services/Models/WorkspaceInputSession.cs) уже владеет
Generation, input/output/event revisions, membership revision, pending content,
coverage gaps и close admission. PullInputSnapshot не берёт workspace semaphore.
[WorkspaceInputMapBuilder.Build](../../../../Services/Workspace/WorkspaceInputMapBuilder.cs)
сохраняет все occurrences/memberships и создаёт watchers. Однако CreateRegions
задаёт `CompletenessKnown:false`; CreateCategories оставляет unknown для imports,
restore, external glob closure/custom tasks. Walk-up кандидаты не являются evidence
реально разрешённых imports; TFM эвристики не доказывают exact instances/edges.

Callback → QueueDiskPath → WorkspaceDiskEventClassifier.Classify → session event/
dirty queues; ReconcileInputEventsCore читает stable bytes через
WorkspaceInputReconciler; flush применяет известные тексты. Session event revision
сохраняет новое событие при reconciliation. Но FlushDirtyDocumentsUnderLockAsync
снимает dirty source/non-CSharp batches и refresh flag **до** await чтения; возврата
batch при cancellation в методе нет. Pending session notice и pending semantic
flush — разные состояния, это не доказательство полной retention.

**Reuse:** один shared index/session/revisions, role classifier/watchers/read policy.
**Change:** candidate coverage/profile evidence, retention через load/flush/cancel/
reset и old/new session mapping. Второй независимый index не создавать.

[WorkspaceInputMapTests.cs](../../../../RoslynMcpServer.Tests/Workspace/WorkspaceInputMapTests.cs):
`Shared_path_keeps_every_project_and_document`,
`Compile_remove_is_not_a_known_input_and_not_a_proven_exclusion`,
`Real_inner_tfm_load_keeps_one_instance_and_does_not_publish_complete_coverage`;
[WorkspaceInputFreshnessTests.cs](../../../../RoslynMcpServer.Tests/Workspace/WorkspaceInputFreshnessTests.cs):
`Change_during_flush_remains_in_the_snapshot`,
`Late_callback_from_the_old_generation_does_not_change_the_new_snapshot`;
[WorkspaceInputWatcherDeliveryTests.cs](../../../../RoslynMcpServer.Tests/Workspace/WorkspaceInputWatcherDeliveryTests.cs):
`Real_watcher_updates_every_project_that_links_an_external_file`,
`Glob_add_exclusion_delete_and_rename_do_not_write_the_project_file`,
`Explicit_obj_input_is_synced_and_generated_outputs_do_not_bump_input_revisions`.

## Additional/config published text — implemented, hydrate preservation missing

[WorkspaceNonCSharpDiskSync.ReadUpdatesAsync/Apply](../../../../Services/Workspace/WorkspaceNonCSharpDiskSync.cs)
обновляет existing additional/config memberships physical path во всех проектах.
Manager хранит `_nonCSharpTexts` отдельно от raw workspace; SetPublishedSolution
накладывает этот слой до analyzer overlay. Flush вызывает AbsorbNonCSharpTextsAsync;
own writes — PublishOwnNonCSharpTextAsync. Read sync не вызывает TryApplyChanges
и не пишет additional/config bytes обратно.

**Reuse:** published text layer/all-membership apply. **Change:** сохранить слой при
promotion/prepare/refresh и host replacement, связать revisions. Markup/Razor live
text остаётся вне выбранной sync policy. Stale prose CreateCategories («Text sync
... out of scope» для AdditionalFiles) не отменяет существующий production path.

[WorkspaceNonCSharpDiskSyncTests.cs](../../../../RoslynMcpServer.Tests/Workspace/WorkspaceNonCSharpDiskSyncTests.cs):
`Apply_updates_every_project_of_one_path_and_leaves_other_documents`,
`Saved_additional_file_and_editorconfig_reach_the_published_snapshot`;
[NonCSharpDocumentTextSyncSpikeTests.cs](../../../../RoslynMcpServer.Tests/Workspace/NonCSharpDocumentTextSyncSpikeTests.cs):
`Additional_file_text_changes_generated_marker_without_rewriting_csproj`,
`Analyzer_config_text_changes_unused_variable_diagnostic_without_rewriting_csproj`.
Этот spike не заменяет sdk-project-v1 dependency experiment.

## WPF wpftmp — implemented regression, candidate integration pending

QueueDiskPath игнорирует temporary project только если его нет в session.LoadedProjectPaths,
включая missing file. Rename проверяет обе стороны. Directory Changed не создаёт
ложный composition stale от дочернего wpftmp. WorkspaceInputSession.TryRun/Close
не допускает поздний callback после закрытия session.

**Reuse:** filter, loaded set, callbacks и admission. **Change:** сохранить при
candidate watcher promotion. WPF regression не означает admission профилем sdk-project-v1.
[WpfTemporaryProjectCallbackTests.cs](../../../../RoslynMcpServer.Tests/Workspace/WpfTemporaryProjectCallbackTests.cs):
`Temporary_events_use_loaded_membership_even_when_the_file_is_missing`,
`Callback_paused_before_admission_cannot_write_after_reset_and_session_replacement`,
`Callbacks_do_not_acquire_the_workspace_semaphore`;
[WpfTemporaryProjectWatchingTests.cs](../../../../RoslynMcpServer.Tests/Workspace/WpfTemporaryProjectWatchingTests.cs):
`Real_WPF_build_preserves_the_cached_graph_after_flush_and_repeated_loads`,
`Real_project_edit_delivered_during_WPF_build_still_invalidates_the_graph`.

## BOM/own-write — implemented byte protocol, hydrate integration missing

[WorkspaceFilePersistence.WriteTextAsync](../../../../Services/Workspace/WorkspaceFilePersistence.cs)
делает BeginOwnWrite до I/O, PersistedPayload.FromText включает encoding preamble,
File.WriteAllBytesAsync, CommitOwnWrite с **записанным payload**; ошибка — AbandonOwnWrite.
Это не повторное чтение disk bytes после записи. WorkspaceInputReconciler.Reconcile
отдельно читает stable bytes и передаёт начальную event revision в
WorkspaceInputSession.ApplyContentObservation. Matching echo не увеличивает revision;
BOM-only/external overwrite не являются echo. SourceTextEncoding.ResolveForWrite и
manager repeat-write protocol нужны и потому, что MSBuild host повторно пишет Document.

**Reuse:** bytes/preamble, begin/commit/abandon, stable-read/revisions/shared memberships.
**Change:** Adhoc adapter подтверждает только saved paths, сохраняет pending при
cancel/promotion/flush; string equality не заменяет bytes, external overwrite/ABA
не подавляются.

[WorkspaceWriteEncodingTests.cs](../../../../RoslynMcpServer.Tests/Workspace/WorkspaceWriteEncodingTests.cs):
`UpdateDocumentInMemoryAsync_does_not_add_bom_to_bom_free_file`,
`UpdateDocumentInMemoryAsync_keeps_bom_of_bom_file`,
`UpdateDocumentInMemory_echo_does_not_add_a_second_input_revision`;
WorkspaceInputFreshnessTests `Partial_write_records_only_the_saved_path`,
`Event_before_commit_is_not_an_echo_until_the_write_succeeds`,
`Bom_only_difference_is_a_new_input_change`, `Detected_aba_sequence_keeps_the_revision`,
`Unstable_read_and_a_newer_event_stay_pending`;
[WorkspaceSharedSourceSyncTests.cs](../../../../RoslynMcpServer.Tests/Workspace/WorkspaceSharedSourceSyncTests.cs):
`One_disk_read_updates_every_user_document_of_a_shared_path`,
`Project_specific_parse_options_stay_with_their_project`, `Confirmed_text_keeps_the_file_bom_state`.

## Verification delta и outcomes

- **V01:** reuse graph/shared memberships; добавить independent fresh ordinary vs
  hydrate graph/navigation/diagnostic comparator. Watcher tests его не заменяют.
- **V02:** reuse encoding/shared parse-options anchors; добавить полный options/
  characters/round-trip bytes comparator на hydrate.
- **V03:** reuse provenance inner-TFM binding/conservative unknown; exact expected
  instances/edges либо pre-hit reject пока missing.
- **V04:** reuse non-CSharp spikes/admission; добавить generated-document full set/
  text, marker/constant/diagnostics в independent oracle; rejected admission отдельно.
- **V05:** explicit obj role/watcher есть; missing/changed generated compile/config
  ещё должен стать pre-hit miss с hashed/probed evidence.
- **V06:** linked all-membership sync есть; external import/glob evidence отсутствует,
  positive reuse ещё не доказан.
- **V23:** RAM marker/reset есть; disk source/capture/write/readiness, force,
  false→true и fresh PID actual hit отсутствуют.
- Новые assertions **V07–V09/V20–V21**: ownership/prepare/cancel/key change,
  preflight zero-byte-change, partial paths, cancelled flush retention, session
  replacement, additional/config layer, wpftmp, BOM/ABA на обоих host paths.

Координатор выполнил `run_test_by_filter` для RoslynMcpServer.Tests.csproj:
Release, `FullyQualifiedName~RoslynMcpServer.Tests.Workspace`, noBuild=false,
noRestore=true, timeoutSeconds=600. Runner сделал build и test --no-build;
итог **275 total / 275 passed / 0 failed**. Summary не показывает отдельное число
skips; точный raw outcome см. [test run](task-00-test-run.md).
Это focused ordinary Workspace pass, **не hydrate equivalence**, AnalyzerLifecycle
или полный main suite. Main/AnalyzerLifecycle в docs-only task-00 не запускались;
при code/test изменениях обязательна final validation по общим правилам.

Read-only команды аудита: git rev-parse HEAD, git status --short, rg --files,
rg -n и Get-Content перечисленных production/test/spec paths. Поиск sdk-project-v1
вне docs не обнаружил reproducible checked-in harness/raw evidence; пересказ spec
не считается выполненным экспериментом. **sdk-project-v1 experiment: not-run**.
E0/task-02 воспроизводит его с SDK/Roslyn pins, fixtures, commands/raw logs и
supported/unsupported/unknown outcomes, затем дополняет packet task-03.
U-ARB-03 остаётся public activation gate, не блокером технического spike.

Audit — named input E1/task-00/E3/task-00: проектировать по текущим reusable paths
и указанной delta; исторический pin сохранять отдельно.
