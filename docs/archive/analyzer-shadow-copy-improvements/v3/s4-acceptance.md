# Приёмка v3 S4 — отказ при failed capture

Дата: 2026-09-12. Вердикт: **принимается в v1.3.18.**
Opt-in без пригодного provenance не публикует semantic snapshot.
V3-R3 закрыт. Самоотчёт в `s4-provenance-failure-gate.md` не заменяет этот прогон.
Норматив: [s4-provenance-failure-gate.md](s4-provenance-failure-gate.md).

Независимый прогон: MCP `run_dotnet_build` Debug `--no-incremental` success
(SDK 10.0.204). Unit-тесты через MCP `run_specific_test` (`noBuild=true`).
`Category=AnalyzerLifecycle` — локальный `C:\Program Files\dotnet\dotnet.exe`,
`DOTNET_MULTILEVEL_LOOKUP=0`.

| Проверка | Результат |
| --- | --- |
| `AnalyzerProvenanceCaptureGateTests` | **4/4** passed |
| `SemanticPublicationStateTests` | **3/3** passed |
| `WorkspaceWriteBoundaryTests` | **16/16** passed |
| `WorkspaceLoadGuidanceTests` unavailable message | **passed** |
| `McpToolCatalogTests.Surface_sizes_match_recorded_release_numbers` | **passed** — 63 / 44,503 |
| `V3RegressionBaselineTests` + `V3ProvenanceFailureGateTests` + E5 missing-path/foreign/Failed-capture + F09 replay | **14/14** passed (~3 м 12 с) |
| R1 / R2 | **passed** — S2/S3 не регрессировали |
| R3 | **passed** — `no-solution`, нет real published/loaded/process, marker пуст |
| Missing / mixed Incomplete / null / session mismatch | **passed** — `Unavailable`, snapshot withheld |
| Edit / flush / cached false/omitted / cached true | **passed** — семантика закрыта; Failed не становится Complete |
| Reset/reopen + fresh host exact V1 | **passed** — новый session/graph, shadow path |
| Complete foreign + missing-path | **passed** — не приравнены к failed capture |
| No-overlay + Failed capture | **passed** — raw refs как прежде |
| F09 replay | **passed** — `Unavailable`, temp binlogs удалены |

Код: пригодный capture для opt-in — `Complete` + `LoadSessionId` текущей сессии.
Missing / Failed / Incomplete / чужой session → `Unavailable`, `_solution = null`.
Prepare не запускается. Strip-confirmed больше не оставляет raw snapshot.
Cached true не чинит Failed; сообщение указывает `reset_workspace` + новый load.
`GetPublishedSolutionAsync` / `FindDocumentAsync` без fallback на raw.
Публичная MCP-схема не менялась. Inaccessible не выбирался.

HEAD git: незакоммиченное S4 поверх `d45ff5b` (v1.3.17). csproj **1.3.18**.
MCP process всё ещё **1.3.15** (publish/reload этого шага не делались).

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| V3-R1 / A4-09 | P1 | stale candidate перезаписывает свежий текст | **закрыт** (S2) |
| V3-R2 | P1 | fail-closed сбрасывается text edit | **закрыт** (S3) |
| V3-R3 | P1 | corrupt capture публикует real | **закрыт** |
| S3-1 | Low | `LoadCore` cache hit `_solution ?? CurrentSolution` | открыт |
| S4-1 | Low | `PublishInMemorySolution(Unavailable)` возвращает raw; публикация режется `WithholdsSnapshot` / `_solution = null` | открыт |

Следующий шаг — S5 (частичный prepare и переходы).
