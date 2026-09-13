# Epoch 2 — First fix attempt: `Workspace.TryApplyChanges` (superseded)

Status: **shipped as v1.3.4**, then found during its own follow-up
verification to corrupt the real `.csproj` on disk. **Superseded by
[Epoch 3](epoch-3-inmemory-overlay-fix.md) (v1.3.5).** Kept here in full so the
mistake and its exact mechanism are not repeated.

## Baseline

Epoch 1 confirmed the root cause but shipped no fix. This epoch designs and
implements the actual rewrite.

## Decisions (as designed and shipped in v1.3.4)

- For every `AnalyzerReference` on a project whose file name (without
  extension) matches another **unambiguous** in-solution project's
  `AssemblyName` (ambiguous — two projects sharing a name — is left
  untouched rather than guessed):
  1. Resolve that matched project's own **build-confirmed** output —
     `Project.CompilationOutputInfo.AssemblyPath` or `Project.OutputFilePath`
     — not the (possibly broken) original `AnalyzerReference.FullPath`. This
     is reliable because Roslyn already resolved it, and in the repro it is
     the path the project was actually built to.
  2. Copy that file (plus its `.pdb`, best-effort) into a private shadow
     directory under the OS temp directory, namespaced by
     `{shadowRoot}\{matchedProjectName}\{sourceLastWriteTicks}\{fileName}` —
     the last-write-time segment means a rebuilt analyzer gets a fresh path
     (and therefore a fresh load) on the next `load_workspace`, instead of
     silently reusing a process-lifetime-cached stale assembly.
  3. Replace the original `AnalyzerReference` with a new
     `AnalyzerFileReference` pointing at the shadow copy, via
     `Solution.WithProjectAnalyzerReferences`.
  4. Load the shadow-copied assembly through a **custom**
     `IAnalyzerAssemblyLoader` (`Services/InProcessAnalyzerAssemblyLoader.cs`)
     built entirely on public Roslyn API, because Roslyn's own non-locking
     loader (`Microsoft.CodeAnalysis.AnalyzerAssemblyLoader.CreateNonLockingLoader`)
     is `internal` and not consumable from a normal NuGet reference.
- **Apply the rewritten `Solution` via `Workspace.TryApplyChanges`.** This was
  believed to be the correct, supported way to push a `Solution` mutation
  back into an *active* `MSBuildWorkspace` so that `SolutionManager.GetCurrentSolution()`
  (which at the time was `_workspace?.CurrentSolution ?? _solution`) and every
  MCP tool built on top of it would see the fix immediately — consistent with
  the existing pattern already used elsewhere in `SolutionManager` for
  document-text updates (`UpdateDocumentInMemoryAsync`, disk-watcher sync).
  `MSBuildWorkspace.CanApplyChange` does in fact return `true` for
  `ApplyChangesKind.AddAnalyzerReference` / `RemoveAnalyzerReference`, so the
  call succeeded without error — the failure mode below is silent.

## Scope

- New `load_workspace` optional flag `shadowCopyInSolutionAnalyzers` (default
  `false`).
- New: `Services/AnalyzerReferenceShadowCopier.cs` (pure rewrite: `Solution →
  (Solution, RewriteResult[])`), `Services/InProcessAnalyzerAssemblyLoader.cs`.
- New: `SolutionManager.ShadowCopyInSolutionAnalyzerReferencesAsync()`,
  called from `WorkspaceTools.LoadWorkspace` after a successful load when the
  flag is set. Returns a one-line summary (`N rewritten, M skipped`); details
  go to the MCP server log.
- New tests: `RoslynMcpServer.Tests/AnalyzerReferenceShadowCopierTests.cs`
  (pure-function tests against an `AdhocWorkspace` fixture — these tests
  remain valid and unchanged in Epoch 3, since the pure rewrite logic itself
  was never the defective part).

## Implementation delta

`Services/SolutionManager.cs` (v1.3.4 shape):

```csharp
public async Task<IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult>> ShadowCopyInSolutionAnalyzerReferencesAsync(
    CancellationToken cancellationToken = default)
{
    // ...
    var (newSolution, results) = AnalyzerReferenceShadowCopier.ShadowCopyInSolutionAnalyzerReferences(
        workspace.CurrentSolution, shadowRoot, _analyzerAssemblyLoader);

    if (results.Any(r => r.Applied))
    {
        if (workspace.TryApplyChanges(newSolution))
        {
            _solution = workspace.CurrentSolution;
        }
        else
        {
            _logger.LogWarning("TryApplyChanges failed while applying analyzer reference shadow copies.");
        }
    }
    // ...
}
```

## Verification performed at the time (and why it looked correct)

- `load_workspace(shadowCopyInSolutionAnalyzers: true)` on `C:\Scratch\GenRepro`
  reported **"1 rewritten"** — the pure rewrite logic matched `Consumer`'s
  broken `AnalyzerReference` to `Generator`'s real, existing build output and
  produced a shadow copy.
- `get_diagnostics_for_file("Consumer/Program.cs")` reported **no compiler
  errors or warnings** — the `CS0103` from Epoch 1 was gone. This appeared to
  confirm the fix worked, because at the time `TryApplyChanges` really had
  updated `workspace.CurrentSolution`, and `GetDiagnosticsForFile` resolved
  its `Document` from `workspace.CurrentSolution` (see Epoch 3 for why this
  coupling itself was also a latent problem).
- `find_symbol_definition("GeneratedGreeter")` — **still not found.** This was
  flagged at the time as an unexplained "mixed outcome" requiring further
  investigation (it is explained in Epoch 3: a separate, unrelated,
  by-design Roslyn limitation, not a defect in this fix).

Version 1.3.4 was published (`dotnet publish`, MCP reloaded, `get_mcp_server_info`
confirmed `1.3.4.0`) on the strength of this evidence, before the disk-write
side effect was discovered.

## The defect: `TryApplyChanges` persisted the change to the real `.csproj`

Continuing verification (real `dotnet build` of the whole repro solution,
which the diagnostics/symbol checks above never exercised) surfaced the
actual defect:

```
C:\Scratch\GenRepro\Consumer\obj\Debug\Generator\Generator.HelloGenerator\GeneratedGreeter.g.cs(5,29):
    error CS0102: The type 'GeneratedGreeter' already contains a definition for 'GeneratedMarker'
C:\Scratch\GenRepro\Consumer\obj\Debug\Generator\Generator.HelloGenerator\GeneratedGreeter.g.cs(7,30):
    error CS0111: Type 'GeneratedGreeter' already defines a member called 'GetGeneratedMarker' ...
```

This reproduced even from a fully clean `obj`/`bin`/`artifacts` state — ruling
out stale incremental-build cache. Inspecting `Consumer.csproj` on disk found
it had been silently rewritten:

```xml
<ItemGroup>
  <ProjectReference Include="..\Generator\Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
<ItemGroup>
  <Analyzer Include="..\..\..\Users\VuDZ\AppData\Local\Temp\RoslynMcpServer.AnalyzerShadowCopy\GenRepro_1A71D719\Generator\639245350847977454\Generator.dll" />
</ItemGroup>
```

Root cause: `MSBuildWorkspace` supports `ApplyChangesKind.AddAnalyzerReference`
/ `RemoveAnalyzerReference` by **editing the backing project file** — this is
by design (it is how e.g. `dotnet-format`-style command-line tools built on
`MSBuildWorkspace` are meant to persist changes). Calling `TryApplyChanges`
with the shadow-copy-rewritten solution therefore:

1. **Injected** a new, literal `<Analyzer Include="...">` MSBuild item
   pointing at a machine-/session-specific temp path (which is deleted or
   regenerated with a *different* path on the next process run — this alone
   makes it unfit to persist even ignoring the corruption angle).
2. **Could not remove** the original `ProjectReference
   OutputItemType="Analyzer"` reference — it is not a literal `<Analyzer>`
   item; it is synthesized by MSBuild/the SDK from the `ProjectReference`
   metadata, so the project-file writer has nothing to delete.

Both references stayed active. `Consumer` therefore ran the `Generator`
analyzer **twice** during a real `dotnet build` — once via the original
`ProjectReference`-derived analyzer, once via the newly injected literal
`<Analyzer>` item — producing the same generated `GeneratedGreeter` type
twice in the same compilation, hence `CS0102`/`CS0111`.

The repro's `Consumer.csproj` was manually restored to its pre-corruption
content (`git` was not available in that external scratch folder) and
re-verified clean before Epoch 3's fix was implemented.

## Compatibility / migration impact

- v1.3.4 was published and reloaded in this same working session but not
  distributed beyond it. No known external consumer ran
  `shadowCopyInSolutionAnalyzers=true` against a real project on v1.3.4.
- **If any `.csproj` was ever touched by a v1.3.4 `load_workspace` call with
  `shadowCopyInSolutionAnalyzers=true`, check it for a stray**
  `<Analyzer Include="...RoslynMcpServer.AnalyzerShadowCopy...">` **item and
  remove it manually** — v1.3.5 does not attempt to auto-detect or clean up
  this specific historical corruption, since it never writes such an item in
  the first place going forward.

## Exit / handoff criteria

- The pure rewrite logic (`AnalyzerReferenceShadowCopier`) and its tests are
  correct and carried forward unchanged into Epoch 3.
- The application mechanism (`Workspace.TryApplyChanges` against the real
  `MSBuildWorkspace`) is confirmed unsafe for `AnalyzerReference` changes and
  must never be reintroduced for this purpose.
- Proceed to Epoch 3: keep the overlay purely in-memory.
