# Приёмка U-ARB-05 — session-sticky повторный флаг

Дата: 2026-09-12. Вердикт: **принимается. Активация session-sticky.
Cached `false`/omitted сохраняет overlay без prepare/refresh. Disable —
`reset_workspace` (или другой load key / graph reopen), затем load без флага.
Публичная MCP schema не менялась.**
Норматив: [UNRESOLVED-v2.md](UNRESOLVED-v2.md) U-ARB-05;
[u-arb-05-decision.md](u-arb-05-decision.md). Решение владельца не
переоткрывалось: desired-state / tri-state / новый disable-параметр
отклонены из-за optional non-nullable `bool`.

Независимый прогон (локальный `C:\Program Files\dotnet\dotnet.exe`,
`DOTNET_MULTILEVEL_LOOKUP=0`, SDK 10.0.204; MCP в этот момент был offline):

- `dotnet build` Debug `--no-incremental`: success (3 pre-existing warnings)
- `Epoch1LifecycleMatrixTests.Flag_true_to_false_and_omitted_preserves_session_overlay_until_reset`: **passed** (12 s)
- `Epoch1LifecycleMatrixTests.Flag_false_to_true_enables_on_cached_load_and_after_reset`: **passed** (15 s)
- `McpToolCatalogTests.Surface_sizes_match_recorded_release_numbers`: **passed** (full 63 / **44503**)

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| Sticky cache hit | — | `false`/omitted: CacheHit, PrepareAttempted=false, тот же generation, exact `V1` | **закрыт** |
| Enable on cache | — | `false`→`true` на том же ключе готовит overlay и даёт `V1` | **закрыт** |
| Disable after reset | — | reset + `false`/omitted: `ShadowEnabled=false`, маркер не `V1` | **закрыт** |
| Schema | — | нет tri-state / нового параметра; catalog +338 | **закрыт** |
| U-ARB-05-1 | Low | MCP-текст «overlay preserved» не покрыт автотестом | **открыт**, не блокер |

Версия **v1.3.13**. Поведение cache-hit уже было shipped (v1.3.5+); продукт
этого шага — выбранный контракт, наблюдаемость в `load_workspace` и
закрепление матрицы. `SolutionManager` на cache hit не чистит mapping и
ставит `_lastPrepareAttempted = false`; prepare идёт только при `true`.
`ClearWorkspaceAsync` / physical reopen сбрасывают overlay.

Наблюдение, не дефект sticky: после reset на redirected missing-path oracle
даёт `execution-not-permitted:RestartRequired`. Overlay выключен; отказ —
эпоха 3 (CLR identity после `V1` в том же процессе), не повторное
включение флага.

Открыто, не блокер: U-ARB-05-1. Inaccessible не открывался. Открытый gate
серии — U-ARB-04 (evidence load boundary).

---

ID: U-ARB-05-1
Severity: Low
Depends: —

Target:
Tools/WorkspaceTools.cs load_workspace summary

Claim:
Сообщение `active session overlay preserved; false/omitted does not disable
or refresh` есть только на MCP-пути. Lifecycle host не проходит
`WorkspaceTools`, поэтому строка не assert-ится. Это не ломает контракт
overlay.

Suggested change:
Не чинить в этом этапе. Не менять schema ради теста текста.

---

## Что дальше

U-ARB-05 закрыт. Следующий gate серии — **U-ARB-04** (evidence only: не
вводить новую load boundary). Inaccessible не открывать. E5-S3-1,
A4-09…A4-12, A6-13, F-06 не чинить без отдельного запроса.
