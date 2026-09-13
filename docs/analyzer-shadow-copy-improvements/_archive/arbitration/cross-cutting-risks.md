# Cross-cutting risks and consistency constraints

## 1. State must be split, not represented by one enabled flag

Interactions: E1-04, E1-09, E2-04, E3-06, E6-03; Astra N-06.

The revision must distinguish requested mode, prepared mapping, active generation,
last refresh result, and observed execution result. A boolean cannot represent a
partially prepared mapping, a stale-but-active generation, or a failed first-use
load. U-ARB-05 later selected session-sticky activation, so cached false/omitted
must not discard the mapping; disable starts a new workspace session.

## 2. Mapping lifetime and operation consistency are one contract

Interactions: R-02, R-03, E2-04, E4-01, E4-05; Astra N-02.

I/O-free reapply requires a stable mapping, while exact inverse application requires
the mapping that produced the candidate. Using only “the latest mapping” can strip
the wrong references after reload; retaining every historical `Solution` creates
the MVCC scope explicitly rejected by arbitration. The minimal operation context
must bind base snapshot/session and mapping generation, and stale mismatch must be
detected before side effects.

Within a semantic operation, do not resolve a symbol from one snapshot and then
select a fresh `GetCurrentSolution()` as the transformation base. Audit rename and
similar two-stage readers for this pattern.

## 3. The write workflow is not a transaction

Interactions: E4-02, E4-04, E4-06; Astra N-01.

Preflight prevents known-invalid candidates from writing files, but it cannot make
multiple document writes plus `MSBuildWorkspace` application atomic. Cancellation,
per-file I/O failure, or workspace rejection can leave partial persistence. The
revision must not combine “atomic `_solution` assignment” with “atomic save” in one
invariant. Structured partial-success/reconciliation outcomes are required; a
rollback engine is not implicitly authorized.

## 4. Write safety and load safety are independent

Interactions: E1-07, E3-01, E4-03, E4-06; Astra N-04.

Removing known overlay diffs before `TryApplyChanges` prevents temp `<Analyzer>`
items. It does not prove that no raw workspace compilation loads the real output.
Conversely, a loader fix does not make analyzer-reference persistence safe. Release
claims and tests must keep the two guarantees separate.

## 5. Content identity, dependency completeness, and CLR identity differ

Interactions: E2-01, E2-02, E2-07, E3-01, E3-02.

A content hash proves the bytes represented by one preparation policy and selects
an immutable path. It does not prove that the dependency set is complete or that
the CLR executes those bytes. Policy/version must be part of generation identity,
and epoch-3 execution tests remain mandatory even when epoch-2 tests pass.

## 6. Shared cache reuse expands integrity obligations

Interactions: E2-03, E2-05, E2-06; Astra N-05.

Keeping a shared per-solution root makes cross-process publication and validation
part of the supported contract. Manifest hashes alone are not a trust boundary if
manifest and files can both be changed. Path containment, ownership, no-replace
publication, corrupt-destination refusal, and cleanup only after owners stop must
remain consistent. Do not combine a shared root with per-session deletion.

## 7. Provenance and dependency resolution select executable code

Interactions: E3-04, E5-01, E5-02, E5-04.

Both matcher fallback and simple-name dependency probing choose code to execute.
Diagnostics cannot compensate for executing the wrong assembly. Unknown analyzer
diffs, unproved provenance, and conflicting helper resolution require explicit
policy/failure; they must not be converted into “best effort” pass-through at a
different layer.

## 8. Enablement must be atomic with the intended load session

Interactions: E1-02, E1-04, E6-03; Astra N-03.

`WorkspaceTools.LoadWorkspace` currently loads and enables overlay in separate
locked calls. If concurrent MCP requests are possible, another load can intervene
and bind the first request's opt-in/summary to a different solution. Verify dispatch
serialization. If concurrency is possible, the internal operation must carry an
expected load/session identity or hold one orchestration scope; no new public
parameter is required.

## 9. Loader tests require process isolation

Interactions: E1-03, E2-05, E3-01, E3-04; Astra N-07.

The loader, CLR assemblies, and global resolver can outlive a workspace. Parallel
tests in one test process can become order-dependent and falsely pass/fail identity
scenarios. Use one process per lifecycle scenario, except that steps intentionally
testing same-process reload stay together. The publication race uses two such
processes and one test-owned root.

## 10. Compatibility statements must follow the chosen lifecycle

Interactions: E1-04, E2-04, E3-05, E6-01, E6-04.

Any change to false/omitted handling and removal of recopy-on-edit can both change
observable v1.3.5 behavior. The user workflow must match the selected flag,
artifact-refresh, and loader contracts. Documentation may finish with deferred
functionality, but the series and release status must not imply those runtime
changes shipped.
