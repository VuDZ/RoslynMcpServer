# Implementation packet E0/task-01 — isolated hydrate host

Producer: task-00. Contract filled; execution **not-run**. Read [design](../design.md),
spec, current audit, task-execution and docs/code-style.md in full before C# edits.
Owner task-01; no shared production file is assigned. Namespace throughout:
`RoslynMcpServer.Tests.WorkspaceLoadCache`. `ImmutableArray<T>` must be non-default;
strings nonnull unless `?`; paths absolute; records hold immutable values.

## Allowlist and denylist

Existing files allowed: **none**. Exact new files below, relative to
`RoslynMcpServer.Tests/WorkspaceLoadCache/`:

- `HydrateHostExperiment.cs`, `ExperimentSession.cs`, `SemanticSnapshotCodec.cs`,
  `DtbEventCounter.cs`, `ExperimentAnalyzerAdmission.cs`, `HydrateHostExperimentTests.cs`.
- `Models/ExperimentRequest.cs`, `Models/InstanceKey.cs`, `Models/ProjectEdge.cs`,
  `Models/DocumentEntry.cs`, `Models/EncodingPolicy.cs`, `Models/ReferenceEntry.cs`,
  `Models/AnalyzerEntry.cs`, `Models/ProjectEntry.cs`, `Models/SemanticSnapshot.cs`,
  `Models/EvidenceEntry.cs`, `Models/RegionEntry.cs`, `Models/RegionMember.cs`,
  `Models/AdmissionEvidence.cs`, `Models/DtbMeasurement.cs`.
- Report `docs/workspace-load-cache/epoch-0-feasibility/evidence/task-01-hydrate-host.md`.

Private smoke fixture helpers stay nested in HydrateHostExperimentTests; generate
files under a unique temp root and remove only that verified root. No nested
fixture C# project in repo globs. All files outside allowlist denied, explicitly
Services/, Tools/, Diagnostics/, both csproj, solution, production config,
normative spec/contract and packets. Design owner alone updates a contract.

## Exact proposed data declarations

One declaration per file matching its name in Models; ordinary using namespaces
System.Collections.Immutable and Microsoft.CodeAnalysis are implied here.

```csharp
internal sealed record ExperimentRequest(
    string WorkspacePath,
    string? Configuration,
    string? Platform,
    string? TargetFramework,
    ImmutableArray<string> SelectedRoots,
    string Scope,
    bool MetadataMode);

internal sealed record InstanceKey(
    string ProjectPath,
    string InnerTargetFramework,
    ImmutableSortedDictionary<string, string> EffectiveProperties);

internal sealed record ProjectEdge(
    InstanceKey From,
    InstanceKey To,
    ImmutableArray<string> Aliases,
    bool EmbedInteropTypes);

internal sealed record EncodingPolicy(
    int? CodePage,
    ImmutableArray<byte> Preamble,
    string DecoderFallback,
    string? DecoderReplacement,
    string EncoderFallback,
    string? EncoderReplacement);

internal sealed record DocumentEntry(
    InstanceKey Project,
    string Role,
    string Path,
    string Name,
    ImmutableArray<string> Folders,
    SourceCodeKind SourceKind,
    bool GeneratedOnDisk,
    EncodingPolicy Encoding);

internal sealed record ReferenceEntry(
    string Path,
    MetadataImageKind Kind,
    ImmutableArray<string> Aliases,
    bool EmbedInteropTypes,
    string? DocumentationPath);

internal sealed record AnalyzerEntry(
    InstanceKey Consumer,
    string OriginalPath,
    string Sha256,
    string OriginDirectory,
    ImmutableArray<string> DependencyPaths);

internal sealed record ProjectEntry(
    InstanceKey Key,
    string Name,
    string AssemblyName,
    string Language,
    string? OutputPath,
    string? OutputReferencePath,
    ImmutableSortedDictionary<string, string?> ParseOptions,
    ImmutableSortedDictionary<string, string?> CompilationOptions,
    ImmutableArray<DocumentEntry> Documents,
    ImmutableArray<ReferenceEntry> MetadataReferences,
    ImmutableArray<AnalyzerEntry> Analyzers);

internal sealed record SemanticSnapshot(
    string Schema,
    ExperimentRequest Request,
    ImmutableArray<ProjectEntry> Projects,
    ImmutableArray<ProjectEdge> ProjectReferences);

internal sealed record EvidenceEntry(
    string Category,
    InstanceKey? Owner,
    string PathOrRegion,
    string? Sha256,
    bool IsAbsent,
    string Source,
    string SourceVersion,
    string RawEvidenceLocator);

internal sealed record AdmissionEvidence(
    string ProfileVersion,
    string Disposition,
    ImmutableArray<EvidenceEntry> Entries,
    ImmutableArray<RegionEntry> Regions,
    ImmutableArray<InstanceKey> ExpectedInstances,
    ImmutableArray<ProjectEdge> ExpectedEdges,
    ImmutableArray<string> ExpectedCompilerInputPaths,
    ImmutableArray<string> Reasons,
    bool AnalyzerInventoryComplete,
    bool OverlayReady);

internal sealed record RegionMember(string RelativePath, string Sha256);

internal sealed record RegionEntry(
    InstanceKey Owner,
    string Root,
    string Role,
    ImmutableArray<string> Includes,
    ImmutableArray<string> Excludes,
    ImmutableArray<RegionMember> Members,
    string ManifestSha256,
    string RawEvidenceLocator);

internal sealed record DtbMeasurement(
    int OpenInvocations,
    int ProjectStarts,
    int CoreCompileTargets,
    int? DesignTimeContexts,
    bool Complete,
    ImmutableArray<string> EvidencePaths);
```

Scope = `full|selected`; roots sorted under platform path comparer, duplicate
canonical roots rejected. Null property means absent; empty explicit value stays
distinct. Instance equality is value equality of path, TFM and sorted properties,
not record default equality of ImmutableDictionary object. No outer TFM, GUID or
temporary path key. Effective properties allowlist: Configuration/Platform/TFM and
DTB flags only; unknown significant property rejects representability.
Role = `compile|additional|analyzer-config`; membership key includes role, path,
instance and folders; duplicate exact key rejects. SHA-256 uppercase hex 64 chars;
absent implies hash null. Region hash, if present, is ordered membership/content
manifest hash and source explicitly identifies it. No implied evidence from null.
Admission disposition/profile rules are fixed in design §7. Dependency DLLs have
separate EvidenceEntry hashes; paths alone cannot authorize loading.

EvidenceEntry.Category has exactly these values: `project`, `import`, `compile`,
`additional`, `analyzer-config`, `metadata`, `analyzer`, `analyzer-dependency`,
`restore`, `toolset`, `sdk`, `known-absent`, `target-input`, `expected-graph`.
`known-absent` always IsAbsent=true; other file categories false. Expected-graph
points to an artifact describing roots/instances/edges, not to a source document.
Regions are separate structured values, never encoded implicitly in PathOrRegion.

Region contract version is fixed by ProfileVersion `sdk-project-v1/e0-1`.
Root is canonical absolute owner project directory. Role = `compile|additional|
razor|web-asset|negative`; Includes/Excludes are relative slash-separated glob
patterns, expanded from accepted XML/SDK evidence by task-02. Allowed syntax:
literal segment, `*` (zero or more non-slash chars), `?` (one non-slash char),
and whole segment `**` (zero or more complete segments). No properties, item
expressions, conditions, brackets or escaping remain. Dot segments, rooted paths,
`..`, and a pattern escaping Root reject as unsupported; unresolved expression
gives unknown. Matching uses ordinal-ignore-case on Windows, ordinal elsewhere;
glob segment matching includes dotfiles. `**/*.cs` includes root-level .cs files.

Region membership = union includes minus union excludes. Standard compile region
has `**/*.cs`, excludes `bin/**`, `obj/**` plus explicit Remove patterns and
observed SDK default excludes; task-02 must supply the exact evaluated supported
list. Explicit obj compiler/config paths remain positive EvidenceEntry independent
of region excludes. Razor regions `**/*.razor`, `**/*.cshtml` have exact SDK
excludes; web region `wwwroot/**` includes `.well-known` and has no implicit
dotfile filter. Empty regions are present with empty Members, never omitted.
Nested symlink/junction/reparse point, unreadable directory/file or enumeration
race rejects the whole probe as unknown. No follow of links or partial manifest.

Enumerate once per root with a hard fixture limit of 100000 entries and 64 levels;
hash matching bytes under 256 MiB aggregate, sequential I/O, check token at each
entry/read. Exceeding limit => unknown/no hydrate, not truncated success. These
limits bound experiments only. Store member paths relative with `/`, actual case
preserved; reject case-collision under platform comparer. Sort first by platform
comparer then ordinal tiebreaker. Manifest encoding is UTF-8 without BOM: prefix
`e0-region-1\n`, then each entry as JSON array `[relativePath,uppercaseSha256]`
using System.Text.Json default escaping, no indentation, followed by LF. Hash
exact bytes SHA-256; empty manifest hashes prefix alone. Includes/Excludes/Root
are compared separately; their semantics are not inferred from manifest hash.
Task-01 owns matcher/manifest verification as private methods in host; task-02
produces this representation. Hydrate enumerates and compares exact Members plus
hash before any host/analyzer materialization. This is a finite experiment
contract, not a production cache-store schema or a new profile.

Option dictionaries are a **closed value schema**, not reflection bags. Values
are canonical JSON scalar/array/object strings with invariant enum names, numeric
decimal values, ordinally sorted diagnostic/feature maps; null remains null.
Parse keys: `SpecifiedLanguageVersion`, `LanguageVersion`, `SpecifiedKind`, `Kind`,
`DocumentationMode`, `PreprocessorSymbolNames`, `Features`. Restore specified values;
check effective values after reconstruction. Compilation keys: `OutputKind`,
`ModuleName`, `MainTypeName`, `ScriptClassName`, `Usings`, `OptimizationLevel`,
`CheckOverflow`, `AllowUnsafe`, `CryptoKeyContainer`, `CryptoKeyFile`,
`CryptoPublicKey`, `DelaySign`, `PublicSign`, `Platform`, `GeneralDiagnosticOption`,
`WarningLevel`, `SpecificDiagnosticOptions`, `ConcurrentBuild`, `Deterministic`,
`MetadataImportOptions`, `NullableContextOptions`, `ReportSuppressedDiagnostics`.
CryptoPublicKey bytes encode base64; symbol/usings order preserved.

The five service keys `MetadataReferenceResolver`, `XmlReferenceResolver`,
`SourceReferenceResolver`, `StrongNameProvider`, `AssemblyIdentityComparer` use
closed descriptors: null; public default identity comparer; public
DesktopAssemblyIdentityComparer.Default; XmlFileResolver(baseDirectory);
SourceFileResolver(searchPaths, baseDirectory, pathMap); DesktopStrongNameProvider
(keyFileSearchPaths); metadata resolver null only. Public service values must be
readable and reconstructible without reflection/private APIs; any other instance
rejects with `unsupported-options`. If an actual MSBuild option cannot be read via
public API, report failed/not-run for positive fixture and return that concrete
finding to design owner; do not silently widen descriptors or coerce null/default.
`SyntaxTreeOptionsProvider` is workspace-owned: regenerate from config documents;
compare observable per-tree diagnostics/generated status in task-04. Language is
`C#`; Errors derived and must be empty. Internal fields seen in XML docs are not
declared public APIs. New public semantic member outside this schema requires
design review, not automatic reflection serialization.

Encoding CodePage=null preserves unspecified encoding; no fabricated UTF-8
metadata. Preamble comes from actual writer encoding and source bytes;
fallback = `exception|replacement|unspecified` (last only for null encoding).
Null encoding write policy: reject write as `unsupported-encoding`, preserving
memory reads; do not guess. Support UTF-8 BOM/no BOM and UTF-16 LE BOM fixtures;
other public Encoding policy may pass only if exact reconstruction and byte
round-trip succeed. Custom fallback rejects.

## Proposed entrypoints and consumed contracts

```csharp
internal sealed class HydrateHostExperiment
{
    public HydrateHostExperiment(ExperimentAnalyzerAdmission? analyzerAdmission = null);
    public Task<ExperimentSession> OpenFreshAsync(
        ExperimentRequest request,
        string artifactDirectory,
        CancellationToken cancellationToken);

    public Task<SemanticSnapshot> CaptureAsync(
        ExperimentSession ordinary,
        AdmissionEvidence admission,
        CancellationToken cancellationToken);

    public Task<ExperimentSession> HydrateAsync(
        SemanticSnapshot snapshot,
        AdmissionEvidence admission,
        string artifactDirectory,
        CancellationToken cancellationToken);

    public Task<WorkspaceWriteResult> ApplyAsync(
        ExperimentSession session,
        Solution heldBase,
        Solution candidate,
        WorkspaceWriteOperationContext? operationContext,
        CancellationToken cancellationToken);

    public Task ReconcileAsync(
        ExperimentSession session,
        ImmutableArray<string> changedPaths,
        CancellationToken cancellationToken);
}

internal sealed class ExperimentSession : IDisposable
{
    public Guid SessionId { get; }
    public ExperimentRequest Request { get; }
    public Solution Solution { get; }
    public DtbMeasurement Dtb { get; }
    public ImmutableArray<WorkspaceDiagnostic> LoadDiagnostics { get; }
    public WorkspaceWriteOperationContext WriteContext { get; }
    public bool AnalyzerAdmissionReady { get; internal set; }
    internal Microsoft.CodeAnalysis.Workspace Workspace { get; }
    internal SemaphoreSlim OperationLock { get; }
    internal ExperimentSession(
        Microsoft.CodeAnalysis.Workspace workspace,
        ExperimentRequest request,
        DtbEventCounter counter,
        ImmutableArray<WorkspaceDiagnostic> loadDiagnostics);
    public void Dispose();
    internal void Publish(Solution solution);
}

internal delegate Task<AnalyzerProvenanceSnapshot?> ExperimentAnalyzerAdmission(
    SemanticSnapshot snapshot,
    AdmissionEvidence evidence,
    Guid newSessionId,
    Solution baseSolution,
    CancellationToken cancellationToken);

internal static class SemanticSnapshotCodec
{
    public static byte[] Encode(SemanticSnapshot snapshot);
    public static SemanticSnapshot Decode(ReadOnlySpan<byte> payload);
}

internal sealed class DtbEventCounter : Microsoft.Build.Framework.ILogger
{
    public Microsoft.Build.Framework.LoggerVerbosity Verbosity { get; set; }
    public string? Parameters { get; set; }
    public void Initialize(Microsoft.Build.Framework.IEventSource eventSource);
    public void Shutdown();
    public void RecordOpenInvocation();
    public DtbMeasurement Snapshot();
}
```

The counter must tee raw BinaryLogger through its Initialize/Shutdown subscription
or an internally nested composite logger; constructor defaults Diagnostic verbosity
and no raw path until Parameters explicitly supplied. Complete=false before attach
and after logging failure; a hydrate-owned closed path records complete zero only
under the no-Open/no-process structural witness in design. Per-stage counter is
owned by a session, never static; callbacks synchronize internal collection access.

Codec is bounded experiment JSON UTF-8: schema exact `e0-semantic-1`, max 16 MiB,
4096 projects, 1 million memberships, max path/string 32768 chars, max JSON depth
64. These are fixture resource bounds, not production budgets. Unknown schema,
missing/extra keys, invalid enum/path/duplicate key or exceeded bound throws
InvalidDataException. Encode/Decode round-trip precedes hydrate; no source/generated
texts or Roslyn objects survive it. Byte arrays returned are caller-owned.

OpenFreshAsync creates MSBuildWorkspace with MetadataMode property, optional
properties only when present, captures every WorkspaceFailed diagnostic and owns
the logger. Raw logs are isolated evidence, not cache payload. Capture binds
Roslyn IDs to task-02 exact instance inventory; ambiguity rejects. Hydrate accepts
only supported evidence, validates disk input hashes/absence/membership and
builds a base with **zero executable analyzer references first**. It builds AdhocWorkspace with
ProjectInfo/DocumentInfo and newly created IDs, no Open*, process, evaluation or
design-time build. Eagerly read source/additional/config bytes and metadata images
before returning; retain owned metadata/analyzer resources to session Dispose.
Generator execution may occur at semantic call; all its admitted dependencies
remain protected by immutable fixture ownership and post-call hash validation.
E0 does not simulate a production reader lease.

Analyzer admission is a separate ordered step. Before testing whether the DTO
has any analyzers, require AnalyzerInventoryComplete=true and compare its complete
analyzer inventory against independently captured evidence for every expected
instance. The exact analyzer key is (Consumer, OriginalPath); compare both sets
and their SHA-256 values against Entries with Category=analyzer and matching Owner.
For each consumer, the union of DependencyPaths must exactly match that consumer's
Category=analyzer-dependency entries; all dependency hashes must be present and
rechecked. Extra, removed, duplicated or unbound entries reject HydrateAsync with
InvalidDataException before host materialization or any DLL load. No subset match.

AnalyzerInventoryComplete is set by task-02 only after independently observing
compiler analyzer inputs for every ExpectedInstance, including an explicit empty
list for each instance without analyzers, and establishing dependency closure.
It is never inferred from DTO emptiness, absence of TaskOutput events, OverlayReady,
or successful ordinary open. Missing inventory coverage means unknown admission
and AnalyzerInventoryComplete=false. Fixture-only synthetic evidence in task-01
must explicitly supply the same inventory; it is not observed production evidence.
Only when these checks prove that both inventories are empty may
AnalyzerAdmissionReady=true without loading DLL. Otherwise OverlayReady=false or
missing injected delegate returns the base session with AnalyzerAdmissionReady=false
and no analyzer references. With OverlayReady=true call injected
ExperimentAnalyzerAdmission exactly once with new SessionId and new base Solution;
task-02 implements it through RevalidateOverlayAsync declared in design §7.
Null, wrong-session or incomplete result is rejected by existing
`AnalyzerProvenanceCaptureGate.TryGetUnsuitableReason(snapshot, sessionId)`;
returned base has no executable refs, no shadow overlay. Only a newly reconstructed
Complete snapshot after independent SDK/pack/DLL/dependency recheck and exact
TaskOutput-to-compiler mapping permits memory AnalyzerFileReference additions.
Same-path hash match alone, explicit Analyzer outside TaskOutput, banned/unavailable
evidence or failed revalidation can never load a DLL from DTO.

Use a private nested experiment IAnalyzerAssemblyLoader backed by an owned
collectible AssemblyLoadContext: only exact admitted original DLL/dependency
paths, no directory glob/simple-name first match; share framework/Roslyn assemblies
by verified runtime identity, reject ambiguous/private dependency outside admitted
closure. AddDependencyLocation may register only verified allowlist paths.
Session Dispose releases/unloads owned context; it does not claim immediate CLR
unload. Do not repurpose production InProcessAnalyzerAssemblyLoader (it owns
process-lifetime shadow assemblies). Gate success here proves only the E0
portable-evidence seam, not production shadow preparation/publication integration.
Required generated output with AnalyzerAdmissionReady=false is failed/not-run,
never positive equivalence. Report original DLL admissions and new session ID;
no shadow paths or session IDs enter SemanticSnapshotCodec.

Session alone owns workspace/resources; caller disposes it exactly once, including
on later compare failure. Factory disposes unsuccessful candidates itself. Returned
Solution is immutable borrowed snapshot valid for use while session alive.
One async operation at a time per session, enforced by owned SemaphoreSlim;
methods never acquire production manager lock or recursively reacquire session
lock. Internal Publish requires caller-held OperationLock, checks Solution.Workspace
matches owned Workspace, updates published Solution/raw revision and WriteContext
atomically for that session; it never writes disk. Disposed session throws ObjectDisposedException. Read factories propagate
OCE with supplied token; malformed input/unsupported representation throws
InvalidDataException; runner report labels reason failed/not-run, never success.

ApplyAsync builds current freshness from session-owned revision/context and calls
existing preflight before any file write; no inferred context for null. Verify all
changes are supported `.cs` paths and SDK glob membership before first byte. Under
same session acquisition persist changed texts, add/remove/rename, then apply
memory/reconcile all successful paths. Partial/cancel result uses existing
WorkspaceWriteResult and lists only successful writes/deletes. Cancellation before
first I/O returns Cancelled/no paths; after a committed write returns Cancelled with
committed paths and unapplied state if needed. I/O failure after a write returns
PartialPersistence. No multi-file rollback. ReconcileAsync reads bytes once per
physical path and updates every role membership, no writer/DTB/csproj changes;
failure leaves old published snapshot and propagates, never clears pending work.

Existing APIs (read-only; exact source declarations in current files):

```csharp
// Services/Workspace/WorkspaceFilePersistence.cs
public static Task WriteTextAsync(
    WorkspaceInputSession? session, string fullPath, string text,
    Encoding encoding, CancellationToken cancellationToken);
// Declared public on the internal WorkspaceFilePersistence class; async implementation.

// Services/Workspace/WorkspaceWriteBoundary.cs; public on internal class.
public static WorkspaceWritePreflight Preflight(
    Solution candidate, Solution workspaceCurrent,
    WorkspaceWriteOperationContext? operationContext,
    WorkspaceWriteFreshnessState current,
    Microsoft.CodeAnalysis.Diagnostics.IAnalyzerAssemblyLoader loader,
    Solution? heldBase = null);

// Services/Workspace/WorkspaceWriteResult.cs; public on internal context class.
public static WorkspaceWriteOperationContext Verified(
    Guid sessionId, string? loadedPath, AnalyzerShadowMapping? mapping,
    Solution? baseSnapshot, Solution? rawWorkspaceSnapshot,
    long rawWorkspaceRevision, bool shadowCopyEnabled,
    SemanticPublicationAdmission? publicationAdmission = null,
    IReadOnlyList<ExcludedAnalyzerReference>? excludedReferences = null);

// Services/Analyzers/AnalyzerProvenanceCaptureGate.cs; public on internal class.
public static string? TryGetUnsuitableReason(
    AnalyzerProvenanceSnapshot? snapshot, Guid sessionId);

// Services/Workspace/WorkspaceWriteResult.cs; existing value constructors.
internal readonly record struct WorkspaceWriteFreshnessState(
    Guid SessionId,
    string? LoadedPath,
    AnalyzerShadowMapping? Mapping,
    bool ShadowCopyEnabled,
    long RawWorkspaceRevision,
    Solution? RawWorkspaceSnapshot,
    Solution? PublishedSnapshot = null,
    SemanticPublicationAdmission PublicationAdmission = SemanticPublicationAdmission.NoOverlay,
    IReadOnlyList<ExcludedAnalyzerReference>? ExcludedReferences = null);

internal readonly record struct WorkspaceWritePreflight(
    bool Accepted,
    string? Reason,
    Solution? CleanedCandidate);
```

Existing WorkspaceWriteResult is a public class in the same file, with implicit
public parameterless constructor and these consumed init properties:
`WorkspaceWriteStatus Status`, `string? Reason`, `IReadOnlyList<string> SavedPaths`
(default empty), `bool WorkspaceApplied`, `bool OverlayPublished`,
`bool UnappliedProjectState`; all booleans default false. Existing status values:
FullSuccess, PreflightRejected, PartialPersistence, ReconciliationSucceeded,
ReconciliationFailed, Cancelled, Skipped. Static factories consumed if useful:
`public static WorkspaceWriteResult PreflightRejected(string reason)` and
`public static WorkspaceWriteResult Skipped(string reason)`. Caller owns value;
SavedPaths is a frozen copy of successfully written/deleted canonical paths.
Preflight has no cancellation/I/O; call under session acquisition; Accepted=true
requires nonnull CleanedCandidate, refused result requires no persistence.
No construction or access to a production manager context is authorized here.

For isolated smoke session pass null input session,
explicit no-overlay verified context and owned analyzer loader. That isolates
writer byte behavior; it does **not** claim production watcher integration.

Public Roslyn 5.9.0 consumed calls, verified in installed net10.0 package XML:
`MSBuildWorkspace.Create(IDictionary<string,string>)`, property
`LoadMetadataForReferencedProjects`, `OpenSolutionAsync(string, ILogger,
IProgress<ProjectLoadProgress>?, CancellationToken)` → Task<Solution> and
`OpenProjectAsync` with same arguments → Task<Project>; named logger parameter
`msbuildLogger`. AdhocWorkspace(HostServices, string) and AddSolution(SolutionInfo),
AddProject(ProjectInfo), AddDocument(DocumentInfo), Workspace.TryApplyChanges(Solution)
→ bool. Roslyn Create/With APIs may use nullable optional inputs; pass explicit
validated values. Cancellation belongs to Open*/GetText/compilation methods;
Create/Add/TryApplyChanges are synchronous and require caller token check before
entry. No AddSolution on MSBuildWorkspace is assumed.

## Fixtures and acceptance

V01/V02/V07 smoke fixture creates two SDK projects with reference under temp root;
precomputed explicit DTO + evidence is fixture-only and labelled synthetic,
not observed sdk-project-v1 completeness. Capture path integrated later with
task-02. Check read semantic, text edit, SDK-glob add/remove/rename, linked source
all memberships, preflight null/stale session/stale base/unknown analyzer rejection,
no bytes on refusal, partial second-file failure, cancel before/after first write,
ordinary build after each successful change, csproj bytes unchanged throughout.
Explicit Compile/disabled default items refuses before write. UTF-8 BOM/no BOM,
non-ASCII and UTF-16 LE preserve expected bytes. Generated/additional/config
reconciliation does not persist through TryApplyChanges. Counter negative witness
must observe ordinary open; hydrate witness observes no Open/process launch.
Negative inventory witnesses remove every AnalyzerEntry, remove one dependency,
add an unobserved analyzer, or set AnalyzerInventoryComplete=false while retaining
the independent evidence. Each must reject before host/semantic/DLL execution;
an empty DTO must never manufacture an analyzer-free positive case.

Report capability result independently from production lifecycle. Coordinator
owns final Release build/full main suite, plus lifecycle when required by shared
rules. Existing focused write/encoding/shared-source/non-CSharp tests remain
regressions; absence of a test run is not pass.
