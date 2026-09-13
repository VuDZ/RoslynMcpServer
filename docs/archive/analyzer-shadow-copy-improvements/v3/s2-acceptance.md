# Приёмка v3 S2 — freshness перед записью

Дата: 2026-09-12. Вердикт: **принимается в v1.3.16.**
Stale candidate отклоняется `PreflightRejected` до persist / `TryApplyChanges` /
reconciliation. V3-R1 закрыт. R2/R3 специально не чинились и остаются красными.
Самоотчёт в `s2-write-base-freshness.md` не заменяет этот прогон.
Норматив: [s2-write-base-freshness.md](s2-write-base-freshness.md).

Независимый прогон: MCP `run_dotnet_build` Debug `--no-incremental` success
(SDK 10.0.204). Unit-тесты через MCP `run_specific_test` (`noBuild=true`).
`Category=AnalyzerLifecycle` — локальный `C:\Program Files\dotnet\dotnet.exe`,
`DOTNET_MULTILEVEL_LOOKUP=0` (MCP `-32001`).

| Проверка | Результат |
| --- | --- |
| `WorkspaceWriteBoundaryTests` | **15/15** passed |
| `WorkspaceDocumentDiskSyncTests` | **5/5** passed |
| `V3_R1_same_session_stale_candidate_is_rejected_before_any_write` | **passed** — `PreflightRejected`, `saved=0`, диск и published = B |
| `V3WriteBaseFreshnessTests` | **7/7** passed (reload, other-doc, watcher, unknown context, fresh overlay/plain, under-lock update/flush) |
| `Epoch4WorkspaceWriteTests` | **13/13** passed (~3 м 11 с) |
| R2 / R3 | **failed** как требуется: после edit real path в published+process; corrupt capture публикует real на load |

Итого lifecycle S2: **8 passed + 2 ожидаемых fail (R2/R3)**. Epoch4 не регрессировал.

Код: контекст держит published base, mapping/session, raw identity и
`_rawWorkspaceRevision`. Freshness сравнивает raw revision/identity, не
overlay↔raw через `ReferenceEquals`. `ResolveOperationContext` неизвестной
базе больше не подставляет текущие session/mapping (A4-12). Under-lock
update/flush штампуют свою текущую базу без повторного semaphore.
Отказ — `unknown-operation-context` / `stale-session` / `stale-base` /
`stale-publication` до любой записи этой операции. Merge нет.
Публичная MCP-схема не менялась; catalog 63 / 44,503 заявлен без изменения.

HEAD git: этот коммит поверх `fbd6766`. csproj **1.3.16**.
MCP process всё ещё **1.3.15** (publish/reload этого шага не делались).

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| V3-R1 / A4-09 | P1 | stale candidate перезаписывает свежий текст | **закрыт** |
| A4-12 | Low | fallback context = текущая сессия | **закрыт** |
| V3-R2 | P1 | fail-closed сбрасывается text edit | **открыт** — S3 |
| V3-R3 | P1 | corrupt capture публикует real | **открыт** — S4 |
| S2-1 | Low | `ApplyAsync_refreshes_identity_when_text_is_unchanged` не проверяет `!ReferenceEquals` | открыт |

Исторические пометки A4-09/A4-12 в v2 docs не переписывать здесь; это S6.
Inaccessible не открывался. ALC / MVCC / новые MCP параметры не добавлялись.

Следующий шаг — S3 (устойчивый fail-closed).
