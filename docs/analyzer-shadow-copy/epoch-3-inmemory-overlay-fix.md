# Epoch 3 — Correct fix: in-memory overlay

Status: **shipped as v1.3.5**. Current shipped design; see
`docs/ARCHITECTURE.md` ("Workspace lifecycle") for the day-to-day description.

## Baseline / problem

Epoch 2 shipped a working-looking fix that turned out to corrupt the real
`.csproj` on disk, because it applied the analyzer-reference rewrite through
`Workspace.TryApplyChanges` against the real `MSBuildWorkspace`. The pure
rewrite logic (`AnalyzerReferenceShadowCopier`) was correct; only the
*application* mechanism was wrong. This epoch redesigns just that part.

## Decisions

1. **Never call `Workspace.TryApplyChanges` with the analyzer-reference-rewritten
   `Solution`.** The rewrite must stay purely in-memory — a projection, not a
   workspace mutation.
2. `SolutionManager` tracks whether the overlay is active
   (`_shadowCopyAnalyzersEnabled`) and the shadow-copy root directory
   (`_shadowCopyRootDirectory`) computed once. `ShadowCopyInSolutionAnalyzerReferencesAsync`
   sets these and assigns `_solution` directly — it does not touch
   `workspace.CurrentSolution` at all.
3. `SolutionManager.GetCurrentSolution()` becomes the single "apply the
   overlay if enabled" choke point:
   ```csharp
   public Solution? GetCurrentSolution()
   {
       // Prefer the locally-tracked snapshot: it carries the analyzer-reference
       // shadow-copy overlay which must never be pushed into workspace.CurrentSolution.
       return _solution ?? _workspace?.CurrentSolution;
   }
   ```
   (Precedence flipped from Epoch 2's `_workspace?.CurrentSolution ?? _solution`.)
4. Every place that previously cached `_solution = workspace.CurrentSolution`
   after a *document*-level `TryApplyChanges` (which is safe — see point 6)
   now instead calls a new private helper,
   `ApplyShadowCopyOverlayIfEnabled(workspace.CurrentSolution)`, so the
   overlay survives later document edits / disk-watcher syncs without ever
   being written back into the workspace:
   - `UpdateDocumentInMemoryAsync`
   - `UpdateDocumentInMemoryUnderLockAsync`
   - `FlushDirtyDocumentsUnderLockAsync` (disk-watcher batch sync)
   - `ApplySolutionChangesToDiskAsync` (see point 6 for its extra guard)
   - `LoadCoreAsync` (the initial `_solution` assignment right after
     `OpenSolutionAsync`/`OpenProjectAsync` — a no-op here since the flag is
     always false immediately after a fresh load, but kept consistent so the
     invariant "every `_solution` assignment goes through this helper" has no
     exceptions).
   `ApplyShadowCopyOverlayIfEnabled` itself just re-invokes
   `AnalyzerReferenceShadowCopier.ShadowCopyInSolutionAnalyzerReferences`
   (the same pure function from Epoch 2, unchanged) against whatever
   `workspace.CurrentSolution` currently is, and returns the result — it is a
   no-op when the flag was never enabled for this load.
5. **`SolutionManager.FindDocumentAsync` must also route through
   `GetCurrentSolution()`,** not `workspace.CurrentSolution.Projects` directly.
   This was a second, distinct gap found while re-verifying: `get_diagnostics_for_file`,
   `find_symbol_references`, and the AST/code-fix tools all resolve their
   starting `Document` via `FindDocumentAsync`, then call
   `document.GetSemanticModelAsync()` — a `Document` obtained from the
   non-overlaid `workspace.CurrentSolution` compiles against the original
   (broken) `AnalyzerReference`, regardless of what `GetCurrentSolution()`
   would have returned. Fixed by changing `FindDocumentAsync` to search
   `GetCurrentSolution() ?? workspace.CurrentSolution` instead.
6. **Defensive guard for any surviving `TryApplyChanges` call site:**
   `RevertAnalyzerReferenceOverlayForApply(candidate, workspaceCurrentSolution)`.
   Document-editing tools (rename, code fix, generated test stub, AST
   mutation) resolve their starting `Document`/`Solution` via
   `FindDocumentAsync`/`GetCurrentSolution()` (now overlay-aware per point 5),
   edit it, and call `SolutionManager.ApplySolutionChangesToDiskAsync(oldSolution,
   newSolution)`. Since `newSolution` in that flow descends from the
   overlay, it still differs from `workspace.CurrentSolution` in
   `AnalyzerReferences` — feeding it straight into `TryApplyChanges` would
   silently reproduce Epoch 2's corruption through a completely different
   call path (a rename, not `load_workspace` itself). The guard walks every
   project in `candidate`, and for any project whose `AnalyzerReferences`
   differ from the corresponding project in `workspaceCurrentSolution`,
   overwrites them back to `workspaceCurrentSolution`'s (the "real",
   never-overlaid) references via `Solution.WithProjectAnalyzerReferences`
   before the solution reaches `TryApplyChanges`. It is a no-op whenever the
   flag was never enabled, or when a project has no analyzer-reference
   difference (the normal case for every edit that isn't touching analyzer
   references, which is all of them).
   `ApplySolutionChangesToDiskAsync` now computes
   `solutionToApply = RevertAnalyzerReferenceOverlayForApply(newSolution, workspace.CurrentSolution)`
   and passes `solutionToApply` (not the raw `newSolution`) to `TryApplyChanges`.
   The other three `TryApplyChanges` call sites
   (`UpdateDocumentInMemoryAsync`, `UpdateDocumentInMemoryUnderLockAsync`,
   `FlushDirtyDocumentsUnderLockAsync`) all build their `newSolution` directly
   from `workspace.CurrentSolution.WithDocumentText(...)`, never from the
   overlay, so they were already safe and did not need the guard.
7. Reset `_shadowCopyAnalyzersEnabled` / `_shadowCopyRootDirectory` in
   `ClearWorkspaceAsync` and at the start of a full reload in `LoadCoreAsync`,
   so a stale flag from a previous `load_workspace` call never leaks into an
   unrelated later solution.

## Scope and non-goals

- In scope: fix the application mechanism only. No tool parameter changed —
  `shadowCopyInSolutionAnalyzers` behaves identically from the caller's side.
- Out of scope: fixing `find_symbol_definition`'s inability to find a
  generator-emitted type by name. Investigated and confirmed to be a
  **separate, by-design Roslyn limitation**: `SymbolFinder.FindDeclarationsAsync`
  / `FindSourceDeclarationsAsync` never search source-generated documents at
  all — see [dotnet/roslyn#63375](https://github.com/dotnet/roslyn/issues/63375)
  ("Closing out as by design. All these systems work off of indices cached
  for real documents. We have no bandwidth or desire to update them for SG
  scenarios." — Roslyn maintainer). This is orthogonal to whether the
  `AnalyzerReference` resolves correctly: `get_diagnostics_for_file` and
  `find_symbol_references` from a known usage site both work correctly once
  this epoch's fix is applied, because they go through a real document's
  compiled semantic model, not `SymbolFinder`'s name-based declaration index.
  A future enhancement could special-case `find_symbol_definition` to also
  scan `Project.GetSourceGeneratedDocumentsAsync()` — not attempted here.

## Implementation delta

- `Services/SolutionManager.cs`:
  - New fields `_shadowCopyAnalyzersEnabled`, `_shadowCopyRootDirectory`.
  - `GetCurrentSolution()` precedence flipped to `_solution ?? _workspace?.CurrentSolution`.
  - `ShadowCopyInSolutionAnalyzerReferencesAsync` rewritten to never call `TryApplyChanges`.
  - New private `ApplyShadowCopyOverlayIfEnabled(Solution)`.
  - New private `RevertAnalyzerReferenceOverlayForApply(Solution candidate, Solution workspaceCurrentSolution)`.
  - `FindDocumentAsync` now searches `GetCurrentSolution() ?? workspace.CurrentSolution`.
  - `ApplySolutionChangesToDiskAsync` applies the revert guard before `TryApplyChanges`.
  - `ClearWorkspaceAsync` / `LoadCoreAsync` reset the shadow-copy flags on
    dispose / full reload.
- `Services/AnalyzerReferenceShadowCopier.cs`: doc comment updated to
  describe the new caller contract (no `TryApplyChanges`); no logic change.
- New tests: `RoslynMcpServer.Tests/SolutionManagerAnalyzerOverlayTests.cs`
  (3 tests, via reflection into the private guard, using an `AdhocWorkspace`
  fixture — no live `MSBuildWorkspace` needed since the guard only transforms
  plain `Solution` objects):
  - `RevertAnalyzerReferenceOverlayForApply_strips_overlay_diff_when_shadow_copy_enabled`
  - `RevertAnalyzerReferenceOverlayForApply_is_noop_when_shadow_copy_not_enabled`
  - `RevertAnalyzerReferenceOverlayForApply_leaves_matching_references_untouched`
- `RoslynMcpServer.Tests/AnalyzerReferenceShadowCopierTests.cs`: unchanged
  (pure-function tests remain valid).
- Docs: `docs/ARCHITECTURE.md` "Workspace lifecycle" section, root `README.md`
  ("Agent tools by version" v1.3.5 entry; v1.3.4 entry marked superseded),
  `.cursor/rules/roslyn-mcp-overview.mdc` version history.
- Version: `RoslynMcpServer.csproj` `1.3.4` → `1.3.5` (patch — correctness fix
  to an already-shipped, not-yet-externally-consumed feature; no tool
  parameter or schema change).

## Compatibility / migration impact

- No MCP tool parameter or schema changed. Callers of `load_workspace
  shadowCopyInSolutionAnalyzers=true` see identical request/response shape.
- See Epoch 2's "Compatibility / migration impact" for the one-time cleanup
  note about any `.csproj` a v1.3.4 process may have already corrupted.
- `SolutionManager.FindDocumentAsync`'s behavior change (now solution-overlay
  aware) is strictly additive/corrective: when no shadow copy is active it
  returns byte-identical results to before, since `GetCurrentSolution()`
  equals `workspace.CurrentSolution` in that case.

## Verification

All against `C:\Scratch\GenRepro`, on the published `v1.3.5.0` binary
(`get_mcp_server_info` confirmed), after restoring the repro's `Consumer.csproj`
to its pre-corruption content:

1. **Disk safety (the actual regression test for Epoch 2's defect):**
   `load_workspace(shadowCopyInSolutionAnalyzers: true)` → response confirms
   `Analyzer reference shadow copy: 1 rewritten.` → `Consumer.csproj` on disk
   is **byte-identical** to its pre-load content (no `<Analyzer Include>`
   injected, `ProjectReference` untouched).
2. **Semantic fix reaches diagnostics tools:**
   `get_diagnostics_for_file("Consumer/Program.cs")` → `No compiler errors or
   warnings found` (the `CS0103` from Epoch 1 stays fixed, and now via the
   `FindDocumentAsync` fix rather than an accidental workspace mutation).
3. **Real `dotnet build` stays clean** (this is the actual reproduction of
   the originally reported symptom, run independently of the MCP process
   while its workspace is still loaded with the fix active):
   - `dotnet build Generator\Generator.csproj -c Debug` → succeeds, confirms
     the MCP process never locked `Generator`'s real build output (`MSB3027`
     does not occur).
   - `dotnet build GenRepro.slnx -c Debug --no-incremental` (clean `obj`/`bin`/`artifacts`)
     → succeeds, `0 Warning(s)`, `0 Error(s)` — no `CS0102`/`CS0111` duplicate
     generator output (Epoch 2's regression is gone).
4. **`find_symbol_definition("GeneratedGreeter")`** — still reports "not
   found". Confirmed via [dotnet/roslyn#63375](https://github.com/dotnet/roslyn/issues/63375)
   to be an unrelated, by-design Roslyn limitation (see Scope/non-goals), not
   a defect in this fix.
5. **Automated suite:** non-incremental `dotnet build` of
   `RoslynMcpServer.sln` — succeeded (pre-existing unrelated `CS8603`
   warnings only). Full test suite: **313 passed, 0 failed** (310 pre-existing
   + 3 new `SolutionManagerAnalyzerOverlayTests`).

## Exit / handoff criteria

- Shipped as `v1.3.5` (`AssemblyVersion`/`FileVersion` `1.3.5.0`).
- Published via `publish-and-verify.ps1`; `get_mcp_server_info` confirmed the
  new binary and version after reload.
- Committed as `404190e` (`feat(mcp): v1.3.4/1.3.5 shadow-copy in-solution
  analyzer references (fix disk corruption)`), bundling the never-committed
  v1.3.4 feature together with this v1.3.5 fix, since v1.3.4 was never a
  safe, independently shippable state.
- `docs/ARCHITECTURE.md`, root `README.md`, and `.cursor/rules/roslyn-mcp-overview.mdc`
  all reflect `v1.3.5` and the corrected mechanism.

### Follow-up (outside shipped scope)

- Consider special-casing `find_symbol_definition` (or a new tool) to also
  search `Project.GetSourceGeneratedDocumentsAsync()` by symbol name, so
  generator-emitted types are discoverable by name and not only from a known
  usage site. Not attempted in this epoch; would need its own design (Roslyn
  does not provide this out of the box — see the linked upstream issue).
- No further action planned on the Epoch 2 `.csproj` corruption cleanup
  beyond the one-time manual-check note, since it was never shipped to an
  external consumer.
