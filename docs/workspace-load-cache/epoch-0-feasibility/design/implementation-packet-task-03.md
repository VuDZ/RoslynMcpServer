# Implementation packet E0/task-03 — oracle fixtures

Producer task-00; task-02 owns the Findings supplement below. Contract filled;
**dispatch blocked until accepted task-01/task-02 findings**, not until U-ARB-03.
Read spec, [design](../design.md), accepted packets-01/04, audit and full code-style.
Only fixture implementation is assigned; no admission/oracle design decisions.

## Boundaries

Existing files allowed: none. New files, all under
`RoslynMcpServer.Tests/WorkspaceLoadCache/`:
`OracleFixtureBuilder.cs`, `OracleFixtureCase.cs`, `OracleFixtureBuilderTests.cs`,
`Models/FixtureScenario.cs`, `Models/FixtureExpectation.cs`.
Report: `docs/workspace-load-cache/epoch-0-feasibility/evidence/task-03-fixtures.md`.
Fixture bytes live in builder literals and are materialized only under owned temp
root; additional golden output strings supplied by task-02 live in expectation
literals in builder. No standalone .cs files are compiled accidentally by the
parent test project. Deny Services/, Tools/, Diagnostics/, all csproj/solution,
other tests, other packets, spec/contract and every file outside this allowlist.
Task-03 cannot edit packet Findings; task-02/design owner integrates it first.

## Exact proposed declarations

Namespace `RoslynMcpServer.Tests.WorkspaceLoadCache`; one independent type per file.

```csharp
internal enum FixtureScenario
{
    ProjectGraph,
    Encodings,
    MultiTarget,
    RazorGenerator,
    GeneratedObj,
    LinkedImport,
    AbsentImport,
    CustomTarget,
    ExplicitCompile,
    WebAssets,
    ExternalAsset,
    MissingReference,
    EmptyProject
}

internal sealed record FixtureExpectation(
    string AdmissionDisposition,
    ImmutableArray<string> RequiredReasonCategories,
    ImmutableArray<string> ExpectedInstanceKeys,
    ImmutableArray<string> ExpectedEdgeKeys,
    ImmutableSortedDictionary<string, string> RequiredOptionValues,
    ImmutableSortedDictionary<string, string> ExpectedSourceCharacters,
    ImmutableSortedDictionary<string, ImmutableArray<byte>> ExpectedSourceBytes,
    ImmutableSortedDictionary<string, string> ExpectedGeneratedTexts,
    ImmutableSortedDictionary<string, string> ExpectedConstants,
    ImmutableArray<string> RequiredDiagnosticIds);

internal static class OracleFixtureBuilder
{
    public static Task<OracleFixtureCase> CreateAsync(
        FixtureScenario scenario,
        string parentDirectory,
        CancellationToken cancellationToken);
}

internal sealed class OracleFixtureCase : IAsyncDisposable
{
    public FixtureScenario Scenario { get; }
    public string Root { get; }
    public ExperimentRequest Request { get; }
    public FixtureExpectation Expected { get; }
    internal OracleFixtureCase(
        FixtureScenario scenario,
        string root,
        ExperimentRequest request,
        FixtureExpectation expected,
        ImmutableSortedDictionary<string, ImmutableArray<byte>> originalFiles);
    public string FilePath(string relativePath);
    public Task RestoreAsync(CancellationToken cancellationToken);
    public Task VerifyRestoredAsync(CancellationToken cancellationToken);
    public ValueTask DisposeAsync();
}
```

OriginalFiles stores exact bytes for source/project/config/additional data, not
arbitrary machine files. FilePath rejects rooted paths or escaping normalized
paths with ArgumentException; path must remain under Root. CreateAsync creates a
unique child and owns cleanup on failure; OCE propagates token. No build/restore,
MSBuild registration, DLL loading or oracle runs in builder. Caller owns fixture
until async disposal, and permits at most one mutation/build/oracle per fixture.
RestoreAsync restores all original bytes, removes scenario-created files under
Root, and removes owned bin/obj so restored bytes are not confused with old
generated output; all deletion targets checked within Root. Never remove parent
directory. VerifyRestoredAsync compares exact original bytes and unexpected
source/project/config/additional paths; generated output ignored only after
explicitly documented build stage. I/O and restore failure throw; do not suppress
cleanup failure or claim repeatability. No manager/session lock acquired.

All dictionary values nonnull, arrays nondefault. Expected keys use relative
`project.csproj|TFM`, edges `from->to`, option key `instance|property`, membership
key `instance|role|relativePath`. Generated key `instance|generatorIdentity|hintName`;
complete expected texts from independently observed task-02 evidence, never
captured from tested hydrate on the fly. Source bytes use literal encodings;
golden values cannot call the codec under test. Expected constant key is
`instance|fully.qualified.Type.Member`. Reasons and diagnostics are sorted;
full diagnostics comparison belongs to task-04, not a fragile golden localized log.

## Finite fixture manifest and expected assertions

Every root has `global.json` SDK 10.0.300, rollForward disable, and `NuGet.config`
clearing external feeds. Packages needed for standard framework/SDK fixtures must
already resolve locally; unavailable prerequisite is not-run, not network fallback.
Projects default `net10.0`, Debug, nullable enabled and C# 14 unless explicitly set.
New generated output paths may be SDK-defined in obj; inventory comes from task-02.

1. **ProjectGraph (V01):** `Lib/Lib.csproj`, `Lib/Value.cs`, `App/App.csproj`,
   `App/Use.cs`. App references Lib, `Lib.Value.Number=42`, App reads it. Exactly
   two inner instances/one edge, navigation resolves Lib source and constant 42.
   Expected supported after dependency evidence; no generated source golden needed.
2. **Encodings (V02):** `App/App.csproj`, `App/Utf8Bom.cs`, `App/Utf8.cs`,
   `App/Utf16.cs`, `App/.editorconfig`. Text contains `Привет Ω`; source bytes
   literal UTF-8 BOM EF BB BF / BOM-free UTF-8 / UTF-16 LE FF FE. Nullable enable,
   LangVersion 14, define E0_ENCODING; `.editorconfig` raises CS0219 to error on
   an unused local. Edit only string value, assert exact prefix/preamble/characters
   and byte round-trip. This error is expected compilation diagnostic, not load
   completeness failure; fixture build failure must be labelled expected fixture
   diagnostic, never host build pass.
3. **MultiTarget (V03):** `App/App.csproj`, `App/Flavor.cs`; TargetFrameworks
   `net9.0;net10.0`, conditions define E0_NINE/E0_TEN respectively. Same physical
   source returns constant 9 or 10, exact two instance keys and symbols. Missing
   net9 reference pack → not-run with prerequisite, never collapse to net10.
4. **RazorGenerator (V04):** `App/App.csproj` Sdk Razor, framework reference
   Microsoft.AspNetCore.App, `App/Marker.razor` with `@code { public const string
   Marker = "E0RazorMarker"; }`, `App/Use.cs`, `App/.editorconfig`. `.razor` is SDK
   AdditionalFile. Assert complete generator set/texts, generated Marker constant
   and editorconfig diagnostic effect. DLL paths/hash/provenance and complete
   golden texts require task-02 Findings; no injected in-memory generator is
   substituted for this supported SDK case. Explicit non-TaskOutput analyzer is
   a separate negative task-02 case, not equivalent to this fixture.
5. **GeneratedObj (V05):** `App/App.csproj`, `App/Value.cs`. SDK's generated
   assembly-info and generated MSBuild editorconfig in obj are explicit observed
   inputs; assert actual paths from Findings. Post-capture delete/change each →
   admission miss before hydrate. No custom target added to manufacture support.
6. **LinkedImport (V06):** `A/A.csproj`, `B/B.csproj`, `shared/Shared.cs`,
   `common.props`; A references B and both explicitly Link `../shared/Shared.cs`.
   common.props has property/items only, no Target. One physical file, two exact
   memberships/folders and both semantic contexts; external import hash changed
   → refusal. No external wildcard. Supported only when bounded evidence complete;
   task-02 records any profile limit as negative admission, not partial equality.
7. **AbsentImport (V12):** `App/App.csproj`, `App/Value.cs`; conditional Import
   `Local.props` with Exists, file initially absent. Expected known-absent evidence;
   mutation creates `App/Local.props` changing define → refusal before hydrate.
8. **CustomTarget:** `App/App.csproj`, `App/Value.cs`, `App/target-input.txt`;
   project Target before CoreCompile. Expected unknown custom-target category,
   no hydrate/no DLL load. Its execution outcome is distinct from admission.
9. **ExplicitCompile (V07):** `App/App.csproj`, `App/Only.cs`, default Compile
   items disabled and explicit Include. Writer add/remove/rename unsupported
   before bytes; this is a capability test, not a new broad admission rule.
10. **WebAssets:** `App/App.csproj` Web, `App/Program.cs`,
    `App/wwwroot/index.txt`, `App/wwwroot/.well-known/e0.txt`. Supported candidate
    requires task-02 complete regions; changing either asset invalidates next load.
11. **ExternalAsset:** `App/App.csproj` Web, `App/Program.cs`,
    `external/asset.txt` referenced as external static asset. Expected unknown
    uncovered asset outside project/SDK, entire request rejected.
12. **MissingReference:** `App/App.csproj`, `App/Use.cs`, absent
    `Missing/Missing.csproj`. Missing root/reference or soft warning cannot give
    completeness; expected unknown, no hydrate.
13. **EmptyProject:** `App/App.csproj` with default items enabled, generated
    assembly info and target framework attributes disabled. No user source is
    legal; expected zero user compile inputs, explicitly admitted empty project
    only if task-02 evidence confirms all generated/input roles. Zero user files
    is not zero-project success.

Global.json and NuGet.config are part of every originalFiles inventory. Exact
SDK-generated file paths/TFM mappings and full Razor text are evidence outputs,
not architecture left to fixture author. Mandatory findings below make dispatch
conditional while keeping names/interfaces fixed.

## Consumed contracts

`ExperimentRequest`, `InstanceKey`, `ProjectEdge`, `AdmissionEvidence`,
`HydrateHostExperiment.OpenFreshAsync/HydrateAsync` and `ExperimentSession.Dispose`
are declared in packet-01; all method arguments/nullability/cancellation/ownership
there are incorporated verbatim. Fixture builder calls none of them except
constructing ExperimentRequest. Validation test may call those runner methods
after accepted task-02 evidence; no production manager method needed.

Future oracle consumer exact declaration (owned by task-04, not task-03):

```csharp
public Task<OracleComparison> CompareAsync(
    OracleFixtureCase fixture,
    string artifactDirectory,
    CancellationToken cancellationToken);
```

Member belongs to `IndependentWorkspaceOracle` in packet-04. It borrows fixture
exclusively for the call, owns/disposes sessions and propagates cancellation;
does not transfer fixture ownership. Task-03 does not depend on its implementation
for compiling its own tests (task-04 follows task-03).

## Findings supplement — task-02 required before dispatch

Observed and accepted for isolated research 2026-10-03 by task-02;
independent reviewer `/root/independent_review`: **ACCEPT**. See the
[acceptance verdict](../evidence/task-02-dependency-admission.md#независимая-приёмка).
**Positive task-03 fixture dispatch remains on hold.** Reproducible raw experiments
now exist. Exact commands, runtime paths, per-fixture dispositions, complete
Razor output locators, SHA-256 inventories, mutation restoration and validation:
[task-02 evidence](../evidence/task-02-dependency-admission.md). This supplement
records observations only; the finite fixture contracts above remain unchanged.

- Runtime SDK 10.0.300 / MSBuild 18.6.3 / Roslyn 5.9.0, exact global.json pin.
  Installed NETCore reference packs 10.0.5/10.0.8; net9 unavailable. MultiTarget
  positive exact mapping/defines and independent graph-edge comparison **not-run**.
  Linked shared source has two observed compiler memberships; this is not full
  fresh-vs-hydrate equivalence.
- Standard GeneratedObj paths observed: `obj/Debug/net10.0/App.AssemblyInfo.cs`,
  `.NETCoreApp,Version=v10.0.AssemblyAttributes.cs`,
  `App.GeneratedMSBuildEditorConfig.editorconfig`. Owned generated/import inputs,
  absent Local.props and empty additional glob passed actual mutation/restoration
  subchecks. Full BOM/UTF-16/encoding oracle matrix **not-run**.
- Even standard fixture imports a Target-bearing workload manifest outside
  `sdk/10.0.300`; literal profile remains unknown. Standard Csc has 8 analyzer DLLs
  versus 6 TaskOutput DLLs (SDK NetAnalyzers pair missing). Explicit analyzer is
  also absent from TaskOutput. Restore/toolset/transitive analyzer closure remains
  unproved; **no supported request or fresh Complete session/gate proof**.
- Exact Razor fixture has **CS0542**: generated class Marker and member Marker
  conflict. Three ordinary builds fail as expected; full generated text and
  `E0Razor.Marker.Marker = "E0RazorMarker"` are observed by fresh MSBuildWorkspace.
  Report links complete baseline/scoped/restored generated dictionaries and DLL
  hashes. These are failing-fixture observations, not accepted positive goldens.
- **Scoped CSS counterexample:** adding Marker.razor.css leaves selected regions
  and all preexisting input hashes unchanged before build/open, but fresh generated
  Razor text gains scope attribute `b-6xpslpw42p`. Removing CSS restores complete
  original generated text. Resolve profile coverage explicitly; task-03 must not
  silently add CSS globs or rename the invalid fixture.
- Web inventory includes `.well-known`; separate Web mutation **not-run**.
  ExternalAsset is linked Content outside project/SDK, not a real NuGet package:
  the required NuGet static-asset experiment **not-run**. Corrupt/ambiguous portable
  provenance and new-session gate integration likewise **not-run**.

Focused checks: matrix 14/14 and Razor negative witness 1/1 passed. Coordinator
final Release solution build passed; main `Category!=AnalyzerLifecycle` 1132/1132
passed on final code. Research acceptance does not remove the positive dispatch
holds; task-03 cannot invent missing goldens or substitute easier positive cases.

Validation: builder tests exercise root/path confinement, exact bytes and restore
after every mutation, plus independent fresh marker check. Coordinator performs
final Release build/main suite per task-execution; fixture skip is reported, never pass.
