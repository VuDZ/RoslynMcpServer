# Analyzer / generator shadow copy: implementation epochs

This directory is the historical record of one investigation: why an
in-solution Roslyn analyzer/generator project referenced with
`OutputItemType="Analyzer"` can silently stop producing generated code (and
lock its own build output) when a repo-wide `Directory.Build.props` overrides
`OutputPath`, and how `RoslynMcpServer` came to fix it.

All three epochs below are **shipped**. This is not a forward-looking plan —
it is kept so the reasoning, the first (flawed) fix attempt, and the final
design are not lost. `docs/ARCHITECTURE.md` describes the current shipped
state; read it for the day-to-day behavior. Read this directory when you need
to know *why* it looks the way it does, or before touching
`Services/AnalyzerReferenceShadowCopier.cs`, `Services/SolutionManager.cs`'s
shadow-copy overlay, or `Services/ProjectOutputDiagnosticsLogger.cs`.

## Epoch order

1. [Epoch 1 — Diagnosis and external MVP repro](epoch-1-diagnosis-and-mvp.md) — shipped as **v1.3.3** (`logProjectOutputDiagnostics`).
2. [Epoch 2 — First fix attempt: `Workspace.TryApplyChanges`](epoch-2-first-fix-attempt-and-disk-corruption.md) — shipped as **v1.3.4**, then found to corrupt the real `.csproj` on disk. **Superseded by Epoch 3.**
3. [Epoch 3 — Correct fix: in-memory overlay](epoch-3-inmemory-overlay-fix.md) — shipped as **v1.3.5**. Current shipped design.

## Fixed architectural decisions (current state, v1.3.5)

- The shadow-copy rewrite (`Services/AnalyzerReferenceShadowCopier.cs`) is a **pure function**: `Solution in → (Solution out, RewriteResult[])`, unit-testable without a live workspace.
- The rewritten `Solution` is **never** handed to `Workspace.TryApplyChanges` on the real `MSBuildWorkspace`. `MSBuildWorkspace` persists `AddAnalyzerReference`/`RemoveAnalyzerReference` back into the backing `.csproj` — confirmed to corrupt it (Epoch 2).
- `SolutionManager.GetCurrentSolution()` re-derives the overlay on top of `workspace.CurrentSolution` on every read instead of caching an "applied" snapshot from the workspace.
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
