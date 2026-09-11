# Review: falsification of analyzer-shadow-copy-improvements

Каталог: замечания к спецификациям, не к тону документов.
Реализация серии не начиналась; сверка с кодом v1.3.5 (`SolutionManager`, `AnalyzerReferenceShadowCopier`, `InProcessAnalyzerAssemblyLoader`, `WorkspaceTools.LoadWorkspace`).

| ID | Sev | Spec |
| --- | --- | --- |
| R-01 | High | README.md |
| R-02 | High | README.md |
| R-03 | High | README.md |
| R-04 | High | README.md |
| E1-01 | Blocker | epoch-1 |
| E1-02 | Blocker | epoch-1 |
| E1-03 | High | epoch-1 |
| E1-04 | High | epoch-1 |
| E1-05 | High | epoch-1 |
| E1-06 | High | epoch-1 |
| E1-07 | High | epoch-1 |
| E1-08 | Medium | epoch-1 |
| E1-09 | High | epoch-1 |
| E1-10 | Medium | epoch-1 |
| E2-01 | High | epoch-2 |
| E2-02 | High | epoch-2 |
| E2-03 | High | epoch-2 |
| E2-04 | High | epoch-2 |
| E2-05 | Medium | epoch-2 |
| E2-06 | Medium | epoch-2 |
| E2-07 | High | epoch-2 |
| E3-01 | Blocker | epoch-3 |
| E3-02 | High | epoch-3 |
| E3-03 | High | epoch-3 |
| E3-04 | High | epoch-3 |
| E3-05 | High | epoch-3 |
| E3-06 | Medium | epoch-3 |
| E4-01 | High | epoch-4 |
| E4-02 | High | epoch-4 |
| E4-03 | High | epoch-4 |
| E4-04 | Medium | epoch-4 |
| E4-05 | High | epoch-4 |
| E4-06 | Medium | epoch-4 |
| E5-01 | Blocker | epoch-5 |
| E5-02 | High | epoch-5 |
| E5-03 | Medium | epoch-5 |
| E5-04 | Medium | epoch-5 |
| E5-05 | Medium | epoch-5 |
| E6-01 | High | epoch-6 |
| E6-02 | High | epoch-6 |
| E6-03 | Medium | epoch-6 |
| E6-04 | Medium | epoch-6 |

---

ID: R-01
Severity: High
Category: Contract

Target:
README.md / line 12-15

Claim:
Серия гарантирует, что повторные запросы, правки документов и перестройка генератора не приводят к потере генерации, блокировке real output или записи temp-ссылок в проект.

Evidence:
Там же, epoch-1 lines 75-76: эпоха 1 может закончиться красным baseline; результат — доказательства, не готовность. Инвариант 4 (line 53) требует неизменяемости опубликованных файлов, которой в текущем коде нет (`File.Copy(..., overwrite: true)` в `CopyToShadowDirectory`). Цель сформулирована как продуктовая гарантия до измерений.

Failure scenario:
1. Эпоха 1 фиксирует потерю overlay после `apply_patch` (E1-09).
2. README всё ещё читается как обещание «не теряем генерацию».
3. Эпохи 2–3 проектируются как улучшения, а не как закрытие уже доказанного слома.

Suggested change:
Заменить «гарантировать» на «измерить и затем обеспечить по подтверждённой матрице». Не держать инвариант 4 как общий до завершения эпохи 2.

Confidence:
High

---

ID: R-02
Severity: High
Category: Snapshot

Target:
README.md / line 51

Claim:
Все семантические операции получают согласованный overlay-снимок.

Evidence:
`GetCurrentSolution()` без `_workspaceLock` (`SolutionManager.cs:257-262`). `FindDocumentAsync` и `GetCurrentSolutionAfterDiskSyncAsync` делают flush dirty set; `CodeAnalysisTools.ExploreAssembly`/`DecompileType` и `UtilityTools.RenameSymbol` берут `GetCurrentSolution()` без flush. `apply_patch` пишет диск + `UpdateDocumentInMemoryAsync` (пересборка overlay с I/O); внешняя правка диска без flush-инструмента не видна.

Failure scenario:
1. Watcher поставил путь в dirty set.
2. `rename_symbol` читает `_solution` до flush — старый текст, но overlay.
3. `get_diagnostics_for_file` через `FindDocumentAsync` — новый текст. Два «семантических» ответа на одном флаге.

Suggested change:
Инвариант 2 сузить до операций, прошедших `EnsureDiskChangesAppliedAsync` + опубликованный `_solution`. Остальные вызовы явно исключить из гарантии.

Confidence:
High

---

ID: R-03
Severity: High
Category: Lifecycle

Target:
README.md / line 19-30

Claim:
Эпоха 4 может выполняться независимо от эпох 2–3; эпоха 3 зависит от 1–2.

Evidence:
Эпоха 4 lines 18-23 требует учёта origin: исходная ссылка, shadow-ссылка, снимок. Сейчас overlay каждый раз заново копирует файлы (`ApplyShadowCopyOverlayIfEnabled` → полный `ShadowCopyInSolutionAnalyzerReferences`). Снять «только overlay-diff» без стабильного mapping (эпоха 2: подготовка отдельно от transform) нельзя, остаётся текущий `SequenceEqual` wipe всего списка. Эпоха 3 требует новый path/поколение из эпохи 2, но исполнение V2 ломается loader identity даже при новом пути (E3-01).

Failure scenario:
1. Эпоха 4 делается первой «потому что независима».
2. Граница записи копирует сегодняшний revert.
3. После эпохи 2 mapping появляется, границу приходится ломать второй раз.

Suggested change:
Эпоха 4 зависит от контракта mapping эпохи 2 (не от загрузчика). Эпоха 3 не закрывается эпохой 2.

Confidence:
High

---

ID: R-04
Severity: High
Category: Lifecycle

Target:
README.md / line 45-46

Claim:
Требует проверки: повторная запись уже загруженной shadow-копии; обновление генератора в одном процессе.

Evidence:
Это не один вопрос. Повторная запись — файловый overwrite + `_loadedByPath.GetOrAdd(path)` (`InProcessAnalyzerAssemblyLoader.cs:31`). Обновление генератора — CLR identity / `LoadFrom` + тот же singleton loader после `ClearWorkspaceAsync` (loader не сбрасывается, `SolutionManager.cs:55`, `578-603`). Смешивание в одном пункте толкает эпоху 2 чинить загрузчик и эпоху 3 чинить файлы.

Failure scenario:
1. Эпоха 2 запрещает overwrite и считает проблему загрузчика закрытой.
2. V1→V2 с новым путём всё ещё исполняет V1.
3. Эпоха 3 начинается с ложным «файловый слой уже достаточен».

Suggested change:
Развести: (a) mutate-after-load файлов, (b) binding identity в процессе, (c) что именно делает `load_workspace` при cache hit.

Confidence:
High
