# E1-S4 — Инвентаризация semantic entry points

Снимок: `GetCurrentSolution()` возвращает `_solution` (overlay) или
`workspace.CurrentSolution`. Сам getter не копирует файлы и не загружает
assemblies. Flush выполняется только `FindDocumentAsync` /
`GetCurrentSolutionAfterDiskSyncAsync` / `EnsureDiskChangesAppliedAsync`.

| Файл | Откуда snapshot | Flush | Та же база до transform | Raw `workspace.CurrentSolution` compilation |
| --- | --- | --- | --- | --- |
| `Tools/WorkspaceTools.cs` | `LoadAndPrepareAsync` | boundary целиком | Load/cache→prepare/gate→publish под одним lock; diagnostics/summary после boundary | нет |
| `Tools/ServerLifecycleTools.cs` | getter | нет | n/a | нет |
| `Tools/UtilityTools.cs` (прочие) | getter | нет | n/a | нет |
| `Tools/UtilityTools.cs` `RenameSymbol` | сериализованный `FindDocumentAsync`; база = `document.Project.Solution` | да | та же база после symbol | нет |
| `Tools/NavigationTools.cs` `FindSymbolReferences` | сериализованный `FindDocumentAsync`; база = `document.Project.Solution` | да | та же база | нет |
| `Tools/NavigationTools.cs` usages/impl/definition | `GetPublishedSolutionAfterDiskSyncAsync` | да | опубликованная база после flush | нет |
| `Tools/CodeAnalysisTools.cs` diagnostics/skeleton | `FindDocumentAsync` | да | документ из flushed overlay | нет |
| `Tools/CodeAnalysisTools.cs` explore/decompile | getter (resolve DLL path) | нет | n/a, не компилирует overlay generators | нет |
| `Tools/CodeFixTools.cs` | `FindDocumentAsync` → `ApplySolutionChangesToDiskAsync` | да | transform от document solution | apply использует workspace current для revert overlay |
| `Tools/RefactoringTools.cs` | то же | да | то же | то же |
| `Tools/AstTools.cs` | то же | да | то же | то же |
| `Tools/EditingTools.cs` | запись файла + `UpdateDocumentInMemoryAsync` | нет | n/a | apply идёт на `workspace.CurrentSolution` |
| `Tools/TestTools.cs` | `GetPublishedSolutionAfterDiskSyncAsync` / `FindDocumentAsync` | да | после serialized flush | нет |
| `Services/SolutionManager.cs` | published `_solution` | accessor/flush под `_workspaceLock` | semantic accessor ждёт load/prepare; lock-free getter только non-semantic | lookup/TryApplyChanges/overlay source; не semantic oracle |

Новый caller без строки в этой таблице должен ломать `Epoch1SemanticInventoryTests`.

## U-ARB-04 evidence (2026-09-12)

`Epoch1SemanticInventoryTests.Production_code_has_no_explicit_raw_workspace_semantic_reader`
подтверждает, что `Tools/`/`Services/` не вызывают
`GetWorkspaceCurrentSolution()` для semantic compilation. Raw
`workspace.CurrentSolution` в `SolutionManager` остаётся базой write workflow,
но сам manager не вызывает на ней `GetCompilationAsync`/`GetSemanticModelAsync`.

Default oracle из опубликованного `GetCurrentSolution()` без активного overlay
доказал, что обычный `GetCompilationAsync` existing-correct project до enable
загружает real analyzer path и блокирует forced rebuild. Это
production-equivalent opt-in boundary. Отдельный test-only raw oracle запускался
после overlay semantic, переиспользовал shadow identity и отдельный real analyzer
не загрузил. Штатные overlay readers загружают shadow path. Полная матрица и
граница вывода:
[u-arb-04-load-boundary-evidence.md](u-arb-04-load-boundary-evidence.md).
Production concurrent-dispatch между завершением physical load и отдельным
enable/prepare evidence-проходом не воспроизводился. В v1.3.14 отдельные
захваты заменены atomic manager workflow, semantic readers переведены на
published-snapshot accessor, а inventory guard запрещает lock-free getter
перед `GetCompilationAsync` / `GetSemanticModelAsync`. Детерминированная
приёмка: [u-arb-04-implementation-acceptance.md](u-arb-04-implementation-acceptance.md).
