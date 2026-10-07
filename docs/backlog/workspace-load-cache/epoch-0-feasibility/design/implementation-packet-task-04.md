# Implementation packet E0/task-04 — independent fresh MSBuild comparator

Producer task-00. Contract filled; execution **blocked for positive equivalence;
isolated negative oracle scope accepted (2026-10-04)**.
[Post-install validation and evidence](../evidence/task-04-equivalence.md#post-install-validation-2026-10-04):
seven C# files/scaffold, negative attempts, fresh ordinary controls and synthetic
capabilities; after owner installed SDK 9.0.314 / reference pack 9.0.16,
negative attempts 13 Passed / 0 NotRun, positive attempts 13 NotRun.
Stages 2–7 not implemented/not-run, positive admission/API limits
require remediation. This execution-status summary changes no contract or
activation permission. Dispatch after accepted
task-01, task-02 with packet-03 Findings, and task-03. Read spec, design, audit,
packets-01/03, verification V01–V06/V23 and code-style in full before edits.
Task-04 is an experiment implementer; independent epoch acceptance remains a
different author-independent session required by task-execution.

## Allowlist / denylist

Existing files allowed: none. Exact new files under
`RoslynMcpServer.Tests/WorkspaceLoadCache/`: `IndependentWorkspaceOracle.cs`,
`WorkspaceObservationReader.cs`, `IndependentWorkspaceOracleTests.cs`,
`Models/WorkspaceObservation.cs`, `Models/OracleDifference.cs`,
`Models/OracleComparison.cs`, `Models/ScenarioOutcome.cs`.
Report `docs/backlog/workspace-load-cache/epoch-0-feasibility/evidence/task-04-equivalence.md`.
All other files denied, explicitly Services/Workspace/SolutionManager.cs,
Services/Analyzers/, Tools/, csproj/solution, production configuration,
shared experiment DTO/host/fixtures, spec and packets. Defect in a dependency is
returned to its owner, not patched here. Reports may reference raw files in owned
artifactDirectory; binary dumps with secrets are not committed.

## Exact proposed declarations

Namespace `RoslynMcpServer.Tests.WorkspaceLoadCache`; each model in matching
Models file. All collections immutable/nondefault, all ordinary strings nonnull.

```csharp
internal enum ScenarioOutcome
{
    Passed,
    Failed,
    NotRun
}

internal sealed record WorkspaceObservation(
    ImmutableArray<string> InstanceKeys,
    ImmutableArray<string> EdgeKeys,
    ImmutableSortedDictionary<string, string?> OptionValues,
    ImmutableSortedDictionary<string, string> Memberships,
    ImmutableSortedDictionary<string, string> SourceCharacters,
    ImmutableSortedDictionary<string, EncodingPolicy> Encodings,
    ImmutableSortedDictionary<string, ImmutableArray<byte>> SourceBytes,
    ImmutableArray<string> ReferenceKeys,
    ImmutableSortedDictionary<string, string> GeneratedTexts,
    ImmutableSortedDictionary<string, string> Constants,
    ImmutableArray<string> DiagnosticKeys);

internal sealed record OracleDifference(
    string Category,
    string Key,
    string? OrdinaryValue,
    string? HydratedValue);

internal sealed record OracleComparison(
    FixtureScenario Scenario,
    string Category,
    ScenarioOutcome Outcome,
    AdmissionEvidence Admission,
    DtbMeasurement CaptureDtb,
    DtbMeasurement HydrateDtb,
    DtbMeasurement OracleDtb,
    ImmutableArray<OracleDifference> Differences,
    ImmutableArray<string> Reasons,
    ImmutableArray<string> EvidencePaths);

internal sealed class IndependentWorkspaceOracle
{
    public Task<OracleComparison> CompareAsync(
        OracleFixtureCase fixture,
        string artifactDirectory,
        CancellationToken cancellationToken);

    public Task<ExperimentSession> OpenOrdinaryAsync(
        ExperimentRequest request,
        string artifactDirectory,
        CancellationToken cancellationToken);
}

internal static class WorkspaceObservationReader
{
    public static Task<WorkspaceObservation> ReadAsync(
        ExperimentSession session,
        AdmissionEvidence admission,
        FixtureExpectation expectation,
        CancellationToken cancellationToken);
}
```

Category = `positive-equivalence|negative-admission|capability`. Unsupported/unknown
can produce Passed only under negative-admission with expected reasons and proof
of no hydrate/DLL loading. NotRun means required environment/output unavailable;
Failed means executed assertion disagreed or a required call failed. Exceptions
from actual experiment are Failed unless explicitly missing prerequisite. Empty
Differences alone never implies Passed. Default or synthetic unobserved DTB measure
is `(0,0,0,null,false,[])`, not complete zero. Diagnostic evidence never contains
arbitrary environment/global properties. Missing side represented null difference;
empty string is a real value.

Observation contains actual generated texts **only as ephemeral oracle evidence**;
it is never a cache payload or source for hydrate. All keys are stable portable
instance/membership/generator keys in packet-03. No ProjectId, runtime object
identity or directory iteration order. Option values cover the entire packet-01
closed schema plus independently computed config diagnostic behavior. Reference
keys include project target, aliases, EmbedInteropTypes and metadata content hash;
duplicate paths with different reference properties remain distinct. Diagnostics
are a sorted multiset, not a set: preserve multiplicity, ID, severity, invariant
message, file+span and project context. Generated key includes generator identity
and hint/source identity; ambiguous public identity is a failure, never first match.

## Consumed signatures and ownership

From task-01, exactly as declared in packet-01:

```csharp
public HydrateHostExperiment(ExperimentAnalyzerAdmission? analyzerAdmission = null);
public Task<ExperimentSession> OpenFreshAsync(
    ExperimentRequest request, string artifactDirectory,
    CancellationToken cancellationToken);
public Task<SemanticSnapshot> CaptureAsync(
    ExperimentSession ordinary, AdmissionEvidence admission,
    CancellationToken cancellationToken);
public Task<ExperimentSession> HydrateAsync(
    SemanticSnapshot snapshot, AdmissionEvidence admission,
    string artifactDirectory, CancellationToken cancellationToken);
public static byte[] Encode(SemanticSnapshot snapshot);
public static SemanticSnapshot Decode(ReadOnlySpan<byte> payload);
public void Dispose();
```

Methods respectively belong to HydrateHostExperiment, SemanticSnapshotCodec and
ExperimentSession. All shared models, constructor fields and invariants are
incorporated from packet-01 without changes. `WorkspaceWriteResult` and ApplyAsync
are consumed only for round-trip byte assertion; full signature packet-01 applies.
`OracleFixtureBuilder.CreateAsync`, FixtureCase restore/verify/dispose and
FixtureExpectation exactly from packet-03. Dependency runner from design §7:

```csharp
public Task<AdmissionEvidence> InspectAsync(
    ExperimentRequest request, string artifactDirectory,
    CancellationToken cancellationToken);
public Task<AnalyzerProvenanceSnapshot?> RevalidateOverlayAsync(
    SemanticSnapshot snapshot, AdmissionEvidence evidence,
    Guid newSessionId, Solution baseSolution,
    CancellationToken cancellationToken);
```

Construct one DependencyEvidenceRunner for the attempt, call its InspectAsync,
then inject that same runner's RevalidateOverlayAsync into HydrateHostExperiment.
The delegate reconstructs fresh session-bound evidence after rechecks; it cannot
reuse old ProjectId/LoadSessionId or authorize a non-TaskOutput DLL. A declined
delegate/base-only session makes required generator output failed/not-run, not
Passed with two empty generated sets. Region fields/algorithms are exactly those
in packet-01, no independent interpretation here.
Require independently complete analyzer inventory and exact DTO/evidence
analyzer/dependency set equality before any empty-analyzer shortcut, as defined
in packet-01. A negative witness removes all AnalyzerEntry records while retaining
observed evidence; HydrateAsync must reject before host materialization, semantic
execution or DLL loading. Also exercise a missing dependency and incomplete
inventory coverage; none may pass through a base-only or empty-generated result.

InspectAsync owns build/log process cleanup, may change only fixture generated
outputs via MSBuild, returns unknown on unavailable evidence, does not execute
analyzer DLL. It precedes stable-tree hash establishment. OpenOrdinaryAsync
delegates only to task-01 OpenFreshAsync, returning a newly owned MSBuild session;
caller disposes returned session. No capture DTO used there. CompareAsync borrows
fixture exclusively, owns/disposes all three sessions/loggers and generated metadata
resources even on failure; does not dispose fixture. ReadAsync borrows session and
materializes observation before returning, leaves no lazy tasks/enumerables.
No production lock acquired; no concurrent operation on same session/fixture.
OCE propagates provided token; wrapper test records cancellation rather than pass.
Bad call arguments throw ArgumentException. Comparison/runtime exceptions become
Failed with bounded English reason and artifact locator, without swallowing OCE.

Public external signatures consumed, Roslyn 5.9.0:

```csharp
Task<Compilation?> Project.GetCompilationAsync(CancellationToken cancellationToken = default);
Task<IEnumerable<SourceGeneratedDocument>> Project.GetSourceGeneratedDocumentsAsync(
    CancellationToken cancellationToken = default);
Task<SourceText> TextDocument.GetTextAsync(CancellationToken cancellationToken = default);
ImmutableArray<Diagnostic> Compilation.GetDiagnostics(CancellationToken cancellationToken = default);
INamedTypeSymbol? Compilation.GetTypeByMetadataName(string fullyQualifiedMetadataName);
ImmutableArray<ISymbol> INamespaceOrTypeSymbol.GetMembers(string name);
```

Signatures identify members, not extension declarations to add. `IFieldSymbol`
HasConstantValue/ConstantValue read marker; semantic navigation uses
`Compilation.GetSemanticModel(SyntaxTree, bool)` and
`SemanticModel.GetSymbolInfo(SyntaxNode, CancellationToken)` then source locations.
GetSourceGeneratedDocumentsAsync forces generation; observe full returned collection
and generator/load diagnostics. Null compilation or absent required symbol is a
failed/not-run evidence outcome, never an empty success. Task-01 verified Open*
logger APIs are reused only by distinct ordinary sessions.

## Execution algorithm fixed before implementation

1. Create/restore fixture; restore packages/build prerequisites explicitly, outside
   measured DTB stage. Record toolset and all source/project bytes. Inspect profile
   via task-02; validate expected disposition/reasons. A rejected fixture ends with
   negative-admission outcome and no Capture/Hydrate/analyzer execution. Do not run
   ordinary-generated equivalence on a rejected generator as if admitted.
2. Stabilize SDK-generated inputs, run capture A ordinary session, collect full
   input manifest/evidence and SemanticSnapshot. Detect late inputs against
   task-02 expected sets. Fully dispose A. Encode then Decode value DTO; reject
   missing/new fields. Store DTO hash as evidence, no source/generated texts in it.
3. Revalidate every positive/negative/region input and toolset against current
   independent resolution. Hydrate B using only decoded DTO and verified evidence.
   Structural/invocation observer prohibits Open*/MSBuild/process on this path.
4. Open oracle C fresh MSBuild workspace through OpenOrdinaryAsync with same
   explicit request, roots/TFM/toolset/full-source mode. C reads disk itself and
   never receives B/A graph or texts. Recheck closure hashes after C; ordinary
   SDK side effects changing bytes invalidate attempt, not ignored obj changes.
5. Read B/C fully, compare graph/options/all memberships/references/characters,
   encoding, complete generated set/texts, diagnostics and required semantic
   navigation/constants. Also compare independently authored fixture expectations;
   common wrong behavior in both branches cannot satisfy those assertions.
6. Round-trip supported text edit under packet-01 adapter, assert exact expected
   persisted bytes and csproj unchanged. Restore fixture before next independent
   comparison and VerifyRestoredAsync. Reconciliation exercises additional/config
   after bytes change: fixture mutation owned by test, host only updates memory.
7. Emit per-fixture result, all counters and evidence hashes. Repeat with reversed
   hydrate project insertion order: stable context selection and linked membership
   results must not depend on order. Negative witness with changed DTO option,
   removed edge, missing generated output and stale same-path DLL must be caught.

Tests V01–V06 require each corresponding fixture, not document-count equality.
V03 rejection before hydrate is valid negative evidence if exact mapping cannot
be supported, but does not satisfy a positive multi-target result. V04 missing
Razor generated golden/output blocks positive assertion. V05 altered/missing obj
rejects. V06 updates both linked memberships, external import invalidates.
V23 limited E0 witness records distinct SessionId values and fresh workspace
construction, and distinguishes capture from hydrate/oracle; production RAM/disk
force/reset policy remains explicitly not-run until E1/E2 implementation.

Counter acceptance requires nonzero ordinary control, no hidden hydrate Open*,
complete logging coverage and structural proof defined in design. A synthetic
counter set to zero is not evidence. Do not share default snapshot/option factories
between expectation creation and actual observation where that masks a lost field.

Report includes positive/negative matrices, full differences, unavailable cases,
commands/counts and context normalization rules. Production paths remain read-only.
Final Release solution build/main suite is coordinator-owned; lifecycle selection
follows task-execution. This packet makes no claim those checks have run.

## Task-02.1 Stage A gate supplement — 2026-10-04

The [task-02.1 decisions](task-02.1-decisions.md) and
[investigative packet](implementation-packet-task-02.1.md) record newly audited
workload/provenance/closure blockers. Proposed normative amendments are not
accepted or applied. Existing declarations, fixture bytes, negative expectations,
host admission and task-04 stages 2–7 remain unchanged. No positive consumer edit
is dispatched. A replacement positive implementation packet and all affected
contracts must be accepted before consumer integration. This supplement records
no new acceptance and does not change the earlier task's accepted scope.

## Task-02.1 owner decisions accepted — 2026-10-04

The owner accepted A021-01/02/03 and D021-04/05; the earlier gate supplement above
records the pre-approval state. The [normative amendment](../spec.md#уточнение-профиля-и-provenance--решение-владельца-2026-10-04)
and [decisions](task-02.1-decisions.md) now govern the distinct workload extension,
SDK-evaluated-item provenance and strict source-backed package-free/exact-pack
closure. Genuine TaskOutput and SDK evaluated facts must remain distinguishable;
no fabricated task context or installed-code trust is authorized. Razor/Web stays
negative. Existing declarations remain unchanged until the executable task-02.1
packet defines and reviews exact truthful model/host/consumer adaptations. Positive
admission, new-session overlay and task-04 stages 2–7 are not proven by approval.

## Task-02.1 limited L021-1 implementation — 2026-10-04

Independent reviewer accepted the finite L021-1 packet for graph observations,
framework file selection and disk-only input rechecks. The runner now persists
those private evidence artifacts and partial independent graph edges. Consumer
signatures and portable semantic/admission schemas are unchanged. All unresolved
closure reasons and both false readiness flags remain; graph observations do not
prove actual reference resolution or exact binlog binding. Existing fixture
expectations remain unknown/false. Code review and final coordinator validation
are pending; full remediation and task-04 positive stages 2–7 are not accepted.
