# Приёмка F-09 — production capture/snapshot

Дата: 2026-09-12. Вердикт: **принимается. Snapshot на physical load есть.
Matcher и E5-S2 rollout не открыты.**
Норматив: [epoch-5-f09-capture-design.md](epoch-5-f09-capture-design.md),
[P0 acceptance](epoch-5-f09-p0-acceptance.md).
Самоотчёт: [epoch-5-f09-production-capture-results.md](epoch-5-f09-production-capture-results.md).

Независимый прогон (MCP `run_specific_test`, x64 `C:\Program Files\dotnet`,
SDK 10.0.204):

- `run_dotnet_build` Debug `--no-incremental`: success
- `F09ProductionCaptureTests`: **3 passed / 0 failed**
- `F09CaptureSpikeTests`: **4 passed / 0 failed** (старый spike helper, не
  production `Bind`)
- `SolutionManagerPathResolutionTests`: **4 passed / 0 failed**

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| F09-04 | Low | `Microsoft.Build` в server csproj | **закрыт** — 18.6.3, `PrivateAssets=all`, `ExcludeAssets=runtime` |
| P0-7 | Medium | Lifecycle в `LoadCoreAsync` | **закрыт** — один capture после cache guard; ноль на cache/edit/query/refresh; новый на globals/graph-stale/reset |
| F09-02 | Medium | Always-on | **закрыт** — capture без проверки `shadowCopyInSolutionAnalyzers`; verbosity `Normal` |
| P0-8 | — | Fail-closed + delete temp | **закрыт** — Missing=`Failed`, mixed=`Incomplete` + 0 confirmed; temp=0 |
| Matcher | — | Unique-name | **не менялся** |
| F09-PROD-1 | Medium | Join matrix через production snapshot | **открыт** — lifecycle только `SdkDefaultCorrectPath` + counters |
| F09-PROD-2 | Low | Unique `FilePath` без output/TFM check | **открыт**, не блокер snapshot |

`AnalyzerReferenceShadowCopier` / публичный MCP API не менялись. Версия
**v1.3.9**.

---

ID: F09-PROD-1
Severity: Medium
Depends: P0 join / E5-S2

Target:
AnalyzerProvenanceCaptureService.Bind
F09ProductionCaptureTests

Claim:
Самоотчёт «функциональные P0-регрессии 3/3» — это
`F09CaptureSpikeTests` со своим `CaptureAsync`/`Join`, не
`SolutionManager.AnalyzerProvenanceSnapshot`. Production lifecycle не грузит
redirected fixture, два inner TFM и foreign same-name. P0 join через production
`Bind` этим этапом не доказан.

Suggested change:
E5-S2 обязан утверждать confirmed/unconfirmed bindings из production snapshot
на redirected + multi-TFM + foreign/missing-path. Не считать spike 4/4
доказательством production join.

---

ID: F09-PROD-2
Severity: Low
Depends: F09-PROD-1

Target:
AnalyzerProvenanceCaptureService.SelectProjectByExactOutputs

Claim:
При ровно одном loaded candidate по `Project.FilePath` production сразу
`Confirmed`, без `Identity == OutputFilePath` и без
`NearestTargetFramework`. Spike join требовал output hit. Риск: item другого
inner TFM подтвердится к единственному загруженному проекту.

Suggested change:
Не чинить в этом этапе. E5-S2: если loaded inners > 1 или TFM metadata
противоречит выбранному project — `unconfirmed`, не unique-name.

---

## Что дальше

Отдельный этап E5-S2: смена matcher на confirmed provenance only + marker
acceptance (missing-path и foreign same-name, exact `GeneratedMarker.Version`).
Закрыть F09-PROD-1 на production snapshot, не на spike.

Не делать: Alt-2/Alt-3, inaccessible, U-ARB-05 sticky.
