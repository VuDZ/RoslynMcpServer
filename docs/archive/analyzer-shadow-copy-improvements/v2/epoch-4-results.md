# Эпоха 4 — результаты

Дата: 2026-09-11. Статус: **принят независимым прогоном**.
Приёмка: [epoch-4-acceptance.md](epoch-4-acceptance.md).
Окружение: Windows 10 (build 26200), .NET SDK 10.0.204, Roslyn 5.9.0,
x64 `C:\Program Files\dotnet`, `DOTNET_MULTILEVEL_LOOKUP=0`. Версия: **v1.3.8**.

Независимый повтор (этот проход), не через MCP:

```text
WorkspaceWriteBoundaryTests → 9 passed
SolutionManagerAnalyzerOverlayTests → 1 passed
McpToolCatalogTests.Epoch4_surface_sizes → 1 passed (63 / 44165)

Epoch4WorkspaceWriteTests | Epoch1WritePathTests.Text_edit
→ 15 passed / 0 failed (13 epoch-4 + 2 Text_edit), 2 m 23 s
```

## Unit

`WorkspaceWriteBoundaryTests` — **9 passed** + overlay pointer **1 passed**.

- Exact inverse восстанавливает только mapped shadow; unrelated refs, порядок и
  кратность сохраняются.
- Whole-list wipe (кандидат без unrelated refs) → `unknown-analyzer-diff`.
- No-overlay noop; unknown diff без temp-prefix inference.
- Stale session; added project с analyzer refs / без них; removed project
  не воссоздаётся mapping.
- Production `workspace.TryApplyChanges(` в `SolutionManager` — ровно один
  вызов (`TryApplyWorkspaceChanges`).

## Lifecycle

| Случай | Результат |
| --- | --- |
| Text edit full success (missing + existing path) | **pass** — FullSuccess, маркер V1, запрошенный текст, `.csproj` bytes, forced rebuild, OverlayPrepare/FileIo не растут |
| Overlay apply full success (оба fixture) | **pass** — то же |
| Watcher flush full success (оба fixture) | **pass** — FSW delivery + production flush, маркер V1, `.csproj` bytes |
| Unknown analyzer diff + text | **pass** — PreflightRejected до записи; consumer и project bytes неизменны |
| Stale candidate после reset+load | **pass** — `stale-session` до записи |
| Rejected TryApplyChanges | **pass** — ReconciliationSucceeded, не FullSuccess; текст на диске; overlay published; project state unapplied |
| File I/O failure | **pass** — PartialPersistence, 0 saved, overlay не published, диск не изменён |
| Cancel after write | **pass** — ReconciliationSucceeded / cancelled; сохранённый текст; не полный успех |
| Reconciliation failure | **pass** — ReconciliationFailed; candidate не published; freshness не обещана |
| No-overlay text edit | **pass** — FullSuccess |

## Ограничения

- Новый публичный MCP schema не вводился; адаптеры пишут статус в существующий
  markdown.
- Атомарный rollback нескольких файлов не обещается (N-01).
- Semantic/load isolation (U-ARB-04) не пересматривалась: inverse защищает
  persistence `.csproj`, не загрузку analyzer. Forced rebuild на full-success
  paths меняет hash real output — утечки lock не видно.
- Binding gate эпохи 3 независим и не менялся.
- Открыты неблокеры A4-09…A4-12 (см. приёмку).
