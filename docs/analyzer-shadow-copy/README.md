# Analyzer / generator shadow copy: implementation epochs

This directory is the historical record of one investigation: why an
in-solution Roslyn analyzer/generator project referenced with
`OutputItemType="Analyzer"` can silently stop producing generated code (and
lock its own build output) when a repo-wide `Directory.Build.props` overrides
`OutputPath`, and how `RoslynMcpServer` came to fix it.

All three epochs below are **shipped**. This is not a forward-looking plan —
it is kept so the reasoning, the first (flawed) fix attempt, and the final
design are not lost. `docs/ARCHITECTURE.md` describes the **current** shipped state (v1.3.6 mapping);
this directory is the v1.3.3–v1.3.5 history. Read it when you need to know
*why* the overlay exists, or before touching `Services/AnalyzerReferenceShadowCopier.cs`,
`Services/SolutionManager.cs`'s shadow-copy overlay, or
`Services/ProjectOutputDiagnosticsLogger.cs`.

## Epoch order

1. [Epoch 1 — Diagnosis and external MVP repro](epoch-1-diagnosis-and-mvp.md) — shipped as **v1.3.3** (`logProjectOutputDiagnostics`).
2. [Epoch 2 — First fix attempt: `Workspace.TryApplyChanges`](epoch-2-first-fix-attempt-and-disk-corruption.md) — shipped as **v1.3.4**, then found to corrupt the real `.csproj` on disk. **Superseded by Epoch 3.**
3. [Epoch 3 — Correct fix: in-memory overlay](epoch-3-inmemory-overlay-fix.md) — shipped as **v1.3.5**. Recopy-on-edit superseded by **v1.3.6** mapping (prepare at load/refresh only).

## Current shipped behavior (v1.3.6)

- File preparation is separate from the `Solution` transform. Generations are content-hashed under `v2-main-only/` (not last-write-time directories).
- `GetCurrentSolution()` returns the stored `_solution` (fallback `workspace.CurrentSolution`). The getter does **not** recompute or recopy the overlay.
- Prepare runs at `load_workspace` / enable / explicit artifact refresh. Document edit, watcher flush, and post-apply reapply the in-memory mapping with no analyzer file I/O.
- The rewritten `Solution` is still **never** handed to `Workspace.TryApplyChanges` on the real `MSBuildWorkspace`.
- `RevertAnalyzerReferenceOverlayForApply` still strips analyzer-reference diffs before apply (exact inverse is epoch 4).
- Timestamp layout and recopy-on-edit below are **v1.3.5 history**.

## Fixed architectural decisions (v1.3.5 history)
- `SolutionManager.FindDocumentAsync` reads through `GetCurrentSolution()`, not `workspace.CurrentSolution` directly, so every MCP tool that resolves a `Document` (not just ones that call `GetCurrentSolution()` explicitly) sees the fix.
- Any future code path that still calls `Workspace.TryApplyChanges` (document edits from rename/code-fix/AST tools) is defended by `RevertAnalyzerReferenceOverlayForApply`, which strips any `AnalyzerReference` diff before the call, so the corruption in Epoch 2 cannot resurface through a different call path.
- Analyzer assemblies are loaded from the shadow copy via `Services/InProcessAnalyzerAssemblyLoader.cs` (a minimal public-API-only `IAnalyzerAssemblyLoader`), never from the real build output, so the MCP process never locks it.
- `find_symbol_definition` on a generator-emitted type name is a **known, out-of-scope Roslyn limitation** (`SymbolFinder` never searches source-generated documents — [dotnet/roslyn#63375](https://github.com/dotnet/roslyn/issues/63375), closed as by-design). This workaround does not and cannot fix that; `get_diagnostics_for_file` / `find_symbol_references` from a known usage site are unaffected.

## External repro (do not commit into this repo)

All three epochs were verified against an external MVP solution at
`C:\Scratch\GenRepro` (outside this repo, per the instruction that started
this investigation): two projects, `Generator` (a trivial
`IIncrementalGenerator` emitting `Consumer.GeneratedGreeter`) and `Consumer`
(references `Generator` via `<ProjectReference OutputItemType="Analyzer"
ReferenceOutputAssembly="false" />`), plus a root `Directory.Build.props` that
redirects `OutputPath` into `artifacts\{ProjectName}\{Configuration}\` instead
of the SDK default `bin\{Configuration}\{TFM}\`. If you need to re-run this
investigation, recreate the same shape — it is intentionally minimal and does
not depend on any real product code.

## Rules for anyone touching this area again

- Read this index and all three epoch documents before changing the shadow-copy overlay logic.
- Never reintroduce `workspace.TryApplyChanges(shadowCopiedSolution)` against the real `MSBuildWorkspace` — see Epoch 2 for exactly what breaks.
- Any new code path that resolves a `Document` or `Solution` for semantic analysis must go through `SolutionManager.GetCurrentSolution()` / `FindDocumentAsync`, not `workspace.CurrentSolution` directly, or it will silently miss the overlay.
- Any new code path that calls `Workspace.TryApplyChanges` must go through (or replicate) `RevertAnalyzerReferenceOverlayForApply` first.
- Keep `docs/ARCHITECTURE.md`'s "Workspace lifecycle" section synchronized with the actual code — it is the current-state description; this directory is the history.
