# Epoch 1 — Diagnosis and external MVP repro

Status: **shipped as v1.3.3** (`load_workspace` `logProjectOutputDiagnostics`).

## Baseline / problem

Colleagues reported (in the context of a large, real, multi-project solution)
that an in-solution Roslyn analyzer/generator project referenced via

```xml
<ProjectReference Include="..\Generator\Generator.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

could stop producing generated source for the referencing project once the
repo introduced a shared `Directory.Build.props` that overrides `OutputPath`
(routing build output into a shared `artifacts\...` tree instead of the SDK
default `bin\{Configuration}\{TFM}\`). Two suspected, related failure modes:

1. **Wrong path** — MSBuildWorkspace's design-time-resolved
   `AnalyzerReference.FullPath` for that `ProjectReference` could disagree
   with the referenced project's own resolved output location, silently
   disabling source generation for the referencing project even though a real
   `dotnet build` succeeds (MSBuild's own build-time evaluation is not
   necessarily wrong — only the design-time evaluation MSBuildWorkspace uses).
2. **Locking** — even if the path were correct, loading the analyzer assembly
   directly from its real build output would keep that file locked for the
   life of the MCP process, breaking a later `dotnet build` of the
   analyzer/generator project (`MSB3027`).

Before committing to any fix, the decision was to **not** guess against the
real (large, multi-service) solution. Reproducing the exact MSBuild property
interaction on a minimal, disposable solution is faster to iterate on and
does not risk that repo's state.

## Decisions

- Build a **minimal external MVP repro** outside this repository (per explicit
  instruction: do not commit it here) at `C:\Scratch\GenRepro`:
  - `Generator\Generator.csproj` — `netstandard2.0`, `IsRoslynComponent=true`,
    references `Microsoft.CodeAnalysis.CSharp` / `Microsoft.CodeAnalysis.Analyzers`.
  - `Generator\HelloGenerator.cs` — a trivial `IIncrementalGenerator` that
    emits a `namespace Consumer { public static partial class GeneratedGreeter
    { ... } }` via `RegisterPostInitializationOutput`, chosen specifically so
    its presence (or absence) in the semantic model is trivial to check by
    name with `find_symbol_definition` / `get_diagnostics_for_file`.
  - `Consumer\Consumer.csproj` — references `Generator` via
    `<ProjectReference OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`,
    and `Consumer\Program.cs` calls `Consumer.GeneratedGreeter.GetGeneratedMarker()`
    (so a missing generator output shows up as `CS0103`).
  - Root `Directory.Build.props`:
    ```xml
    <PropertyGroup>
      <BaseOutputPath>$(MSBuildThisFileDirectory)artifacts\$(MSBuildProjectName)\</BaseOutputPath>
      <OutputPath>$(BaseOutputPath)$(Configuration)\</OutputPath>
      <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    </PropertyGroup>
    ```
    — this is the exact shape of the reported "enterprise-style" override:
    build output goes to `artifacts\{ProjectName}\{Configuration}\` instead of
    `bin\{Configuration}\{TFM}\`.
- Add a **diagnostic-only** `load_workspace` flag, `logProjectOutputDiagnostics`
  (default `false`), before attempting any fix. It must only report facts to
  the MCP server log — never change resolution or mutate anything — so the
  problem can be confirmed/refuted without risk.

## Scope and non-goals

- In scope: reproduce the symptom on a disposable solution; add read-only
  diagnostics; confirm or refute both suspected failure modes above.
- Out of scope (deferred to Epoch 2): any actual fix, any `Solution` mutation,
  any change to `AnalyzerReference` resolution.

## Implementation delta

- **New:** `Services/ProjectOutputDiagnosticsLogger.cs`
  - `Collect(Solution)` — pure data collection: for every project, its
    `AssemblyName`, `OutputFilePath` (existence + last-write time),
    `CompilationOutputInfo.GeneratedFilesOutputDirectory` (existence), and for
    every `AnalyzerReference` on that project: `Display`, `FullPath`
    (existence + last-write time).
  - `Log(Solution, ILogger)` — writes one Information-level line per project
    plus one line per `AnalyzerReference`, formatted for `tail_tool_log` /
    `read_log_tail`. Never included in the tool's return value (diagnostic
    log only, to avoid bloating the agent-facing response).
- **Changed:** `Tools/WorkspaceTools.cs` `LoadWorkspace` — new optional
  `bool logProjectOutputDiagnostics = false` parameter; when true, calls
  `ProjectOutputDiagnosticsLogger.Log` after a successful load.
- **New tests:** `RoslynMcpServer.Tests/ProjectOutputDiagnosticsLoggerTests.cs`
  using an `AdhocWorkspace` fixture (`ReproFixture`) to simulate matching and
  non-matching `OutputFilePath` / `AnalyzerReference` scenarios without a real
  MSBuild project on disk.

## Verification (confirmed both suspected failure modes)

Against `C:\Scratch\GenRepro`, with `logProjectOutputDiagnostics=true`:

- The log showed `exists=false` for the `Generator` `AnalyzerReference` on the
  `Consumer` project — i.e. the design-time-resolved `AnalyzerReference.FullPath`
  pointed at a file that does not exist on disk.
- Specifically, Roslyn's resolved `AnalyzerReference` path was missing the
  `\Debug\` configuration segment relative to `Generator`'s own resolved
  `OutputFilePath` / `CompilationOutputInfo.AssemblyPath` under
  `artifacts\Generator\Debug\Generator.dll` — confirming mode 1 (wrong path),
  even though a real `dotnet build` of the whole solution succeeds.
- `find_symbol_definition("GeneratedGreeter")` — not found.
- `get_diagnostics_for_file("Consumer/Program.cs")` — `CS0103: The name
  'Consumer' does not exist in the current context` (the generator never ran
  in the Roslyn semantic model, even though `dotnet build` succeeds).

Mode 2 (locking) was not directly exercisable yet at this point, since no fix
loaded the analyzer assembly through Roslyn at all — the broken path meant it
was never loaded in the first place. It is exercised and confirmed as an
architectural property of the design in Epoch 3's verification (shadow copy
never locks the analyzer project's real output).

## Exit / handoff criteria

- Root cause of the "generator not running" symptom is confirmed on a
  reproducible, disposable solution: MSBuildWorkspace's design-time
  `AnalyzerReference.FullPath` for an `OutputItemType="Analyzer"`
  `ProjectReference` can disagree with the referenced project's own resolved
  build output when `Directory.Build.props` overrides `OutputPath`.
- `logProjectOutputDiagnostics` is shipped, tested, documented, and
  diagnostic-only (no behavior change risk).
- Proceed to Epoch 2 to design and implement an actual fix.
