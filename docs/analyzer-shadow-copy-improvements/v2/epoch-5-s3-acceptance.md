# Приёмка E5-S3 — диагностика provenance matcher

Дата: 2026-09-12. Вердикт: **принимается. Семь внутренних reason codes и
раздельные path states есть. Публичная MCP schema не расширена.**
Норматив: [epoch-5-reference-provenance.md](epoch-5-reference-provenance.md) E5-S3.
Самоотчёт: [epoch-5-s3-results.md](epoch-5-s3-results.md).

Независимый прогон (MCP `run_specific_test` / `run_dotnet_build`, x64
`C:\Program Files\dotnet`, SDK 10.0.204):

- `run_dotnet_build` Debug `--no-incremental`: success
- `AnalyzerReferenceShadowCopierTests`: **11 passed / 0 failed**
- `AnalyzerProvenanceBindingTests`: **3 passed / 0 failed**
- `Epoch4_surface_sizes_match_recorded_release_numbers`: **pass** (63 / 44165)
- `F09ProductionCaptureTests`: **4 passed / 0 failed**
- redirected exact `V1`: **pass**
- foreign existing exact `FOREIGN`: **pass** (skipped rows, 0 Applied)

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| E5-S3 codes | — | Семь кодов + original/source states | **закрыт** |
| Counts | — | rewritten/skipped по `Applied` | **закрыт** |
| Schema | — | Компактный текст + log | **закрыт** |
| Matcher | — | Confirmed-only не менялся | **закрыт** |
| E5-S3-1 | Low | `proven_foreign_path` = source project not loaded | **открыт**, не блокер |
| E5-S3-2 | Low | Lifecycle DTO без ReasonCode | **закрыт** в E5-S4 |

Версия **v1.3.11**. Inaccessible / U-ARB-05 не выбирались.

---

ID: E5-S3-1
Severity: Low
Depends: E5-S3 foreign code

Target:
AnalyzerReferenceShadowCopier.EnumerateReferenceDecisions

Claim:
`MissingSourceProject` + непустой `MSBuildSourceProjectFile` даёт
`proven_foreign_path` / `ProvenanceSourceProjectNotLoaded`. Это не
NuGet-only foreign и не «несовпадение путей». Для project-produced item,
чей `.csproj` не открыт, код слишком конкретен относительно таблицы
«подтверждённое внешнее происхождение».

Suggested change:
Не чинить в этом этапе. E5-S4 не должен менять matcher из-за этого кода.
При политике inaccessible оставить `access_failure` как path state, не
как foreign.

---

ID: E5-S3-2
Severity: Low
Depends: E5-S3 / E5-S4

Target:
RoslynMcpServer.LifecycleTestHost.RewriteDto

Claim:
`RewriteResult` несёт ReasonCode и path states; `RewriteDto`/`MapRewrite`
их не пробрасывает. Lifecycle проверяет только Applied. Коды покрыты
unit-тестами.

Resolution:
E5-S4 добавил ReasonCode и path states в `RewriteDto`/`MapRewrite` и
оракулит их на fixture. Публичный MCP ответ не расширен.

---

## Что дальше

E5-S4 [принят](epoch-5-s4-acceptance.md) в v1.3.12: diagnostics на fixture
различают missing output / unconfirmed / access; foreign existing честно
`provenance_unconfirmed`. Inaccessible и sticky не открывать.
