# Finding verdicts

`ACCEPT WITH MODIFICATION` is the normative equivalent used here for a
partially valid finding. Each such verdict states the exact accepted boundary.

## Series README

### R-01 — ACCEPT WITH MODIFICATION

Grok is right that the unqualified word “guarantee” can be read as a current
product promise even though the series explicitly says implementation has not
started. Astra is right that a forward-looking specification may retain
immutability as a target invariant. The invalid step in Grok's suggested change
is removing invariant 4 until epoch 2: doing so would remove the release gate
that epoch 2 is meant to satisfy.

Change the README to label current v1.3.5 behavior, target invariants, and
per-epoch release gates separately. Keep immutable published generations as a
target/release invariant; do not claim it is already true.

### R-02 — ACCEPT WITH MODIFICATION

The code publishes immutable `Solution` objects atomically, so absence of a lock
in `GetCurrentSolution()` does not imply a partially mutated `Solution`. Grok is
nevertheless right that snapshot integrity, delivery/flush of disk events, and
use of one snapshot throughout a semantic operation are different guarantees.
Astra correctly notes that excluding all getter callers permanently would
weaken the intended semantic consistency goal without evidence.

Define three separate contracts: snapshot integrity, freshness after a specified
flush boundary, and single-operation snapshot consistency. Inventory every
semantic entry point against those contracts. Do not promise visibility of an
FSW event that has not been delivered, and do not permanently exempt a reader
that can feasibly follow the common operation contract.

### R-03 — ACCEPT

Exact inversion of overlay changes requires a stable original-to-shadow mapping.
The current whole-list replacement cannot distinguish overlay changes from an
intentional analyzer-reference change. Implementing epoch 4 before the mapping
would either preserve that defect or require a second redesign. Epoch 4 therefore
depends on the mapping contract from epoch 2, while epoch 3 retains a separate
binding gate that content-addressed paths do not satisfy.

### R-04 — REJECT

The criticism assumes that listing overwrite and in-process update in one open-
questions section assigns them to one solution. The proposal already gives file
mutation to epoch 2 and CLR binding/update to epoch 3, and explicitly says a new
path does not prove new execution. Cache-hit semantics still need clarification,
but that is E1-02, not evidence of a collapsed responsibility boundary.

## Epoch 1

### E1-01 — ACCEPT

The proposed oracle is not executable as written through the public MCP tools,
and empty diagnostics cannot distinguish V1 from V2. Use an in-process integration
test host that exercises the production `SolutionManager`/loader lifecycle and
reads the known generated symbol's constant value (optionally retaining generated
text as evidence). Do not introduce a public generated-document MCP API.

### E1-02 — ACCEPT

The code confirms that same-key `load_workspace` can be a RAM cache hit, while
`reset_workspace` disposes only the workspace and not the process-lifetime loader.
“Full reload” is therefore ambiguous. Define and test three distinct operations:
same-key cached load, reset plus load in the same process, and process restart.
Record whether the graph reopened and whether artifact refresh was attempted.

### E1-03 — ACCEPT

Resetting overlay fields does not reset the loader or CLR identities. A cross-
solution test that observes only flags is insufficient. Test solutions A and B
with the same analyzer assembly identity and distinct generated markers, and
verify the actually executed marker and loaded path/identity. Treat collision as
a risk until reproduced, not as an already proven v1.3.5 failure.

### E1-04 — ACCEPT WITH MODIFICATION

The same-path true-to-false cache-hit case is absent and the code demonstrably
keeps the old overlay. Grok is right that the behavior must be tested and made
explicit. Neither “opt-in” nor the current boolean description establishes whether
enablement is session-sticky or desired state on every invocation; false and
omitted are indistinguishable at the method boundary. Add true→false,
true→omitted, false→true, and reset variants, and document current sticky behavior
plus the reset workaround. The future flag semantics are U-ARB-05 and must not be
silently changed in this revision.

### E1-05 — ACCEPT

The fixture must use the generated member and the oracle must assert the exact
V1/V2 value while Consumer remains unchanged. Add a negative control with the
generator unavailable. This closes the false-positive path where diagnostics are
empty even though generation never ran.

### E1-06 — ACCEPT WITH MODIFICATION

Grok correctly identifies three non-isomorphic paths, but overstates the omission:
the proposal already lists rename separately from ordinary edit and watcher sync.
Make the coverage traceable rather than changing scope: name and test
`UpdateDocumentInMemoryAsync`, overlay-derived `ApplySolutionChangesToDiskAsync`,
and FSW delivery followed by the production flush. Each must assert exact marker,
new document text, and byte-identical project files.

### E1-07 — ACCEPT

The original requirement includes both recovery from a missing resolved path and
avoidance of locking an existing correct output. A missing-path fixture cannot
verify the second. Require both fixtures and force a build that writes new output
after each write path has caused semantic execution. Record the actual load path;
do not call a successful build proof of a particular suspected trigger.

### E1-08 — ACCEPT

FSW delivery only queues dirty paths; publication requires the production flush.
Test delivery and flush as separate observable phases, with bounded waits. A test
seam may make the flush deterministic but does not replace one real watcher test.

### E1-09 — ACCEPT

The current reapply path repeats file copying and discards `RewriteResult`; on a
copy failure it can publish original references while the enabled flag remains
set. Add an explicit loaded-shadow→edit→reapply failure regression and preserve
preparation/reapply status. Epoch 1 may close with a measured failure; epoch 2
must remove preparation from document-edit publication.

### E1-10 — ACCEPT WITH MODIFICATION

Several Consumers sharing one generator belongs in the mandatory epoch 1/2
matrix because it exercises repeated preparation/reuse. Grok's claim that it
necessarily fails on the first load is not established: copying and analyzer
loading are lazy/sequential in relevant parts of the current code. Helper and
version-conflict fixtures may be run early only after the exact oracle exists and
must report preparation, binding, and execution failures separately; supporting
them remains an epoch 3 decision.

## Epoch 2

### E2-01 — ACCEPT WITH MODIFICATION

The proposal intends helper changes to enter identity only after dependency
discovery in epoch 3, so Grok's claim of an immediate epoch-2 obligation is too
broad. The wording and cache layout are still ambiguous. Epoch 2's supported
policy is main-DLL-only. The generation identity must include a format/policy
version and the ordered relative-path/hash set; epoch 3's dependency-set policy
must use a distinct namespace and must not treat a main-only generation as
complete. The helper-only acceptance test belongs to epoch 3.

### E2-02 — ACCEPT

Any operation advertised as checking for new build output must hash the required
bytes. Timestamp and size may avoid work only where no artifact refresh is
promised; edit/watcher reapplication uses the in-memory mapping and performs no
artifact check. Add the same-size/same-timestamp changed-bytes test.

### E2-03 — ACCEPT WITH MODIFICATION

The readiness protocol is underspecified. Fully write and close required files,
write the manifest last in staging, then perform one same-volume move without
replacement. Reuse requires validation of format, file set, relative paths, and
hashes. A destination without a valid manifest is unusable and must not be
overwritten or deleted as “ours.” Grok's claim about a generally observable
Windows mid-move state is not established, so specify logical publication and
failure handling, not crash-durable transactional semantics.

### E2-04 — ACCEPT

Preparation must occur only at enable/load and explicitly defined artifact-
refresh/reopen boundaries. Document edits, watcher flushes, and post-apply
publication must apply a prepared in-memory mapping without hashing or copying.
A failed refresh may retain an older active generation only with an explicit
stale result; ordinary edits do not create refresh failures.

### E2-05 — ACCEPT WITH MODIFICATION

The current root is deterministic per loaded path, and the proposal explicitly
allows another participant to publish the same generation. That is a cross-
process contract, not a thread-only contract. Retain the shared-root design for
this revision and require a two-process test using one test-owned root, including
publisher termination and reuse by the survivor. A future switch to session roots
would be a different cache/ownership decision and must remove cross-process reuse
claims at the same time.

### E2-06 — ACCEPT WITH MODIFICATION

Immutable content-addressed generations make capacity and disk-full behavior a
release concern, but automatic retention is not an original requirement and
deleting on `ClearWorkspaceAsync` is unsafe while the loader or old compilations
may retain files. Add measured growth, a disk budget/operational cleanup policy,
ownership rules, and explicit disk-full/stale behavior in epoch 2. Do not require
automatic deletion or claim clear/dispose makes generations unreferenced.

### E2-07 — REJECT

The counterexample assumes an implementation with immutable tick-based identity.
That implementation already violates the explicit content-identity requirement
and the same-timestamp changed-bytes acceptance test. The proposal also separates
file identity from CLR binding in epoch 3. E2-01 and E2-02 make the existing rule
precise; no additional loader requirement follows from this finding.

## Epoch 3

### E3-01 — ACCEPT WITH MODIFICATION

Grok correctly identifies that a new path and workspace reset do not establish
fresh CLR execution, but an expected failure of current code cannot by itself
invalidate a desired improvement. The authoritative request, however, permits a
documented limitation and does not mandate hot reload. Make in-process V2 a
feasibility outcome, not an unconditional release gate. Run the three E1-02
operations, then select either a proven in-process mode or a restart-required
mode with explicit rejection/diagnosis of unsupported refresh. The actual mode
remains unresolved until the experiment exists.

### E3-02 — ACCEPT WITH MODIFICATION

ALC cannot solve files that were never prepared, so preparation/discovery must be
tested before binding isolation. Do not adopt Grok's production rule to copy all
`*.dll` with a blacklist; a shared output directory can contain unrelated or
incompatible assemblies. First use a fixture with an explicit known main+helper
set, then specify a verified production discovery source or explicitly limit the
supported dependency scenario. ALC remains contingent on the binding results.

### E3-03 — ACCEPT WITH MODIFICATION

Shared host contract identity is an input condition for any ALC prototype, not a
post-hoc policy. Enumerate the actual shared contracts and prohibit private copies
of them in the generation context; add positive and negative type-identity tests.
Grok's blanket formulation “Roslyn in Default plus System.*” is not a complete
general policy, so the exact list and version compatibility must be established
for this host.

### E3-04 — ACCEPT WITH MODIFICATION

The global resolver's unordered search across all dependency directories cannot
support conflicting helpers safely. Instrument actual `AddDependencyLocation`
behavior. Supporting conflicts requires proven requester/generation-scoped
resolution; otherwise the scenario is explicitly unsupported and must fail
without executing another generator's helper. Do not mandate “one loader per
generation” as sufficient, and do not promise handler removal merely on workspace
clear while dependent operations can remain alive.

### E3-05 — ACCEPT WITH MODIFICATION

The existing text is not internally contradictory: it says an interim release
does not complete an epoch whose current target is in-process update. The real
problem is that this target was not mandated by the original request. Split
completion of feasibility/contract selection from implementation completion.
A restart-required contract may be the final supported result after evidence and
an explicit decision; unsafe version mixing must reject with actionable guidance,
not silently return wrong semantics.

### E3-06 — ACCEPT WITH MODIFICATION

`Applied` currently means that a reference was rewritten after preparation; load
and execution remain lazy. Grok is right that these stages must not be conflated,
but eager loading is not required and changes cost/behavior. Define separate
prepared, rewritten, load-failed, and execution-observed states. Keep the short
summary scoped to rewrite, correlate first-use load diagnostics with project and
generation, and make eager validation a separate evidence-based decision.

## Epoch 4

### E4-01 — ACCEPT WITH MODIFICATION

A registry of all in-flight snapshots would contradict the non-goal of a general
concurrency/MVCC redesign. A current-session mapping alone is also insufficient
when reload can occur between read and save. Require only an operation context
containing the immutable base snapshot identity and its mapping/generation, with
compatibility preflight before side effects. Do not add persistent snapshot
history or a merge engine.

### E4-02 — ACCEPT WITH MODIFICATION

Grok correctly exposes the more serious ordering problem: current code writes
documents before classification/rejection. But passing unknown analyzer diffs to
`MSBuildWorkspace` is unsafe because it may persist them to the project file.
Preflight and classify before all side effects; strip only the known overlay;
reject unsupported analyzer diffs with no writes. Do not silently erase or
automatically pass through unknown diffs. Intentional analyzer editing needs a
separate explicit contract.

### E4-03 — ACCEPT WITH MODIFICATION

The write boundary prevents project-file persistence of temp references; it does
not prove that raw `workspace.CurrentSolution` can never load the real analyzer.
Grok's proposed trigger through each `TryApplyChanges` call is not yet proven.
Separate the write and semantic/load boundaries, run the existing-path load/lock
tests for all lifecycle operations, and make any load-boundary redesign contingent
on locating a real leak. Do not narrow the anti-lock requirement to missing paths.

### E4-04 — ACCEPT

The proposal incorrectly treats a failed `TryApplyChanges` as if nothing is then
published, while current code reconciles already-written texts. Specify four
paths: preflight rejection with no side effects, successful apply, failure after
some persistence, and reconciliation of what actually reached disk. Never publish
unapplied project changes. Expose full success, partial persistence, and
reconciliation failure distinctly; do not imply a filesystem transaction.

### E4-05 — ACCEPT

The current whole-list wipe cannot prove that only overlay changes were removed.
Make epoch 4 depend on epoch 2's mapping contract. Tests must invert only known
mapping entries, preserve unrelated references and order, reject unknown/stale
diffs according to preflight policy, and cover added/removed projects. Path-prefix
recognition is not provenance.

### E4-06 — ACCEPT

The common write workflow must include both safe workspace application and
post-apply publication of the prepared overlay mapping. Test all three E1-06
inputs and the E4-04 fallback for exact generated marker, new text, and unchanged
project bytes. Funnel structure alone is not acceptance, and post-apply must not
perform artifact I/O.

## Epoch 5

### E5-01 — UNRESOLVED

The proposal simultaneously requires proof of provenance, skip when proof is
absent, and continued recovery of the original missing-path repro. Neither side
has established whether Roslyn 5.9.0/evaluated MSBuild data can supply the needed
link. Grok's missing+unique-name rule preserves behavior but is still an
execution-selection heuristic and can replace a missing foreign analyzer.

Resolution requires a feasibility result listing available evaluated metadata,
its cost and stability, plus a distinct missing foreign same-name fixture. If no
proof exists, an explicit product decision must choose between retaining the
opt-in heuristic risk and narrowing support. Do not change the matcher before
that decision.

### E5-02 — ACCEPT WITH MODIFICATION

Existing foreign and missing-path cases need separate rules, but Grok's binaries
are too strong: an existing different path may be a stale output of a provenance-
confirmed project, while a missing path does not prove ownership. Add a decision
table for exact resolved output, confirmed project with wrong path, proven foreign
analyzer, missing path without proof, and inaccessible path. The last two follow
E5-01 and are not silently mapped to replace or skip. Preserve current behavior
until that gate is resolved.

### E5-03 — ACCEPT

Use the actually loaded inner `Project`, its global properties, and its resolved
output. Do not reinterpret the unevaluated `TargetFrameworks` list. Ambiguity is
among candidates present in the loaded solution, refined by verified provenance
if available; otherwise skip rather than select the first.

### E5-04 — ACCEPT WITH MODIFICATION

Stable reason codes are needed, but `provenance_unconfirmed` is a legitimate
specific result if the final policy skips on missing evidence. Introduce distinct
internal codes for missing source output, ambiguous assembly name, proven foreign
path, unconfirmed provenance, access failure, preparation failure, and successful
rewrite. Record original/source path state separately. Keep the public summary
compact; do not invent a structured public schema in this revision.

### E5-05 — ACCEPT WITH MODIFICATION

The proposal already depends on epoch 1, so Grok's claimed dependency omission is
incorrect. Add the same-filename foreign analyzer versus in-solution candidate
fixture with distinct markers to epoch 1 and make it an epoch-5 release gate.
Epoch 5 remains algorithmically independent of epochs 2–4, but cannot claim whole-
series readiness while their baseline failures remain. Exact execution identity,
not generated API shape alone, is the oracle.

## Epoch 6

### E6-01 — ACCEPT

Moving from recopy-on-edit to mapping reuse changes observable v1.3.5 behavior,
even if the old pickup was accidental and unreliable. Add an action × v1.3.5 ×
target × verification matrix and release/upgrade note. Define cached load,
reset+load, process restart, edit, and graph-stale reopen separately. Never imply
that reset unloads CLR assemblies.

### E6-02 — ACCEPT WITH MODIFICATION

The recompute-on-read statements conflict with code and should be corrected before
the final documentation epoch. Grok is wrong that the epoch text necessarily
requires waiting; the series README already says factual limitations are corrected
immediately. The next proposal must create an early factual-correction task for
`docs/ARCHITECTURE.md`, the historical README's current-state note, and the
`ShadowCopyInSolutionAnalyzerReferencesAsync` remarks; epoch 6 only audits them.
This arbitration pass itself leaves all protected source documents unchanged.

### E6-03 — ACCEPT

Build the lifecycle matrix from real entry points and branches: cache hit/miss,
graph-stale reopen, overlay enable/disable transitions, prepare failure/partial
mapping, all workspace-apply paths and fallback, watcher dirty/flush, and clear.
For each, specify graph, mapping, active generation, loader, watcher, and result
state. Do not hide these distinctions behind “load” or “reload.”

### E6-04 — ACCEPT WITH MODIFICATION

Reuse-on-edit is implemented and accepted in epochs 2/4, not introduced by a
documentation-only epoch. The fact that the proposal is forward-looking means
its target statement is not itself false, but epoch 6 cannot mark missing runtime
work complete. Epoch 6 may complete its audit with epoch 3 explicitly deferred or
with a restart-required contract; the status of the overall series must separately
show any unimplemented work.
