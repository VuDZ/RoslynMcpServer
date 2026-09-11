# E1-S4 — Инвентаризация semantic entry points

Снимок: `GetCurrentSolution()` возвращает `_solution` (overlay) или
`workspace.CurrentSolution`. Сам getter не копирует файлы и не загружает
assemblies. Flush выполняется только `FindDocumentAsync` /
`GetCurrentSolutionAfterDiskSyncAsync` / `EnsureDiskChangesAppliedAsync`.

| Файл | Откуда snapshot | Flush | Та же база до transform | Raw `workspace.CurrentSolution` compilation |
| --- | --- | --- | --- | --- |
| `Tools/WorkspaceTools.cs` | getter после enable | нет | Load→enable→summary в одном вызове, два захвата lock | нет |
| `Tools/ServerLifecycleTools.cs` | getter | нет | n/a | нет |
| `Tools/UtilityTools.cs` (прочие) | getter | нет | n/a | нет |
| `Tools/UtilityTools.cs` `RenameSymbol` | `FindDocumentAsync` затем **повторный** `GetCurrentSolution()` | да, затем re-get | **нет гарантии** той же базы после symbol | нет |
| `Tools/NavigationTools.cs` `FindSymbolReferences` | `FindDocumentAsync` затем `GetCurrentSolution()` | да, затем re-get | **нет гарантии** | нет |
| `Tools/NavigationTools.cs` usages/impl/definition | `GetCurrentSolutionAfterDiskSyncAsync` | да | одна база после flush | нет |
| `Tools/CodeAnalysisTools.cs` diagnostics/skeleton | `FindDocumentAsync` | да | документ из flushed overlay | нет |
| `Tools/CodeAnalysisTools.cs` explore/decompile | getter (resolve DLL path) | нет | n/a, не компилирует overlay generators | нет |
| `Tools/CodeFixTools.cs` | `FindDocumentAsync` → `ApplySolutionChangesToDiskAsync` | да | transform от document solution | apply использует workspace current для revert overlay |
| `Tools/RefactoringTools.cs` | то же | да | то же | то же |
| `Tools/AstTools.cs` | то же | да | то же | то же |
| `Tools/EditingTools.cs` | запись файла + `UpdateDocumentInMemoryAsync` | нет | n/a | apply идёт на `workspace.CurrentSolution` |
| `Tools/TestTools.cs` | `GetCurrentSolutionAfterDiskSyncAsync` / `FindDocumentAsync` | да | после flush | нет |
| `Services/SolutionManager.cs` | overlay `_solution` | flush только явными API | getter без lock | lookup/TryApplyChanges/overlay source; не semantic oracle |

Новый caller без строки в этой таблице должен ломать `Epoch1SemanticInventoryTests`.
