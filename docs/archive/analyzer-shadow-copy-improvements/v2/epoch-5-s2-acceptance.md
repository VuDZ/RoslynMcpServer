# Приёмка E5-S2 — provenance-only matcher

Дата: 2026-09-12. Вердикт: **принимается. Rewrite только Complete+Confirmed
той же load-сессии. Unique-name fallback снят.**
Норматив: [epoch-5-reference-provenance.md](epoch-5-reference-provenance.md) E5-S2,
[F09-PROD-1/2](epoch-5-f09-production-capture-acceptance.md).
Самоотчёт: [epoch-5-s2-results.md](epoch-5-s2-results.md).

Независимый прогон (MCP `run_specific_test`, x64 `C:\Program Files\dotnet`,
SDK 10.0.204):

- `run_dotnet_build` Debug `--no-incremental`: success
- `AnalyzerReferenceShadowCopierTests`: **8 passed / 0 failed**
- `AnalyzerProvenanceBindingTests`: **3 passed / 0 failed**
- `F09ProductionCaptureTests`: **4 passed / 0 failed**
- `Basic_generation_opt_in_load_exact_V1…` (redirected missing path): **pass**, exact `V1`
- `Foreign_existing…`: **pass**, exact `FOREIGN`, 0 confirmed, 0 rewrite
- `Foreign_missing…`: **pass**, missing path сохранён, не `V1`
- `Negative_control_unavailable_generator_fails_oracle`: **pass**, flag off → `no-type`

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| E5-S2 table | — | Confirmed rewrite / skip unconfirmed / keep foreign | **закрыт** |
| F09-PROD-1 | Medium | Join через production snapshot | **закрыт** — redirected V1; multi-TFM 2 rewrite; foreign 0 confirmed |
| F09-PROD-2 | Low | Unique FilePath без output/TFM | **закрыт** — source требует exact output; TFM conflict → unconfirmed |
| Filename fallback | — | Copier + mapping + gate | **снят** |
| Alt-2/3 / inaccessible | — | Не выбирались | **закрыт** |
| E5-S3 | — | Reason codes / schema | **не в скоупе** |
| E5-S2-1 | Low | Второй inner TFM без oracle маркера | **закрыт** — exact `V1` в свежем host |

Версия **v1.3.10**. Публичная MCP schema не менялась. U-ARB-05 sticky не трогали.

---

ID: E5-S2-1
Severity: Low
Depends: E5-S2 multi-TFM

Target:
F09ProductionCaptureTests.Production_snapshot_selects_each_loaded_inner_tfm_for_rollout

Claim:
Два applied rewrite и два shadow path есть; exact `V1` только у `Consumer`.
Второй inner (`net10.0`) не проверен `GeneratedMarker.Version` и не сверён
с двумя `SourceProjectId`.

Resolution:
Тот же production snapshot по-прежнему подтверждает два applied rewrite и два
разных shadow path. После marker `Consumer` отдельный свежий lifecycle-host
загружает ту же multi-TFM solution и первым компилирует `Consumer1`; exact
`GeneratedMarker.Version == "V1"` проходит. Разные host-процессы исключают
ложный отказ epoch-3 из-за двух разных `Generator.dll` с одной CLR identity.

---

## Что дальше

E5-S3: стабильные reason codes (`reference_rewritten`, `source_output_missing`,
`ambiguous_assembly_name`, `proven_foreign_path`, `provenance_unconfirmed`,
`access_failure`, `preparation_failure`). Новая публичная schema не вводится.

Не делать: Alt-2/Alt-3, inaccessible, U-ARB-05 sticky.
