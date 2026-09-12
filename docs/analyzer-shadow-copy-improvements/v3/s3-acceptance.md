# Приёмка v3 S3 — устойчивый fail-closed

Дата: 2026-09-12. Вердикт: **принимается в v1.3.17.**
Запрет raw publication после неуспешного opt-in переживает edit / flush /
reconciliation / cancel и cached false/omitted. V3-R2 закрыт. R3 специально
не чинился и остаётся красным (S4). Файл приёмки в дереве до этого прогона
не заменяет сверку. Норматив:
[s3-persistent-publication-state.md](s3-persistent-publication-state.md).

Независимый прогон: MCP `run_dotnet_build` Debug `--no-incremental` success
(SDK 10.0.204). Unit-тесты через MCP `run_specific_test` (`noBuild=true`).
`Category=AnalyzerLifecycle` — локальный `C:\Program Files\dotnet\dotnet.exe`,
`DOTNET_MULTILEVEL_LOOKUP=0`.

| Проверка | Результат |
| --- | --- |
| `WorkspaceWriteBoundaryTests` | **16/16** passed (admission change → `stale-publication`) |
| `SemanticPublicationStateTests` | **2/2** passed |
| `V3RegressionBaselineTests` + `V3PersistentPublicationStateTests` + `UArb04LoadBoundaryEvidenceTests` | **18 passed / 1 failed** (~2 м 14 с) |
| R1 | **passed** — S2 не регрессировал |
| R2 | **passed** — после text edit нет real published/loaded/process, marker пуст |
| `V3PersistentPublicationStateTests` | **7/7** passed (flush, recon, cancel, cached false/omitted, restore, no-overlay, successful opt-in) |
| `UArb04LoadBoundaryEvidenceTests` | **passed** (fail-closed / cancel / blocking / overlay-first) |
| Epoch1 stale-mapping reapply + cached false overlay | **2/2** passed |
| R3 | **failed** как требуется: `capture=Failed`, published = real `Generator.dll` на load |

Код: `SemanticPublicationAdmission` (NoOverlay / AllowedMapping / Banned)
отдельно от last execution observation. Исключённые references фиксируются
на load/prepare (`CaptureFromInSolutionReferences`) и на ordinary publication
только применяются. Cached true восстанавливает overlay после успешного
prepare; identity gate в коде на месте (Epoch1 stale-mapping зелёный).
GetPublishedSolutionAsync без fallback на raw. Inspector / ProbePath /
EvaluateAssemblyPath — только счётчики, поведение не ослаблялось.
Публичная MCP-схема не менялась; catalog 63 / 44,503 заявлен без изменения.

HEAD git: незакоммиченное S3 поверх `7fb86b3` (v1.3.16). csproj **1.3.17**.
MCP process всё ещё **1.3.15** (publish/reload этого шага не делались).

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| V3-R1 / A4-09 | P1 | stale candidate перезаписывает свежий текст | **закрыт** (S2) |
| V3-R2 | P1 | fail-closed сбрасывается text edit | **закрыт** |
| V3-R3 | P1 | corrupt capture публикует real | **открыт** — S4 |
| S3-1 | Low | `LoadCore` cache hit ещё делает `_solution ?? CurrentSolution`; это не published accessor | открыт |

Inaccessible не открывался. ALC / MVCC / новые MCP параметры не добавлялись.

Следующий шаг — S4 (отказ при failed capture).
