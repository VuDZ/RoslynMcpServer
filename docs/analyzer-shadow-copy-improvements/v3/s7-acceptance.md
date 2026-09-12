# Приёмка v3 S7 — итоговый runtime

Дата: 2026-09-12. Первый вердикт: **не принято (код v1.3.19, commit `0115790`).**
Поправка 2026-09-12: повтор матрицы на **v1.3.20** (после S8) —
**принимается в исходниках.** Исторический отказ 1.3.19 не переписывается.
Опубликованный MCP process всё ещё **1.3.15**. Исходная серия v1/v2 не
завершена (inaccessible open).
R1–R3, частичный prepare, успешный opt-in, запись, capture gate и
restart-required зелёные. Строка CLR/dependencies красная: private helper
больше не даёт `DependencyUnsupported` / main-only. v3 целиком не принята;
исходная серия не завершена. Inaccessible открыт.
Норматив: [s7-runtime-acceptance.md](s7-runtime-acceptance.md).

Независимый прогон после S6 `0115790`. MCP `run_dotnet_build` Debug
`--no-incremental` success (SDK 10.0.204). Unit-тесты — MCP
`run_specific_test` (`noBuild=true`). Lifecycle — локальный
`C:\Program Files\dotnet\dotnet.exe`, `DOTNET_MULTILEVEL_LOOKUP=0`
(MCP `-32001`). Skip/timeout не было.

| Проверка | Результат |
| --- | --- |
| R1 / freshness | **passed** — `V3_R1_*` + `V3WriteBaseFreshnessTests` 7/7 |
| R2 / fail-closed | **passed** — `V3_R2_*` + `V3PersistentPublicationStateTests` 7/7 |
| R3 / capture | **passed** — `V3_R3_*` + `V3ProvenanceFailureGateTests` 7/7 |
| Частичный prepare | **passed** — `V3PartialPrepareTransitionTests` 5/5 |
| Успешный opt-in | **passed** — `Successful_opt_in_*`; `Epoch5S4AcceptanceTests.Redirected_missing_path_*`; U-ARB-04 overlay |
| Anti-lock | **passed** — U-ARB-04 existing-correct lazy/rebuild (9/9 класс) |
| Запись | **passed** — `WorkspaceWriteBoundaryTests` 17/17; `Epoch4WorkspaceWriteTests` 13/13; `Epoch1WritePathTests` 6/6 |
| Переходы сессии | **passed** — cached true/false/omitted, reset, graph reopen в S3/S4/U-ARB-04 |
| CLR / dependencies | **failed** — restart-required 3/3 Epoch3 зелёные; helper 0/2 |
| Документация R4 | **закрыт** в S6; inaccessible по-прежнему вне матрицы |
| Catalog | **passed** — `Surface_sizes_match_recorded_release_numbers` 63 / 44,503 |

Локальный фильтр S7: **77 passed, 2 failed, 0 skipped**, 13 м 48 с, DLL
`RoslynMcpServer.Tests` Debug net10.0, собранная этим build.

Красные:

- `Epoch3LoaderContractTests.Private_helper_is_refused_and_helper_only_change_does_not_become_main_only_success`
- `Epoch3LoaderContractTests.Two_generators_with_conflicting_helper_versions_are_refused`

Ожидание: `Execution.Status = DependencyUnsupported`, Action main-only,
без Applied. Факт: `LoadFailed`, `reason=opt-in-prepare-not-enabled`,
`dependency` пустой. Overlay/loaded пустые — исполнения helper нет, но
контракт U-ARB-03 / Epoch3 потерян.

Причина в продукте, не во флаке: `AnalyzerShadowPublicationPlanner.Evaluate`
при `appliedCount == 0` и `blockedCount > 0` отбрасывает observation
`DependencyUnsupported` и ставит `LoadFailed` /
`FailureSummary ?? restart?.Reason ?? "opt-in-prepare-not-enabled"`.
Unit-гейт `AnalyzerLoaderContractTests` 5/5 и планировщик 3/3 это не ловят.
S5 этот путь не гонял.

Проверено **в исходниках** v1.3.19 / `0115790`. Опубликованный MCP process
всё ещё **1.3.15** (`get_mcp_server_info`). Publish/reload не делались.

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| V3-R1 | P1 | stale candidate | **закрыт** (S2) |
| V3-R2 | P1 | fail-closed сбрасывается edit | **закрыт** (S3) |
| V3-R3 | P1 | corrupt capture → real | **закрыт** (S4) |
| V3-R4 | Docs | противоречивые live-нормы | **закрыт** (S6) |
| S7-1 | P1 | helper → `LoadFailed` / `opt-in-prepare-not-enabled` вместо `DependencyUnsupported` | **закрыт в S8** (1.3.20) |
| S3-1 / S4-1 / S5-1 / S6-1…S6-3 | Low | прежние leftover | открыты, не чинились |

## Повтор 2026-09-12 на v1.3.20

Тот же фильтр lifecycle, DLL Debug после S8. Combined: **78 passed, 1 failed,
0 skipped**, 13 м 16 с. Единственный fail —
`V3PartialPrepareTransitionTests.Edit_flush_and_reconciliation_keep_partial_admission_without_analyzer_io`
на первом `BuildAsync`: `host-response-timeout` (не assertion контракта).
Изолированный повтор того же теста: **1/1**, 7 с. Epoch3 helper оба **passed**
в combined-прогоне (`DependencyUnsupported`).

Unit на той же DLL: `WorkspaceWriteBoundaryTests` 17/17;
`AnalyzerShadowGenerationPublisherTests` 18/18; `AnalyzerProvenanceBindingTests` 3/3;
`SemanticPublicationStateTests` 5/5; planner **4/4**; loader 5/5; catalog 63 / 44,503.

CLR/dependencies **зелёная**. v3 **принята в исходниках** v1.3.20. Publish/reload
не делались.
