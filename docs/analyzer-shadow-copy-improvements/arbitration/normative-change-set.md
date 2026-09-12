# Normative change set

## Series README

1. Label every guarantee as one of: current v1.3.5 behavior, target invariant,
   or release gate introduced by a named epoch.
2. Keep these target invariants: overlay analyzer-reference changes never reach a
   project file; published generations are immutable; preparation failure is not
   reported as successful refresh/execution; the feature remains opt-in.
3. Replace the single snapshot-consistency statement with three definitions:
   immutable snapshot integrity, disk freshness after a specified flush boundary,
   and one-snapshot consistency for a semantic operation.
4. Change the dependency graph so epoch 4 depends on the original↔shadow mapping
   contract delivered by epoch 2. State that content-addressed paths do not satisfy
   epoch 3's CLR binding gate.
5. Define these terms once and use them in every epoch:
   - `cached load`: same load key, graph not stale, existing workspace reused;
   - `reset+load`: workspace disposed and reopened in the same server process;
   - `process restart`: new server process and loader;
   - `artifact refresh`: required source bytes are reread and hashed;
   - `overlay reapply`: a prepared mapping is applied without file I/O.
6. State the current v1.3.5 behavior of `shadowCopyInSolutionAnalyzers`: a same-key
   cached load with `false` or omission does not disable a previously active
   overlay; reset followed by load does. Do not change this behavior until
   U-ARB-05 is decided. **Satisfied in v1.3.13:** U-ARB-05 retained this behavior
   as the session-sticky contract.

## Epoch 1 — lifecycle verification

1. Implement the integration oracle in an isolated test process with real MSBuild
   bootstrap and the production `SolutionManager`/analyzer loader lifecycle.
2. Obtain one overlay `Project` snapshot, compile it, locate the known generated
   type/field, and assert `IFieldSymbol.ConstantValue` equals the expected `V1` or
   `V2`. Treat missing type, field, or constant as failure. Do not use an empty
   diagnostic set as a version oracle and do not add a public MCP API.
3. Keep Consumer source unchanged between V1 and V2, make it use the generated
   member, and add a negative control with an unavailable generator.
4. Run and record cached load, reset+load in the same process, and process restart.
   For each, record graph cache hit/reopen, artifact-refresh attempt, selected
   generation, loaded assembly identity/path, and executed marker.
5. Add same-load-key flag transitions: true→false, true→omitted, false→true, and
   each transition after reset. Assert and record current behavior; use the final
   U-ARB-05 decision for any later behavioral acceptance test.
6. Add solutions A and B with the same analyzer assembly identity and different
   markers. Load A with overlay enabled, then B with its correct analyzer path and
   overlay disabled. Assert B's marker and loaded path/identity. In the broken-path
   disabled variant, assert absence of generation rather than B's marker.
7. Name and independently test these production write paths:
   `UpdateDocumentInMemoryAsync`, overlay-derived
   `ApplySolutionChangesToDiskAsync`, and real FSW delivery followed by
   `FindDocumentAsync` or `GetCurrentSolutionAfterDiskSyncAsync` flush.
8. For every write path, assert the exact generated marker, requested document
   text, and byte-identical `.csproj` files.
9. Test both a missing resolved analyzer path and an existing correct analyzer
   path. After semantic execution and after every write path, force a generator
   rebuild that writes changed DLL bytes; assert success and record actual paths
   loaded by the server.
10. For the watcher case, separately observe dirty-event delivery with a bounded
    wait, invoke the production flush, then assert published text and marker. Keep
    at least one real FSW test even if an internal seam is used elsewhere.
11. Add a loaded-shadow→document-edit→overlay-reapply case with injected
    preparation failure. Assert that reapply results remain observable and the
    active mapping is not silently replaced with broken original references.
12. Make several Consumers sharing one generator a mandatory epoch-1/2 case.
13. Add a foreign analyzer and an in-solution candidate with the same filename/
    assembly name but distinct generated markers. Use executed identity as the
    assertion and make this an epoch-5 rollout gate.
14. Helper-only and conflicting-helper experiments must label preparation,
    binding, and execution outcomes separately. They do not establish support
    before epoch 3 selects and implements a contract.
15. Run each loader lifecycle scenario in its own process; keep the multi-step
    reload sequence within that one process. Run the shared-publication test with
    two isolated child processes sharing only the test-owned shadow root.

## Epoch 2 — immutable generations and mapping

1. Split preparation from `Solution` transformation. Preparation returns an
   immutable mapping containing session/load identity, project ID, original
   analyzer reference, shadow analyzer reference, generation ID, and preparation
   result. Overlay reapply consumes only this mapping.
2. Restrict the initial preparation policy to the main analyzer DLL. Define the
   generation ID as a format/policy version plus an ordered set of normalized
   relative paths and content hashes.
3. Reserve a different policy/layout namespace for the later dependency-set
   generation. Never reuse or supplement a main-only published generation as a
   dependency-complete generation. Put helper-only invalidation acceptance in
   epoch 3.
4. On every operation that promises artifact refresh, reread and hash all required
   bytes regardless of cached workspace graph, timestamp, or size. Add a changed-
   bytes/same-size/same-timestamp test.
5. On document edit, watcher flush, reconciliation, and post-apply publication,
   reuse the prepared mapping without copying, hashing, or reading analyzer files.
6. Define refresh failure as a result distinct from ordinary edit success. An
   older active mapping may remain only with an explicit stale-generation result;
   never report the attempted refresh as successful.
7. Write all required files to a unique staging directory, close them, write the
   manifest last, then perform one same-volume directory move without replacement.
8. Before reuse, validate manifest format/policy version, required relative path
   set, safe containment under the generation root, and hashes of all required
   files. Reject a destination with a missing/invalid manifest; do not overwrite
   or delete it as part of publication.
9. Add interrupted staging, missing/corrupt manifest, corrupt file, competing
   publisher, and move-failure tests. State that publication is a logical readiness
   protocol, not a power-loss-durable filesystem transaction.
10. Retain the shared per-solution root for this revision. Add a two-process race
    and reuse test using one test-owned root, including termination of one publisher
    and successful validated reuse by the survivor.
11. Specify ownership, measured bytes per generation, expected growth over repeated
    rebuild/refresh, disk-full behavior, and a supported operator cleanup procedure
    that runs only after owning processes stop. Do not delete published generations
    during `ClearWorkspaceAsync`/workspace dispose and do not add automatic GC in
    this epoch.
12. Treat manifest paths as untrusted cache data: reject absolute paths, traversal,
    and resolved paths outside the generation directory before reading or cleanup.

## Epoch 3 — loader and dependencies

1. Split the epoch into a feasibility decision and implementation of the selected
   supported mode.
2. In feasibility, run V1→V2 with unchanged assembly name/version for cached load,
   reset+load, and process restart, using the exact executed-marker oracle.
3. Select one supported update contract from the evidence:
   - proven in-process update, with its precise supported operations; or
   - restart required, with an explicit pre-execution rejection/diagnostic for an
     unsupported in-process refresh.
4. Do not mark implementation complete until the selected contract's acceptance
   tests pass. Do not require in-process hot reload merely to complete the epoch.
5. Test dependency preparation first with a fixture whose main and private helper
   files are explicitly known. Do not use “copy all output `*.dll` except a
   blacklist” as the production discovery algorithm.
6. Before production helper support, specify and test a verified dependency
   discovery source. If none is available, explicitly limit helper support and
   diagnose the limitation.
7. If an ALC prototype is evaluated, define before implementation the exact host
   contract assemblies shared by identity, version-compatibility rules, and the
   prohibition on loading generation-private copies of those contracts. Add a
   negative duplicate-contract test and a positive generator-execution test.
8. Instrument `AddDependencyLocation`, requesting assembly, selected dependency
   path, and generation during feasibility.
9. Support conflicting helper versions only with proven requester/generation-
   scoped resolution. Otherwise reject that configuration before returning
   semantics; never search an unordered process-global directory set and execute
   the first matching simple name.
10. Tie resolver/context cleanup to the actual owner lifetime and absence of
    dependent operations. Do not promise unload or handler removal at workspace
    clear.
11. Define separate states and diagnostics for prepared, reference rewritten,
    load failed, and execution observed. The load response's rewrite count must not
    claim generation executed successfully.
12. Keep loading lazy unless a separate measured decision selects eager validation.
    Correlate first-use load failures with project, generation, and dependency.
13. Measure repeated reload memory/disk behavior and verify the real main/helper
    output remains writable by forced rebuild.

## Epoch 4 — workspace write boundary

1. Require the epoch-2 mapping contract before implementing inverse overlay logic.
2. Pass an operation context containing the immutable base snapshot identity and
   the mapping/generation used to construct the candidate. Do not persist arbitrary
   snapshot history or implement a merge engine.
3. Before any document or project-file write, validate that the current load
   session/base is compatible, classify analyzer-reference differences, and strip
   only entries produced by the known mapping.
4. Reject stale/incompatible candidates and unsupported analyzer-reference diffs
   before side effects. Do not silently erase unknown diffs and do not pass them to
   `MSBuildWorkspace`. Keep intentional analyzer-reference editing out of scope
   until it has a separate contract.
5. Preserve unrelated analyzer references and their ordering. Add exact-inverse,
   no-overlay, unknown-diff, stale-session, added-project, and removed-project
   tests. Do not infer mapping from a temp-path prefix.
6. Define one orchestration workflow covering preflight, overlay inversion,
   workspace apply, disk persistence, reconciliation, and overlay publication.
   Under-lock callers may enter the workflow at an internal under-lock stage; they
   must not reacquire the same semaphore.
7. Define outcomes for: preflight rejection with no side effects; full success;
   persistence/apply failure after some files changed; reconciliation success; and
   reconciliation failure. Never publish unapplied project-state changes.
8. Return or internally propagate enough structured status to distinguish full
   success, partial persistence, and reconciliation failure. Test cancellation and
   per-file I/O failure. Do not claim multi-file/project atomic rollback.
9. After successful apply or reconciliation, publish one complete overlay snapshot
   from the prepared mapping without analyzer file I/O.
10. Test text edit, overlay-derived apply, watcher+flush, and fallback for exact
    marker, requested text, and byte-identical `.csproj` files.
11. State explicitly that the write boundary protects persistence, not analyzer
    load isolation. Run the existing-path load/lock matrix and inventory semantic
    reads of raw `workspace.CurrentSolution`; create a separate load-boundary design
    only if a leak is reproduced and localized.

## Epoch 5 — reference provenance

1. Make evaluated-metadata discovery a no-mutation feasibility gate before matcher
   rollout. Record every available link from analyzer item to source project in
   Roslyn 5.9.0, required MSBuild evaluation, global properties, cost, and stability.
2. Do not change the shipped matcher until the missing-path policy in
   `unresolved.md` is decided.
3. Define a matcher decision table with these distinct rows: exact resolved output;
   provenance-confirmed project with stale/wrong original path; proven foreign
   analyzer; missing original path without proof; inaccessible original path;
   ambiguous candidates; and missing source output.
4. Never equate inaccessible with missing. Never call unique assembly-name matching
   proof of provenance. Apply the final missing/inaccessible actions only after the
   unresolved decision is recorded.
5. Select candidates from actually loaded inner projects, their global properties,
   and resolved outputs. Do not parse an unevaluated `TargetFrameworks` list to
   choose an output. If multiple loaded candidates remain and verified provenance
   does not disambiguate them, skip.
6. Add stable internal result codes for successful rewrite, missing source output,
   ambiguous assembly name, proven foreign path, unconfirmed provenance, access
   failure, and preparation failure. Store original-path state and selected-source
   state separately from the reason code.
7. Keep the public response summary compact; do not add a public structured schema
   in this revision.
8. Gate rollout on the epoch-1 same-name foreign analyzer fixture and the original
   missing-path repro, both using distinguishable executed markers.

## Epoch 6 — contract and documentation

1. Move correction of the false recompute-on-read description into an early
   factual-documentation task targeting `docs/ARCHITECTURE.md`, the current-state
   bullets in `docs/analyzer-shadow-copy/README.md`, and the remarks on
   `ShadowCopyInSolutionAnalyzerReferencesAsync`. Preserve historical descriptions
   as history and annotate the actual v1.3.5 behavior.
2. Keep epoch 6 as an audit. It must not first implement mapping reuse, write
   orchestration, matcher changes, or loader behavior.
3. Add an action × v1.3.5 behavior × selected target behavior × verification matrix
   covering cached load, reset+load, process restart, graph-stale reopen, overlay
   enable/disable transitions, edit, watcher delivery/flush, every apply path,
   preparation failure/partial mapping, fallback/reconciliation, and clear.
4. For every matrix row, state effects on the workspace graph, published snapshot,
   prepared mapping, active generation, loader lifetime, watcher dirty set, and
   returned status.
5. Document the behavioral change from recopy-on-edit to mapping reuse and the
   selected build/update workflow. Do not imply reset unloads CLR assemblies.
6. Permit the documentation audit to complete when epoch 3 has a selected,
   accurately documented implemented result or an explicitly deferred result.
   Track overall series completion separately; do not mark unimplemented runtime
   acceptance as complete through documentation.
