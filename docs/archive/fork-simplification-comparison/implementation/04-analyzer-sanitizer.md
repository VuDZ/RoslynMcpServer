# 4 — `WorkspaceAnalyzerSanitizer`

Pri **P1**. SemVer: **patch**. Зависимостей нет; желателен до навигации (8).
Разбор: [../port-candidates-1-2-3-4-7.md](../port-candidates-1-2-3-4-7.md#4-workspaceanalyzersanitizer).

## Цель

`UnresolvedAnalyzerReference` не валит solution-wide `SymbolFinder` (Roslyn 5.9
checksum). Load/health по-прежнему видят raw missing path.

## Файлы

- новый `Services/WorkspaceAnalyzerSanitizer.cs` (~форк, ~90 строк)
- `Services/SolutionManager.cs` — кэш snapshot по identity raw solution,
  **не** `SetPublishedSolution`
- `Tools/NavigationTools.cs`, `CallGraphHelper`, `rename_symbol` — поиск на
  sanitized + `WithSanitizedRetryAsync`
- `RoslynMcpServer.Tests/WorkspaceAnalyzerSanitizerTests.cs` (рецепт форка)

## Правка

1. Helper: `RemoveUnresolvedAnalyzers`, `IsUnresolvedAnalyzerError`,
   `WithSanitizedRetryAsync`.
2. `GetSanitizedPublishedSolution()` (имя на наше): берёт **уже published**
   raw snapshot, кэширует sanitized по `ReferenceEquals` raw.
3. Инвалидация — смена published/raw после disk-sync. Не `TryApplyChanges`
   sanitized в `MSBuildWorkspace`.
4. Не возвращать sanitized из `GetPublishedSolutionAfterDiskSyncAsync` —
   этот путь кормит overlay/admission.

## Тесты

Рецепт форка: AdhocWorkspace + stub → SymbolFinder; retry; rethrow при том же
solution. Плюс: published raw после sanitize всё ещё содержит unresolved ref
(health).

## Acceptance

`find_usages` / `find_symbol_references` / `get_call_graph` /
`find_implementations` / `rename_symbol` на solution с отсутствующей analyzer
DLL; здоровый solution — no-op (тот же инстанс).

## Не копировать

`GetCurrentSolutionAfterDiskSyncAsync` форка, который сразу отдаёт sanitized.
Overlay, provenance, `shadowCopyInSolutionAnalyzers` не трогать.
