# Review: falsification of disk-sync-csproj-mutation

Каталог: замечания к спецификациям, не к тону документов.
Сверка: рабочее дерево `7c931cb` (`fix(tests): discover custom test attributes…`),
runtime `get_mcp_server_info` = **1.3.29**, не «версия из будущего патча».
Код: `WorkspaceDocumentDiskSync`, `WorkspaceWriteBoundary`,
`SolutionManager.FlushDirtyDocumentsUnderLockAsync` / `ApplyWorkspaceWriteUnderLockAsync`,
`StructuralRefactoringHelper`, `RefactoringTools`,
`RoslynMcpServer.Tests/WorkspaceDocumentDiskSyncTests.cs`,
`docs/workspace-load-cache/epoch-1-workspace-lifecycle.md` (A-WRITE, не в runtime).

Цель ревью — опровергнуть предложенную архитектуру, а не улучшить её изложение.
План и production-код этим каталогом не изменены.

## Результат

**8 замечаний: 1 Blocker, 5 High, 2 Medium.** Рекомендацию **A+C** (§3) нельзя
принимать как норму патча: C-1 / вариант C / T-2 делают `document-set-diff`
глобальным гейтом `Preflight` и ломают уже существующие write-тулы с
`AddDocument`. A как диск-sync остаётся правильным направлением, если закрыть
early-return flush и oracle T-3.

| ID | Sev | Spec |
| --- | --- | --- |
| C-01 | Blocker | proposed-solution.md |
| C-02 | High | proposed-solution.md |
| C-03 | High | proposed-solution.md |
| C-04 | High | proposed-solution.md |
| C-05 | Medium | proposed-solution.md |
| C-06 | Medium | proposed-solution.md |
| V-01 | High | proposed-solution.md |
| V-02 | High | proposed-solution.md |

Карточки: [proposed-solution.md](proposed-solution.md).

## Покрытие документов

Прочитаны оба файла канона темы:

- `README.md` (проблема): механизм `AddDocument` → `TryApplyChanges` →
  NETSDK1022, логи `added>0`, пробел в тестах на существующие документы —
  подтверждаются кодом 1.3.29. Самостоятельных карточек `R-*` нет.
- `proposed-solution.md` (варианты / C-1…C-6 / A+C / T-1…T-5 / §7): C-01…C-06,
  V-01, V-02.
