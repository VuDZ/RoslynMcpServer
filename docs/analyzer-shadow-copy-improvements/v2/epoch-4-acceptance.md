# Приёмка эпохи 4

Дата: 2026-09-11. Вердикт: **принимается** независимым прогоном.
Норматив: [epoch-4-workspace-write-boundary.md](epoch-4-workspace-write-boundary.md) E4-S1–S4.
Прогон: [epoch-4-results.md](epoch-4-results.md).
Версия: **v1.3.8**.

Самоприёмка реализации закрыла A4-01…A4-08 без остатка. Ниже — независимый
повтор: матрица E4-S4 зелёная, блокера нет. Открыты неблокеры A4-09…A4-12.

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| A4-01 | — | Inventory production `TryApplyChanges` | **закрыт** — один `workspace.TryApplyChanges(` в `SolutionManager`; wrapper `TryApplyWorkspaceChanges` (apply + recon); text / overlay / FSW / fallback / under-lock → `ApplyWorkspaceWriteUnderLockAsync` |
| A4-02 | — | Exact inverse, не whole-list wipe | **закрыт** — unit: порядок / кратность / unrelated; wipe-кандидат → `unknown-analyzer-diff` |
| A4-03 | — | no-overlay / unknown / stale / added / removed | **закрыт** — unit + lifecycle unknown/stale **до** записи consumer/`.csproj` |
| A4-04 | — | Три write path + fallback | **закрыт** — text / overlay / FSW × 2 fixture; rejected-apply → `ReconciliationSucceeded`, не `FullSuccess` |
| A4-05 | — | Fault injection | **закрыт** — rejected apply, file I/O, cancel, recon success/failure |
| A4-06 | — | Post-apply без analyzer I/O | **закрыт** — `OverlayPrepareCount` / `AnalyzerFileIoCount` не растут |
| A4-07 | — | Нет temp `<Analyzer Include>` | **закрыт** — `snapshotCsproj` после write path |
| A4-08 | — | Existing-output lock | **закрыт** — оба `OutputPathMode` на full-success; forced rebuild меняет hash DLL; Epoch1 `Text_edit` 2/2 |
| A4-09 | Medium | `BaseSnapshot` не участвует в preflight | **открыт** — не блокер |
| A4-10 | Low | `Cancelled` схлопывается в recon | **открыт** — не блокер |
| A4-11 | Low | Watcher-тест не проверяет `WriteStatus` | **открыт** — не блокер |
| A4-12 | Low | Fallback context = текущая сессия | **открыт** — не блокер |

---

## Независимый прогон

x64 `C:\Program Files\dotnet`, `DOTNET_MULTILEVEL_LOOKUP=0`. Lifecycle **не** через MCP.

```text
WorkspaceWriteBoundaryTests → 9 passed
SolutionManagerAnalyzerOverlayTests → 1 passed
McpToolCatalogTests.Epoch4_surface_sizes → 1 passed (63 / 44165)

Epoch4WorkspaceWriteTests | Epoch1WritePathTests.Text_edit
→ 15 passed / 0 failed, 2 m 23 s
```

### Lifecycle

| Случай | Наблюдение |
| --- | --- |
| Text / overlay / FSW × missing+existing | `FullSuccess` (text/overlay), маркер V1, запрошенный текст, `.csproj` bytes, prepare/I/O не растут, forced rebuild |
| Unknown analyzer + text | `PreflightRejected` / `unknown-analyzer-diff`; consumer и project bytes не менялись |
| Stale после reset+load | `PreflightRejected` / `stale-session`; диск не менялся |
| Rejected `TryApplyChanges` | `ReconciliationSucceeded`, `try-apply-rejected`, не `FullSuccess`; текст на диске; overlay published; project unapplied |
| File I/O | `PartialPersistence`, 0 saved, overlay не published |
| Cancel after write | `ReconciliationSucceeded` / `cancelled`; текст сохранён |
| Recon failure | `ReconciliationFailed`; candidate не published |
| No-overlay text | `FullSuccess` |

---

ID: A4-09
Severity: Medium
Depends: —

Target:
WorkspaceWriteBoundary.Preflight
WorkspaceWriteOperationContext.BaseSnapshot

Claim:
E4-S1 требует идентичность базового снимка в operation context и отказ stale
candidate **до** побочных эффектов («защитой overlay нельзя перезаписывать более
свежие изменения»). `BaseSnapshot` пишется (CWT / `ResolveOperationContext`) и
**не читается** в `Preflight`. Проверяются только `SessionId` и `LoadedPath`.
E4-S4 сужает тест до reload — он зелёный. Same-session: `GetCurrentSolution`
без lock, запись берёт lock позже; параллельный MCP tool может применить
candidate со старого overlay поверх более свежей записи. MVCC / registry
in-flight — non-goal, поэтому не блокер.

Evidence:
`WorkspaceWriteBoundary.Preflight` (session/path only).
`SetPublishedSolution` штампует overlay как `BaseSnapshot`.
`UpdateDocumentInMemoryUnderLockAsync` штампует raw `workspace.CurrentSolution`.
Сравнение `ReferenceEquals(workspace.CurrentSolution, BaseSnapshot)` на overlay
apply всегда ложно — поле нельзя использовать как freshness без сырого штампа.

Suggested change:
Не чинить в этой эпохе. Если понадобится: штамповать raw
`workspace.CurrentSolution` в момент создания candidate и отвергать, если
workspace уехал. Не вводить историю снимков.

---

ID: A4-10
Severity: Low
Depends: —

Target:
SolutionManager.FinishAfterSideEffectsAsync
WorkspaceWriteStatus.Cancelled

Claim:
E4-S2 просит различать отмену как исход. После частичной записи + успешного
reconciliation статус становится `ReconciliationSucceeded`, reason=`cancelled`.
`Cancelled` остаётся только если `savedTexts` пуст. Адаптеры пишут Status+Reason —
агент видит отмену. Тест эпохи 4 это фиксирует.

Suggested change:
Оставить. Либо `Cancelled` + `OverlayPublished` при успешном recon, без смены
смысла «не полный успех».

---

ID: A4-11
Severity: Low
Depends: —

Target:
Epoch4WorkspaceWriteTests.Watcher_flush_full_success_*

Claim:
Продуктовый FSW flush идёт в `ApplyWorkspaceWriteUnderLockAsync`. Тест не
утверждает `WriteStatus=FullSuccess` (только Ok / текст / counts / маркер /
`.csproj`). Пробел теста, не продукта.

Suggested change:
Добавить `WriteStatus` на `flushFind`, если host начнёт его отдавать.

---

ID: A4-12
Severity: Low
Depends: A4-09

Target:
SolutionManager.ResolveOperationContext

Claim:
E4-S1: «последний mapping не заменяет mapping исходной операции». CWT hit
(held overlay после reset) даёт старую сессию → `stale-session`. Fallback при
промахе CWT собирает **текущие** session/mapping: stale-session не сработает.
Production держит overlay `Solution`, ключ CWT жив; промах не воспроизведён.
`ClearWorkspaceAsync` CWT не чистит — как раз поэтому applyHeld после reload
зелёный.

Suggested change:
Не чистить CWT на reset без fail-closed. Fallback лучше отказывать
(`stale-session` / no-context), а не подставлять текущий mapping.

---

## Что лежит в дереве

- `WorkspaceWriteBoundary` + `AnalyzerShadowMapping.InvertKnownReplacements`
- `WorkspaceWriteResult` / `WorkspaceWriteOperationContext`
- `SolutionManager.ApplyWorkspaceWriteUnderLockAsync` — единый workflow
- ConditionalWeakTable штампует mapping/session на опубликованный overlay
- Адаптеры MCP: Status в markdown; новой schema нет
- Host: hold/applyHeld, unknown analyzer, apply/file/recon/cancel inject

## Что не чинить в этой эпохе

- MVCC / merge engine / реестр всех in-flight operations
- Load-isolation redesign (U-ARB-04) — утечки нет; inverse защищает `.csproj`, не CLR load
- Намеренное редактирование analyzer references
- U-ARB-01 matcher, U-ARB-05 sticky flag
- A2-09 отдельные stores (requested / prepared / active / refresh / observed)
- A4-09…A4-12
