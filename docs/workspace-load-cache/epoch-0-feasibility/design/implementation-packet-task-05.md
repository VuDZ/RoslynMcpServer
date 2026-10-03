# Implementation packet E0/task-05 — separate O7 metadata comparator

Producer task-00. Contract filled; execution **not-run**. Dependencies: accepted
task-04 and task-00; use their exact shared interfaces. Read design/spec/audit,
packets-01/03/04 and full code-style before edits. Metadata result is O7 evidence,
not full-source cache equivalence, cache go or change to public tools/profile.

## Allowlist / denylist

Existing files allowed: none. Exact new files under
`RoslynMcpServer.Tests/WorkspaceLoadCache/`: `MetadataComparator.cs`,
`MetadataComparatorTests.cs`, `Models/MetadataScenario.cs`,
`Models/MetadataComparison.cs`, `Models/MeasurementAuthorization.cs`,
`Models/MetadataAttempt.cs`.
Reports:
`docs/workspace-load-cache/epoch-0-feasibility/evidence/task-05-metadata.md` and
`docs/workspace-load-cache/epoch-0-feasibility/evidence/task-05-measurement-manifest.json`.
All other files denied, explicitly Services/Workspace/SolutionManager.cs,
Services/Analyzers/, Tools/, csproj/solution, shared host/oracle/fixtures,
cache-contract/spec and packets. Do not add public metadata mode or internal flag.

## Exact proposed declarations

Namespace `RoslynMcpServer.Tests.WorkspaceLoadCache`; data models in matching Models
files. One type/file, no hidden config source or dependency on public tool state.

```csharp
internal enum MetadataScenario
{
    FreshReference,
    StaleReference,
    MissingReference,
    CorruptReference
}

internal sealed record MeasurementAuthorization(
    string ApprovalEvidencePath,
    string WorkloadManifestPath,
    int AttemptsPerMode,
    double MedianMillisecondsBudget,
    double P95MillisecondsBudget,
    long PeakWorkingSetBytesBudget,
    DateTimeOffset ApprovedAt);

internal sealed record MetadataAttempt(
    int Ordinal,
    string Mode,
    ScenarioOutcome Outcome,
    double? OpenMilliseconds,
    double? FirstSemanticMilliseconds,
    long? PeakWorkingSetBytes,
    DtbMeasurement Dtb,
    string Reason);

internal sealed record MetadataComparison(
    MetadataScenario Scenario,
    ScenarioOutcome Outcome,
    string RequestedMode,
    string EffectiveMode,
    string Scope,
    string Readiness,
    string? FallbackReason,
    ImmutableArray<string> SourceProjectKeys,
    ImmutableArray<string> MetadataReferenceKeys,
    ImmutableArray<OracleDifference> SemanticDifferences,
    ImmutableArray<MetadataAttempt> Attempts,
    ImmutableArray<string> EvidencePaths);

internal sealed class MetadataComparator
{
    public Task<MetadataComparison> CompareAsync(
        OracleFixtureCase fixture,
        MetadataScenario scenario,
        string artifactDirectory,
        MeasurementAuthorization? measurement,
        CancellationToken cancellationToken);
}
```

Category is always metadata in report. RequestedMode = `metadata`; EffectiveMode
= `metadata|full-source|unavailable`. Scope = `root-source-with-metadata-references`
or `full-source` or `unavailable`; do not claim full source navigation for a DLL.
Readiness = `ready|failed|not-run`; semantic success requires requested useful call,
not successful Open*. Missing/stale/corrupt causes explicit comparator full-source
fallback before useful semantic execution; fallbackReason =
`reference-missing|reference-stale|reference-corrupt`. If full-source fallback
fails, effective/readiness unavailable/failed; outcome Failed. Report actual
MSBuild mode behavior separately in evidence because its built-in fallback may
differ; selected conservative comparator policy does not alter Roslyn behavior.

Model fields nullable only as shown. Ordinal >= 1, Mode = `full-source|metadata`,
counts per mode >= 10. Latency/resource numbers null for failed/not-run or
unapproved timing, not zero. MeasurementAuthorization with nonexistent/empty
approval, wrong workload or approval after first attempt throws ArgumentException.
Budgets finite positive; exact values are owner-approved, never defaults.
This experiment authorization does not resolve global U-ARB-03.

## Consumed signatures, errors, cancellation and ownership

Task-04 class IndependentWorkspaceOracle provides:

```csharp
public Task<ExperimentSession> OpenOrdinaryAsync(
    ExperimentRequest request, string artifactDirectory,
    CancellationToken cancellationToken);
```

Task-01 HydrateHostExperiment provides:

```csharp
public Task<ExperimentSession> OpenFreshAsync(
    ExperimentRequest request, string artifactDirectory,
    CancellationToken cancellationToken);
```

For full source pass `request with { MetadataMode = false }` to ordinary oracle.
For metadata pass `request with { MetadataMode = true }` to fresh runner;
runner sets public `MSBuildWorkspace.LoadMetadataForReferencedProjects` before
Open*. No hydrate entrypoint is used. This is not zero-DTB mode: log actual events
with the same DtbMeasurement contract. Task-04 observation call and shared types:

```csharp
public static Task<WorkspaceObservation> ReadAsync(
    ExperimentSession session, AdmissionEvidence admission,
    FixtureExpectation expectation, CancellationToken cancellationToken);
```

Full-source observation uses task-02 observed instance mapping through reader.
Metadata observation uses public Solution project/reference enumeration locally
in comparator and compares required root symbol/constant directly. Retain all
reference keys/coverage differences in MetadataComparison; do not project or
relabel full-source AdmissionEvidence as supported for a partial graph. These two
observation operations are fixed: full-source through reader, metadata through
local public graph/constant enumeration. No reader mutation or omitted coverage.

FixtureCase methods, including RestoreAsync, VerifyRestoredAsync and DisposeAsync,
and data expectations have packet-03 signatures/ownership; scalar constant access
uses packet-04 public Compilation APIs. Public MSBuild and counter calls have
packet-01 signatures, no new Roslyn runtime dependency. Comparator borrows fixture
exclusively; owns/disposes sessions/processes/loggers, never fixture. Each call
fully restores original fixture files in finally and reports restore failure;
only within-root outputs are modified. No production manager/session lock.
Cancellation propagates OCE with token after cleanup; I/O/semantic failure is
Failed with bounded reason/evidence, never an invented latency. Invalid
scenario/authorization/path inputs throw ArgumentException.

## Fixture protocol and mandatory assertions

Use ProjectGraph fixture from task-03 with App root and Lib reference. The comparator
owns only an exclusive temp copy. Build Release or Debug matching request, record
source/project/toolset fingerprint and output Lib.dll bytes/hash; build occurs
outside latency budget, is still recorded as setup. A local manifest proves this
fixture DLL freshness: source+options+reference inputs hash before build, exit 0,
output hash after build. mtime/size is insufficient. No arbitrary-workload freshness
detector is claimed. A reference of unknown freshness is unavailable/deferred,
not assumed fresh.

- FreshReference: unchanged input manifest/output hash; set metadata flag before
  root open. Record actual source projects and metadata references. Required
  semantic call resolves `Lib.Value.Number=42` from root compilation, and explicitly
  reports no Lib source navigation if Roslyn used metadata. If MSBuild still loads
  Lib source, report full-source effective mode and no metadata fast-open success.
- StaleReference: change Lib constant source to 43 without rebuild, preserve old DLL
  hash (optionally old size/mtime). Manifest mismatch causes `reference-stale`,
  full-source fallback returns 43. Record actual raw metadata behavior as separate
  observation; never pass old 42 as equivalent.
- MissingReference: delete only fixture Lib output DLL; classify missing before
  useful call, fallback full source yields 42 and reports fallback source scope.
- CorruptReference: replace fixture DLL bytes with non-PE literal; hash/PE validation
  rejects, fallback full source yields 42; no crash/no stale semantic result.

For every case compare root symbols/diagnostics/constant with independent fresh
full-source oracle and report coverage loss separately. Whole graph equality is
not required for metadata mode; do not suppress differences to manufacture it.
All changes restore fixture bytes afterward; cross-attempt source and output
states are recreated, not inherited from the preceding stale/corrupt case.

## Measurement authority and report

Default call `measurement: null` executes functional assertions only; Attempts is
empty and report timing `deferred: budget-not-approved`. Do not publish incidental
stopwatch values as performance. Before any timed call, persist measurement
manifest containing owner approval evidence/date, named workload/pin/size,
SDK/TFM/instances, restore/build/setup exclusion, semantic call, both modes,
alternating order, OS cache state, attempts >= 10, median/p95/peak-memory budgets.
Caller supplies approved values; task-05 cannot select them. For timing use fresh
process attempts where environment permits; otherwise mark process isolation
unavailable and defer the benchmark, not simulate a new process with new Workspace.
Raw attempt records keep stage counters, return codes, first semantic outcome and
scope. Median/p95 calculated per mode from successful attempts with sample counts;
failed attempts still listed and cannot be dropped to imply success.

No current numerical authority is present in task-00: O7 performance remains
deferred unless owner supplies one later. This does not block functional O7 work.
Coordinator owns final Release build/main suite and lifecycle selection per shared
rules. O7 success never flips cache activation, sdk-project-v1 or U-ARB-03.
