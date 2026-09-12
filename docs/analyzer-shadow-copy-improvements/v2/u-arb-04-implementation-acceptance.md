# Приёмка реализации U-ARB-04 — atomic load/prepare

Дата: 2026-09-12. Вердикт: **принимается в v1.3.14.** Один
`LoadAndPrepareAsync` под одним `_workspaceLock`; production semantic ждёт
`GetPublishedSolutionAsync` / `FindDocumentAsync`; fail-closed не отдаёт raw
real path. Публичная schema не менялась. Inaccessible не открывался.
Самоотчёт implementer в дереве не заменяет эту сверку.
Норматив: [u-arb-04-atomic-load-prepare.md](u-arb-04-atomic-load-prepare.md).

Независимый прогон (локальный `C:\Program Files\dotnet\dotnet.exe`,
`DOTNET_MULTILEVEL_LOOKUP=0`, SDK 10.0.204):

- `dotnet build` Debug `--no-incremental`: success
- `UArb04LoadBoundaryEvidenceTests`: **9 passed**
- `Epoch1WritePathTests`: **6 passed**
- `Epoch1SemanticInventoryTests`: **5 passed**
- flag sticky / `false→true`: **2 passed**
- catalog 63 / **44503**: **passed**
- `IsMissingCompileTarget_still_blocking`: **passed**

Итого **24/24**.

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| S1 single lock | — | `LoadWorkspace` только `LoadAndPrepareAsync`; diagnostics после publish | **закрыт** |
| S1 fail-closed | — | новая сессия: strip in-solution refs; `_solution` без fallback на raw | **закрыт** |
| S2 accessor | — | semantic через lock + published snapshot; inventory без lock-free getter у compilation | **закрыт** |
| S3 order | — | opt-in первым: accessor не completed до prepare, shadow path, rebuild меняет hash | **закрыт** |
| S3 semantic-first | — | published no-overlay → enable = RestartRequired, overlay off | **закрыт** |
| S3 compat | — | sticky / `false→true` / write-path 6/6 зелёные | **закрыт** |
| S4 schema | — | catalog 63 / 44503; version 1.3.14 | **закрыт** |
| U-ARB-04-IMPL-1 | Low | implicit `FindDocument` load публиковал raw без prepare | **закрыт** в v1.3.15 |

`GetCurrentSolution()` больше не делает fallback на `workspace.CurrentSolution`.
Test-only `GetWorkspaceCurrentSolution` / `oracleSource: workspace` сохранены.
`ShadowCopyInSolutionAnalyzerReferencesAsync` не вызывается из `WorkspaceTools`.

---

ID: U-ARB-04-IMPL-1
Severity: Low
Depends: —

Target:
SolutionManager.EnsureWorkspaceLoadedForFileUnderLockAsync

Claim:
Если workspace ещё не загружен, `FindDocumentAsync` вызывает
`LoadCoreAsync(..., publishLoadedSolution: true)` без opt-in prepare.
Это не путь `load_workspace(..., true)` и не окно двух lock.

Resolution:
Implicit file-walk load удалён. `FindDocumentAsync` без заранее загруженного
workspace возвращает `null`, как уже обещают tool descriptions и
`WorkspaceLoadGuidance` («agents must call load_workspace»). Автоматический
opt-in/prepare не вводился. Регрессия:
`SolutionManagerPathResolutionTests.FindDocumentAsync_without_loaded_workspace_does_not_auto_load`.

---

## Что дальше

U-ARB-04 реализация принята. Серия: inaccessible в U-ARB-01 не открывать.
E5-S3-1, A4-09…A4-12, A6-13, F-06 и U-ARB-05-1 не чинить без запроса.
