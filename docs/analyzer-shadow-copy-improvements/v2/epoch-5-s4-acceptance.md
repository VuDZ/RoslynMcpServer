# Приёмка E5-S4 — provenance на fixture эпохи 1

Дата: 2026-09-12. Вердикт: **принимается. Missing-path → rewrite + exact `V1`.
Foreign existing → exact `FOREIGN`. Диагностика различает missing output /
unconfirmed / access. Публичная MCP schema не расширена.**
Норматив: [epoch-5-reference-provenance.md](epoch-5-reference-provenance.md) E5-S4.
Самоотчёт: [epoch-5-s4-results.md](epoch-5-s4-results.md). Черновик приёмки
в дереве был самоотчётом; ниже независимый прогон.

Независимый прогон (локальный `C:\Program Files\dotnet\dotnet.exe`,
`DOTNET_MULTILEVEL_LOOKUP=0`, SDK 10.0.204; MCP в этот момент был offline):

- `dotnet build` Debug `--no-incremental`: success
- `Epoch5S4AcceptanceTests`: **6 passed / 0 failed** (53 s)
- `AnalyzerReferenceShadowCopierTests` + `AnalyzerProvenanceBindingTests`:
  **14 passed / 0 failed**

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| E5-S4 missing-path | — | Redirected repro → rewrite + exact `V1` | **закрыт** |
| E5-S4 foreign existing | — | Same-name external → exact `FOREIGN`, path сохранён | **закрыт** |
| E5-S4 foreign missing | — | Missing foreign не `V1`; path сохранён | **закрыт** |
| E5-S4 diagnostics | — | missing output / unconfirmed / access различны | **закрыт** |
| E5-S3-2 | Low | Lifecycle DTO без ReasonCode | **закрыт** |
| E5-S3-1 | Low | `proven_foreign_path` = source project not loaded | **открыт**, не блокер |
| Alt-2/3 / inaccessible | — | Не выбирались | **вне скоупа** |

Версия **v1.3.12**. Открыто, не блокер: E5-S3-1. Inaccessible и sticky не выбирались
этой приёмкой. Эпоха 5 S1–S4 закрыта. Позднее U-ARB-05 выбран как session-sticky
в v1.3.13; U-ARB-04 и inaccessible остаются открытыми.

---

ID: E5-S3-1
Severity: Low
Depends: E5-S3 foreign code

Target:
AnalyzerReferenceShadowCopier.EnumerateReferenceDecisions

Claim:
Эпоха-1 foreign fixture без `MSBuildSourceProjectFile` честно даёт
`provenance_unconfirmed`, не `proven_foreign_path`. Код
`ProvenanceSourceProjectNotLoaded` остаётся для complete snapshot с
source `.csproj`, который не загружен. Это не блокер S4.

Suggested change:
Не чинить в этом этапе. Не менять matcher. Не приравнивать unconfirmed
foreign к proven foreign.

---

## Что дальше

Эпоха 5 закрыта. Открыто, не блокер: E5-S3-1. Inaccessible не открывать.
U-ARB-05 позднее закрыт как session-sticky; открытый gate серии —
U-ARB-04 (load boundary).
F-07, A4-09…A4-12 и A6-13 не чинить без отдельного запроса.
