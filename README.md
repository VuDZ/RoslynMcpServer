# RoslynMcpServer

[🇷🇺 Читать на русском (Russian Version)](#russian-version)

RoslynMcpServer gives an AI coding agent compiler-aware tools for C# solutions: semantic navigation, diagnostics and code fixes, refactoring, dependency inspection, and bounded `dotnet` build/test/run output.

Unlike a filesystem MCP, it loads `.sln`, `.slnx`, or `.csproj` through Roslyn/MSBuild. The agent can resolve symbols and project context instead of inferring them from text alone.

## MCP and agent: the 60-second model

- **MCP client** — Cursor, OpenCode, or another compatible host. It starts the server and exposes its tools to the model.
- **AI agent** — the model plus the client's orchestration. It decides which tool to call; it is not part of this repository.
- **RoslynMcpServer** — a local stdio process. It exposes tools but has no chat UI and does not act autonomously.
- **`AGENTS.md`** — repository policy for the agent. MCP configuration makes tools available; this file tells the agent when to use them.

Typical flow:

`developer request → agent → MCP tool call → Roslyn/MSBuild/ILSpy/dotnet → compact result → agent`

The server is C#-focused. It can read non-C# files and execute selected CLI operations, but it does not provide Python semantic analysis. See [Architecture and constraints](docs/ARCHITECTURE.md) for component boundaries, state, synchronization, and extension rules. Planned (not shipped) large-solution load cache: [docs/workspace-load-cache/](docs/workspace-load-cache/README.md).

## What it provides

- Compiler-aware declaration, usage, implementation, and call-graph navigation.
- Roslyn diagnostics, code actions, AST edits, and semantic rename.
- ILSpy inspection of referenced or explicitly addressed assemblies.
- Build, test, run, format, and NuGet workflows with SDK alignment, timeouts, parsed diagnostics, and context-safe output.
- `full` and context-saving `lite` tool catalogs, plus in-session tool-group discovery.

## Security boundary

This server can read and write files and start `dotnet`/Git child processes with the permissions of its host process. It is **not a sandbox** and does not confine every operation to the loaded workspace.

- Use Git and review agent changes.
- Do not run the server as Administrator/root.
- Never put PATs, passwords, connection strings, or other secrets in prompts or tool arguments.
- Incoming MCP parameters are logged by default; see [Logs](#logs) before using the server with sensitive repositories.

## Prerequisites

- An MCP client with local stdio-server support.
- A .NET SDK compatible with the solution being analyzed; honor the repository's `global.json`.
- The **.NET 10 SDK** when building this server from source.
- Git is strongly recommended for reviewing and reverting agent changes.

## First setup

Run the **published executable**, not `dotnet run`. The publish is self-contained and ReadyToRun, but intentionally not single-file because Roslyn, MSBuild, analyzers, and BuildHost dependencies are loaded dynamically.

### 1. Publish the server

From this repository (replace the RID for another platform):

```bash
dotnet publish RoslynMcpServer.csproj -c Release -r win-x64
```

The Windows x64 binary is written to:

- Windows x64: `bin/Release/net10.0/win-x64/publish/RoslynMcpServer.exe`

Linux/macOS use the same relative publish layout under the selected RID.

### 2. Register the local stdio server

In Cursor, use **Cursor Settings → Tools & MCP**, or create a project-level `.cursor/mcp.json`. JSON paths can use forward slashes on Windows:

```json
{
  "mcpServers": {
    "roslyn-mcp-server": {
      "command": "C:/absolute/path/to/RoslynMcpServer.exe",
      "env": {
        "ROSLYN_MCP_WORKSPACE": "C:/absolute/path/to/YourApp"
      }
    }
  }
}
```

`ROSLYN_MCP_WORKSPACE` is optional but recommended: set it to the target repository root, especially when that repository contains `global.json`. For smaller local models, configure `ROSLYN_MCP_TOOL_PROFILE` / `ROSLYN_MCP_TOOL_GROUPS` as described in [Tool profiles](#tool-profiles).

Restart or reload MCP servers after changing client configuration.

### 3. Add agent policy to the target repository

Copy or merge [`AGENTS.md.sample`](AGENTS.md.sample) into the application repository as `AGENTS.md`. Keep repository-specific commands and entry points there. This is separate from MCP registration: without policy, the tools may be connected but the agent can still choose generic text search or shell commands.

### 4. Verify the first session

Ask the agent:

> Use Roslyn MCP. Call `get_mcp_server_info`, then `load_workspace` for the absolute path to this repository's solution. List the loaded projects and report workspace health. Do not modify files.

A healthy session reports the server binary/version, the active tool profile, SDK/restore health, and at least one loaded project. Then try a semantic request such as “find the definition and usages of `SolutionManager`.”

If tools are missing, call `list_tool_groups`. If workspace loading fails, follow the returned diagnostic rather than switching to shell `dotnet`.

### OpenCode installer

After `dotnet publish`, the publish folder contains [`install2opencode.ps1`](install2opencode.ps1) and [`AGENTS.md.sample`](AGENTS.md.sample) next to `RoslynMcpServer.exe`.

From the root of the **project you want to configure** (your application repo):

```powershell
cd D:\Devel\YourApp
& "D:\Devel\RoslynMcpServer\bin\Release\net10.0\win-x64\publish\install2opencode.ps1"
```

The script:

- Creates or updates **`opencode.json`** in the target directory, registering `RoslynMcpServer` as a local MCP server (`type: local`, `enabled: true`, **`timeout`: `600000`** ms = 10 minutes).
- Creates or updates **`AGENTS.md`**: if Roslyn MCP behavioral rules are not already present, merges content from the bundled `AGENTS.md.sample` (creates a new file or appends to an existing one).

**OpenCode MCP host timeout:** OpenCode’s default for MCP `tools/call` is about **60 seconds**. That is **not** the tool argument `timeoutSeconds` on `run_dotnet_test` / `run_dotnet_run`. Without raising the host timeout, long build/test calls fail with `McpError: MCP error -32001: Request timed out` (~60s) even if you pass `timeoutSeconds: 900`. Set `"timeout": 600000` (or higher) on the server entry — see [`opencode.json.sample`](opencode.json.sample). `install2opencode.ps1` writes this for you. If an older OpenCode build ignores per-server `timeout`, also try `"experimental": { "mcp_timeout": 600000 }` in `opencode.json`.

Example server block:

```json
"roslyn-mcp-server": {
  "type": "local",
  "command": ["C:/path/to/RoslynMcpServer.exe"],
  "timeout": 600000,
  "enabled": true
}
```

| Parameter | Description |
| --- | --- |
| `-BinaryPath` | Optional. Absolute path to `RoslynMcpServer.exe`. When omitted, the script uses the binary next to itself. |
| `-ProjectPath` | Optional. Target project root. Defaults to the current working directory. |

Restart OpenCode or reload MCP servers after running the script.

## Tool profiles

Default is **`full`** (every public tool). A **`lite`** session starts with the 19-tool core so local models spend less context on `tools/list`. Extra groups can be added at process start or, on clients that honor `notifications/tools/list_changed`, during the session.

| Variable | Values | Effect |
| --- | --- | --- |
| `ROSLYN_MCP_TOOL_PROFILE` | `full` (default) or `lite` | Selects the startup catalog. Empty/unset is `full`. |
| `ROSLYN_MCP_TOOL_GROUPS` | `core`, `files`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations` (comma-separated, case-insensitive) | Adds those groups to **`lite` before the first `tools/list`**. In `full` the names are recorded and do not change the set. Unknown names fail startup. `core` is already in lite. |

Example: `ROSLYN_MCP_TOOL_GROUPS=decompile,nuget`

Portable OpenCode examples: [`opencode.json.sample`](opencode.json.sample) (`roslyn-mcp-server` = full, `roslyn-mcp-lite`, `roslyn-mcp-lite-groups`). Enable only one server.

| Group | Intent | Tools |
| --- | --- | ---: |
| `core` | Workspace, navigation, build, test, help | 19 (lite default) |
| `files` | Disk read, search, patch | 7 |
| `editing` | AST edits, code fixes, format, rename | 17 |
| `decompile` | Third-party assemblies | 4 |
| `nuget` | Package list, audit, search, add/remove | 6 |
| `project` | Solution graph and project rename | 3 |
| `runtime` | Run apps, list tests, raw `dotnet` | 3 |
| `operations` | Logs, scratchpad, process lifecycle | 4 |

**Discovery (Markdown, not a replacement for JSON Schema):**

- `list_tool_groups` — purpose, active/inactive, member names, startup fallback.
- `get_tool_help` — kind/group plus **live** parameters from the registered method schema.
- `enable_tool_group` — add one group to this process (idempotent; `full` is a no-op). Sends `tools/list_changed` only when the set actually grows. There is **no** disable in this release.

MCP `tools/list` stays JSON + JSON Schema. Markdown is for JIT help and diagnostics.

**Client compatibility:** do not assume the host refreshes tools after `tools/list_changed`. If a newly enabled tool is missing from the client's catalog, restart with `ROSLYN_MCP_TOOL_GROUPS=<group>` (and `ROSLYN_MCP_TOOL_PROFILE=lite`). Measured minified `tools/list` (UTF-8): full **63 / 44,165** (~43.1 KB); lite **19 / 16,579** (~16.2 KB). Adding `editing` to lite is above the 20 KB *startup-lite* budget (expected).

## Agent tools by version

Tracks MCP tools relevant to [`AGENTS.md.sample`](AGENTS.md.sample) (copy into app repos as `AGENTS.md`). Current server version: see `RoslynMcpServer.csproj`.

### v1.3.12

- **Analyzer provenance acceptance (E5-S4).** The original redirected missing-path repro now rewrites from confirmed load-session provenance and executes exact `V1`. Same-name foreign analyzers keep their path and execute exact `FOREIGN` (missing foreign is not replaced by the in-solution candidate). Lifecycle diagnostics distinguish `source_output_missing`, `provenance_unconfirmed`, and `access_failure` on those fixtures. The public `load_workspace` schema is unchanged.
- **Catalog size** — unchanged: full 63 tools / 44,165 bytes; lite 19 / 16,579.

### v1.3.11

- **Analyzer provenance diagnostics (E5-S3).** Shadow-copy decisions now carry stable internal reason codes and independent original/source path states, plus selected project, selection basis, source path, and generation. Concrete missing-output, ambiguity, proven-foreign, access, and preparation failures are no longer collapsed into generic unconfirmed provenance. The `load_workspace` response remains a compact text summary; detailed fields are logged without adding a public structured MCP schema.
- **Catalog size** — unchanged: full 63 tools / 44,165 bytes; lite 19 / 16,579.

### v1.3.10

- **Analyzer provenance rollout (E5-S2).** `shadowCopyInSolutionAnalyzers` now rewrites only analyzer references that the complete load-session F-09 snapshot exact-joins to one loaded source project. Filename/`AssemblyName`, first-candidate, and incomplete-capture fallbacks are removed. Same-name external or unconfirmed missing references remain unchanged; ambiguous source projects are skipped. Source binding requires an exact resolved output and rejects contradictory effective TFM metadata.
- **Catalog size** — unchanged: full 63 tools / 44,165 bytes; lite 19 / 16,579.

### v1.3.9

- **Analyzer provenance capture (F-09 production snapshot).** Every physical `MSBuildWorkspace` open now records the same design-time build into process-private binlogs, replays them into an immutable load-session snapshot, and exact-joins captured analyzer items to loaded consumer/source `ProjectId`s. Replay is fail-closed and temporary data is deleted before publication. Cached loads, source edits, semantic queries, and analyzer artifact refreshes do not recapture; graph-stale, changed load globals, and reset+load do. This release does not change the analyzer matcher or enable E5-S2 rollout.
- **Catalog size** — unchanged: full 63 tools / 44,165 bytes; lite 19 / 16,579.

### v1.3.8

- **Workspace write boundary (epoch 4).** All production `TryApplyChanges` and server document writes go through one preflight → exact original↔shadow inverse → persist → apply/reconciliation → overlay publish workflow. Unsupported or stale analyzer-reference diffs (including CodeAction / `rename_symbol`) are rejected before any server write instead of silently wiping the analyzer list. Partial persistence is not full success: the reply includes Status, Reason, and the known saved paths (existing tool text, no new MCP schema). Post-apply reuses the prepared mapping with no analyzer file I/O. Upgrade from v1.3.5: consumer edit no longer recopies analyzer files and will not pick up rebuilt generator bytes — after a same-identity rebuild, restart the MCP process.
- **Catalog size** — unchanged: full 63 tools / 44,165 bytes; lite 19 / 16,579.

### v1.3.7

- **`shadowCopyInSolutionAnalyzers` loader contract (epoch 3).** Chosen mode is **restart-required**: a rebuilt generator with the same assembly name/version cannot execute in the current MCP process (cached load and `reset_workspace`+load refuse with an explicit “restart the MCP server process” action). A new process runs the new bytes. Supported dependency policy is **main-only**; private helper DLLs are refused rather than guessed from the output directory. Rewrite count still means reference rewrite only; execution status is a separate line in the load summary.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 44,165 bytes; lite 19 / 16,579.

### v1.3.6

- **`shadowCopyInSolutionAnalyzers` immutable mapping (epoch 2).** Analyzer shadow copies are content-hashed main-only generations under `v2-main-only/` (manifest-last, same-volume no-replace publish). Preparation runs at load/enable and explicit refresh only; document edit, watcher flush, and post-apply reapply the in-memory mapping with **zero analyzer file I/O**, so a missing original output path no longer drops the overlay. Failed refresh keeps a stale mapping instead of reverting to broken originals. Timestamp directories are not migrated. CLR reload of a rebuilt generator remains an epoch-3 / U-ARB-02 gate.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 43,895 bytes; lite 19 / 16,309.

### v1.3.5

- **Fix: `shadowCopyInSolutionAnalyzers` (v1.3.4) no longer touches the real `.csproj` on disk.** The v1.3.4 implementation applied the rewritten `Solution` via `Workspace.TryApplyChanges`, which for `MSBuildWorkspace` persists `AddAnalyzerReference`/`RemoveAnalyzerReference` back into the backing project file. Confirmed against the `C:\Scratch\GenRepro` repro: this injected a machine-/session-specific temp shadow path as a literal `<Analyzer Include=...>` MSBuild item and could not remove the original `ProjectReference OutputItemType="Analyzer"` reference (it is synthesized by MSBuild, not a literal item), leaving both active — the generator then ran twice and the next real `dotnet build` failed with `CS0102`/`CS0111` duplicate-member errors. Fix: `SolutionManager` now keeps the rewrite purely in-memory instead of ever pushing it back via `TryApplyChanges`. *(Historical wording said `GetCurrentSolution()` re-derived the overlay on every read. The getter returned a stored `_solution` even then; v1.3.6+ documents that explicitly and reapplies mapping only at mutation points.)* `FindDocumentAsync` (backs `get_diagnostics_for_file`, `find_symbol_references`, AST/code-fix tools) now also reads through `GetCurrentSolution()` instead of `workspace.CurrentSolution` directly, so the fixed semantic model (no more `CS0103` on generator-produced types) reaches those tools too. Any tool that edits a document and calls back into `ApplySolutionChangesToDiskAsync` (rename, code fix, generated test stub) is defended by a new guard that strips any accidental `AnalyzerReference` diff before it reaches `TryApplyChanges`, so the same corruption cannot resurface via a different call path. No tool parameters changed; `shadowCopyInSolutionAnalyzers` behaves the same from the caller's side, just without the disk side effect. **Known separate limitation (not something this fixes):** `find_symbol_definition` still cannot locate a generator-emitted type by name — `SymbolFinder` [never searches source-generated documents, by Roslyn design](https://github.com/dotnet/roslyn/issues/63375). `get_diagnostics_for_file` / `find_symbol_references` from a real usage site are unaffected.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / (see `get_mcp_server_info`); lite 19 / (unchanged — no tool surface change).

### v1.3.4

- **`load_workspace` `shadowCopyInSolutionAnalyzers`** — Optional bool (default `false`). Fixes the case `logProjectOutputDiagnostics` (v1.3.3) diagnoses: rewrites any `AnalyzerReference` whose file name matches another (unambiguous) in-solution project's `AssemblyName` to a private shadow copy of that project's own resolved output (`CompilationOutputInfo.AssemblyPath` / `OutputFilePath`, not the possibly-broken original reference path). Confirmed against an external repro: without the fix, `Directory.Build.props` overriding `OutputPath` left an `OutputItemType="Analyzer"` `ProjectReference`'s resolved path missing on disk, so the generator never ran in the Roslyn semantic model even though `dotnet build` succeeded (`CS0103` on a generator-produced type). Also avoids locking the analyzer project's real build output (a prior `dotnet build` of it would otherwise fail with `MSB3027`) — shadow copies are namespaced by the source file's last-write time, so a rebuilt analyzer is picked up fresh on the next `load_workspace`. Requires the referenced project to already have a build output on disk (build it once first). Returns a one-line summary; details go to the MCP server log via `tail_tool_log` / `read_log_tail`. New: `Services/AnalyzerReferenceShadowCopier.cs` (pure rewrite + `Solution.WithProjectAnalyzerReferences`), `Services/InProcessAnalyzerAssemblyLoader.cs` (public-API-only `IAnalyzerAssemblyLoader`, since Roslyn's own non-locking loader is `internal`). **Superseded by v1.3.5**: the application mechanism described here (`Workspace.TryApplyChanges`) corrupted the real `.csproj` — see the v1.3.5 entry above.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 43,619 bytes; lite 19 / 16,033.

### v1.3.3

- **`load_workspace` `logProjectOutputDiagnostics`** — Optional bool (default `false`). When `true`, logs one Information-level line per project (`OutputFilePath`, exists/last-write, `CompilationOutputInfo.GeneratedFilesOutputDirectory`, exists) plus one line per `AnalyzerReference` (`Display`, `FullPath`, exists/last-write) to the MCP server log — not the tool's return value. Diagnostic-only aid for the known pitfall where a repo-wide `Directory.Build.props` overrides `OutputPath` (e.g. into a shared `artifacts` folder) and MSBuildWorkspace design-time evaluation ends up pointing an analyzer/generator project's `AnalyzerReference` at a stale or missing DLL, silently disabling source generation. Read the result with `tail_tool_log` / `read_log_tail`. New helper: `Services/ProjectOutputDiagnosticsLogger.cs` (`Collect` for pure data, `Log` to write it).
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 42,632 bytes; lite 19 / 15,046.

### v1.3.2

- **`binariesPath` on `run_specific_test` / `run_dotnet_test`** — Same bin-directory mode as `run_test_by_filter`: loaded `.sln`/`.slnx` plus a `.csproj` `workspacePath`; runs `{AssemblyName}.dll` from that directory (the DLL sits directly in `binariesPath`, no `OutputPath` join). When `noBuild=false`, pre-test compile is `dotnet build <sln> -t:"Folder\Project"` (configuration/platform/`buildArgs` from `load_workspace`), then `dotnet test <dll> --no-build`. When `noBuild=true`, the DLL must already exist. After a successful sln-target build, a missing DLL reports the expected path plus a Configuration / `.runtimeconfig.json` hint.
- **`run_test_by_filter` `noBuild=false`** — Honor `noBuild` with the same sln-target pre-build; previously any `binariesPath` skipped compile.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 41,958 bytes; lite 19 / 14,372.

### v1.3.1

- **`get_test_list` filters** — Optional `projectName` (exact match on loaded Roslyn project name, file name, or assembly) and `nameContains` (case-insensitive substring of the VSTest FQN, class, or method). Both apply **before** `maxResults`. Unknown or ambiguous `projectName` returns the project list, not empty JSON. Filtered `count: 0` is a no-match signal (not “wrong `.csproj`”). Each test item includes `projectName`.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 41,490 bytes; lite 19 / 13,904.

### v1.3.0

- **`run_test_by_filter`** — New tool. Passes a raw VSTest `--filter` to `dotnet test` (`FullyQualifiedName~MyClass`, `TestCategory=Smoke`, …). Default `noBuild=true`. Optional `binariesPath` is a bin directory: requires a loaded `.sln`/`.slnx` and a `.csproj` `workspacePath`; runs `{AssemblyName}.dll` from that directory (no pre-test build). Prefer `run_specific_test` for one class or method.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 41,139 bytes; lite 19 / 13,904.

### v1.2.3

- **`run_dotnet_build` `projectName`** — Optional. When set, `workspacePath` must be a `.sln`/`.slnx`. Resolves the project's Solution Explorer virtual path (solution folders, not the filesystem path) and runs `dotnet build <sln> -t:"Folder\Project"` so solution Configuration/Platform mappings apply. Match is case-insensitive on display name, file name without extension, or virtual path (`src\App`). Missing or ambiguous names return the `Name → path` list. `.csproj` + `projectName` is an error.
- **`dotnet build` `-p:Configuration`** — Probe steps and the pre-test compile now pass `-p:Configuration=` (same as existing `-p:Platform`). `dotnet test` still uses `-c` so output is found under `bin/{configuration}`.
- Metadata reports `ProjectName` and `SolutionTarget` (`-t`) to diagnose MSB4057.
- **Catalog size** — minified `tools/list` UTF-8: full 62 tools / 39,738 bytes; lite 18 / 12,503.

### v1.2.2

- **`load_workspace` `briefOutput`** — Optional. Default `false` keeps the full MSBuild/NuGet warning dump (plus notes). `true` collapses successful-load warnings to category and code counts (`MSB3270×28`, `NU1701×5`, …). Health block and project list stay. Failures (`Workspace Load Failed`, missing TFM/Compile, BuildHost, client abort) always print in full. Not session-cached.
- **Catalog size** — minified `tools/list` UTF-8: full 62 tools / 39,408 bytes; lite 18 / 12,173.

### v1.2.1

- **`load_workspace` `buildArgs`** — Optional extra arguments appended to later `dotnet build` only: the `run_dotnet_build` probe steps and the pre-test compile used by `run_dotnet_test` / `run_specific_test`. Session-cached like `configuration` / `platform` (not an MSBuildWorkspace property; changing `buildArgs` does not reopen the solution). Omit or whitespace clears the session suffix. Do not put `-c` / `-p:Platform` / `-v` / `--no-incremental` here. Not applied to restore, `dotnet test`, or `dotnet run`.
- **Catalog size** — minified `tools/list` UTF-8: full 62 tools / 39,063 bytes; lite 18 / 11,828.

### v1.2.0

- **Tool profiles** — `ROSLYN_MCP_TOOL_PROFILE=full|lite` (default `full`). Lite starts with 18 core tools (workspace, navigation, build/test, `list_tool_groups` / `get_tool_help` / `enable_tool_group`).
- **Startup groups** — `ROSLYN_MCP_TOOL_GROUPS=decompile,nuget` expands lite before the first `tools/list`. Fallback for clients that ignore `tools/list_changed`.
- **`list_tool_groups` / `get_tool_help`** — compact Markdown help. Parameter names/types/defaults come from the live schema; Markdown does not replace JSON Schema.
- **`enable_tool_group`** — add one catalog group at runtime; one `tools/list_changed` per real change. Idempotent; `full` is a no-op. No runtime disable.
- **Catalog size** — minified `tools/list` UTF-8: full 62 tools / 38,500 bytes; lite 18 / 11,265. Description text on the original 59 tools: 35,445 → 13,387 (−62%).

### v1.1.1

- **`explore_assembly` / decompile `assemblyName`** — dotted simple names (`Spectre.Console`, `Newtonsoft.Json`) are no longer truncated at the last `.`. Only a trailing `.dll` / `.exe` is stripped; previously `Path.GetFileNameWithoutExtension` looked for `Spectre.dll`.

### v1.1.0

- **Disk sync for saved `.cs`** — after `load_workspace`, a `FileSystemWatcher` records dirty source paths (not every keystroke: unsaved editor buffers are ignored). Before `find_symbol_*` / `find_usages` / `get_class_skeleton` / test discovery, only those files are read and applied with one `TryApplyChanges`. Host/git/`dotnet format` edits of existing files show up without `reset_workspace`. New `.cs` under a project folder are `AddDocument`’d; deleted files are removed. `.csproj`/`.sln`/`Directory.Build.props` set a graph-stale hint and skip the `load_workspace` cache — still no automatic `OpenSolutionAsync` from the watcher. `reset_workspace` remains for generated `obj` files after build. Linux uses inotify (watch-limit errors log and degrade; they do not crash the process).

### v1.0.35

- **VS 2026 / MSBuild 18 BuildHost (`XMakeElements`)** — `Microsoft.CodeAnalysis.*` **5.9.0** (Roslyn AppDomain isolation for the net472 BuildHost). `load_workspace` / `find_symbol_*` no longer die with a raw `TypeInitializationException` on machines with Visual Studio 2026; residual crashes return dedicated **Workspace Load Failed (VS 2026 / MSBuild 18 BuildHost)** and are **not** `MCP_MSBUILD_SDK_MISMATCH`. Workaround: load a single SDK-style `.csproj`. `search_code` / host Grep still work.

### v1.0.34

- **`load_workspace` design-time MSBuild warnings** — MSBuildWorkspace wraps ASP.NET/SDK deprecation (`IncludeOpenAPIAnalyzers` / ASPDEPR007), processor-architecture mismatch (MSB3270), and analyzer-project metadata refs as `Msbuild failed when processing the file` with `Failure` kind, often **without** a `warning XXXX` prefix. Those no longer fail load when projects opened; remapped to **Warning (MSBuild design-time)**. Wrapped messages without `error NU|MSB|NETSDK` or a known-hard inner text (`could not be loaded`, missing `Compile`, empty TFM, SDK not found) are warnings. Explicit errors and unloadable projects still fail.

### v1.0.33

- **`load_workspace` `targetFramework`** — Optional MSBuild `TargetFramework` global property (same idea as `dotnet build -f`). Needed when `Directory.Build.props` / csproj sets `TargetFrameworks`: the CrossTargeting outer evaluation has no `Compile` target and Roslyn cannot load. Dedicated failure **Workspace Load Failed (missing Compile target)** lists TFMs from the nearest props and tells the agent to retry with one inner TFM (e.g. `net10.0`). Not inherited by `run_dotnet_build`. `get_code_skeleton` / host Grep remain usable without a workspace.

### v1.0.32

- **`run_specific_test` slow-test duration** — VSTest console lines for long runs use `[1 s]` / `[1 m 28 s]`, not only `[12 ms]`. Parser now accepts those units so a passing filtered test is not reported as **no matching tests**.

### v1.0.31

- **`run_dotnet_test` / `run_specific_test` pre-test build** — When `noBuild=false`, compile with a separate incremental `dotnet build` (same `-c` / `-p:Platform`), then `dotnet test --no-build --no-restore`. VSTest summary is parsed from the test process only, so MSBuild warning dumps no longer produce **`Status: partial`**. Build failure/timeout is reported and tests are not started. `noBuild=true` still skips the extra compile. Shared `timeoutSeconds` covers both processes.

### v1.0.30

- **`apply_patch` replaceAll hang** — `replaceAll=true` no longer rescans the inserted `newString`. When `newString` contains `oldString` (typical rename `Foo` → `Ns.Foos`, production case `AllSoftNotificationHelper` → `HelperContainer.Eis.AllSoftNotificationHelpers`) the old `while (IndexOf)` loop grew the file forever and never returned — OpenCode showed a freeze with nothing useful in logs. Matching now advances past each insert (same as `string.Replace`); logs `ApplyPatch start/matched/wrote` with lengths, `newContainsOld`, replacement count, and elapsed ms.

### v1.0.29

- **`load_workspace` configuration / platform** — Optional MSBuild global properties (same names as the VS active solution config). Cached for `run_dotnet_build` / `run_dotnet_test` / `run_specific_test` when those tools omit `-c` / `-p:Platform`. Empty `TargetFramework` (`ResolvePackageAssets`) stays a load failure, with a dedicated report (retry with IDE config, or Bazel-generated csproj are not evaluable).

### v1.0.28

- **`run_dotnet_test` / `run_specific_test` .slnx summary** — VSTest/MSBuild console may omit `Passed:` when tests fail (and omit `Failed:` on success). Parser infers `Passed = Total − Failed − Skipped` so a fail-only `Total tests` block is not reported as **partial**.

### v1.0.27

- **`load_workspace` NU1701 / MSBuild wrapper** — Package TFM-compat restore warnings (`NU1701`, netfx assets in a netcore/net10 project) remapped to warnings — do not fail load when other projects opened. The word `failed` in Roslyn's `Msbuild failed when processing the file` wrapper is no longer treated as fatal by itself; explicit `error NU|MSB|NETSDK` and unloadable projects still fail load.

### v1.0.26

- **Agent diagnostics for workspace/test discovery** — `load_workspace` cancelled by the MCP host returns **Workspace Load Cancelled (client abort)** (not MSBuild failure) with OpenCode timeout guidance; `get_test_list` with `count: 0` explains wrong `.csproj` scope; `run_specific_test` no-match reports Roslyn vs `dotnet test` path mismatch and name-suffix fallback.

### v1.0.25

- **`run_dotnet_build` trust** — Default `noIncremental=true` (`--no-incremental`) so MSBuild up-to-date cache cannot report a fake success after edits; set `false` only for large monorepos that accept incremental risk. Effective exit uses the **last** `dotnet build` step (restore exit 0 cannot mask a failed build with no rebuild). Success/failure metadata includes `Configuration` and `NoIncremental`.

### v1.0.24

- **SDK env metadata** — `run_dotnet_build` / `run_dotnet_test` / `run_specific_test` report inherited `MSBuildSDKsPath`, strip vs `global.json` pin action, and SDK version inferred from MSBuild/NETSDK log paths

### v1.0.23

- **`dotnet` child env** — Without `global.json` pin, strip inherited `MSBuildSDKsPath` / `MSBUILD_EXE_PATH` / `DOTNET_MSBUILD_SDK_RESOLVER_*` (MSBuildLocator / IDE pollution) so `run_dotnet_build` / `run_dotnet_test` use the host SDK instead of e.g. 9.x → `NETSDK1045` on net10

### v1.0.22

- **`run_specific_test` method filter** — VSTest-safe filters: Roslyn method FQN without `()`; always `FullyQualifiedName~` (not `=`); no bogus leading `.` when `methodName` is already a dotted FQN; escape `\ ( ) & | = ! ~`

### v1.0.21

- **`load_workspace` soft prune** — Unused `PackageReference` / NuGet prune advisories (`will not be pruned`, prune package data) remapped to warnings — do not fail load when projects opened

### v1.0.20

- **`configuration`** — Optional `configuration` (`dotnet -c`) on `run_dotnet_build`, `run_dotnet_test`, `run_specific_test` for multi-config solutions (`Sit-Debug`, `Dit-Debug`, …)

### v1.0.19

- **`.slnx` support** — `load_workspace`, `run_dotnet_build`, `run_dotnet_test` / `run_specific_test`, discovery, and NuGet/format paths accept `.slnx`; prefer solution files for multi-config repos
- **OpenCode host timeout** — Document + `opencode.json.sample` / `install2opencode.ps1`: `"timeout": 600000` ms — avoids MCP `-32001` (~60s); separate from tool `timeoutSeconds`

### v1.0.18

- **Tool descriptions** — Accurate agent-facing `[Description]` across build/test/decompile/NuGet/navigation/AST params

### v1.0.17

- **`run_dotnet_test` / `run_specific_test`** — Optional `noBuild` / `noRestore` (`--no-build` / `--no-restore`); after build use `noBuild=true` for faster re-runs. Default `noBuild=false` compiles in a separate `dotnet build` then tests with `--no-build`.

### v1.0.16

- **`run_dotnet_test` / `run_specific_test`** — `timeoutSeconds` default **300**; kill process tree on timeout/cancel
- **`run_dotnet_build` probe** — Overall wall-clock budget (~300s) + per-step timeout; skip escalate when budget exhausted
- **`execute_dotnet_command`** — Same default timeout + kill on cancel
- **Silent fail UX** — Hints for zombie `dotnet` / locked `obj` when exit≠0 and no parsed diagnostics

### v1.0.15

- **`search_code`** — `caseSensitive` (default `false`); leftover branding → `caseSensitive=true`
- **No-workspace UX** — Semantic tools list candidate `.sln`/`.slnx` under `ROSLYN_MCP_WORKSPACE` / cwd (no auto-load)
- **`rename_project`** — SDK-style dir+csproj+ProjectReference+.sln/.slnx; `dryRun`; no namespace chain
- **Branding recipe** — Documented in `AGENTS.md.sample` (hybrid MCP + host edit)

### v1.0.14

- **`run_dotnet_run`** — SDK-pinned `dotnet run`, separate stdout/stderr, timeout, truncated output (stderr tail for progress)
- **`run_nuget_audit`** — Structured vulnerability table from `dotnet list package --vulnerable`
- **`get_changed_files`** — Git porcelain status + suggested test projects (no diff body)
- **`load_workspace`** — **Workspace health** block: SDK/global.json, restore assets, tool count
- **`execute_dotnet_command`** — SDK pinning + truncated stdout/stderr
- **`find_usages` / `find_symbol_references`** — **find_references** family; prefer `find_usages` when only `symbolName` is known
- **`get_project_graph` / `list_projects`** — Project dependency graph
- **`rename_symbol`** — `previewOnly=true` default workflow; **C# symbols only**
- **`run_format`** — `dotnet format` wrapper

### v1.0.13

- **`AssemblyReferenceResolver`** — Exact `{name}.dll`; deps.json + NuGet fallback
- **`DecompilerHost`** — NuGet / BCL / runtime pack resolver for ILSpy tools

### v1.0.10–v1.0.12

Build/test SDK pinning, VSTest parser, decompiler `assemblyPath`, `MCP_MSBUILD_SDK_MISMATCH` — see git history.

### Not implemented (see AGENTS.md.sample — Secrets)

- Read-only TFS / HTTP probe MCP tools
- MCP «secret configured: yes/no» without reading values
- Unified diff in `get_changed_files` (use host/shell `git diff` when allowed)

## Agent Initialization (How to force tool usage)

Even if the MCP is active, AI clients don't always load the tools into the current chat context. Put the **session policy** in the app repo so the agent actually uses Roslyn MCP.

**Canonical file:** [`AGENTS.md.sample`](AGENTS.md.sample) — copy into the **application** repository as `AGENTS.md` (or merge into `.cursor/rules`). It is policy only (when / MCP vs host / bans); per-tool parameters live in MCP tool Descriptions and in **Reference: MCP Tools** below.

**OpenCode:** `install2opencode.ps1` (see **Option C** above) writes or merges the sample into `AGENTS.md` automatically.

**Maintainers:** when tools or agent-visible behavior change, update **`AGENTS.md.sample` and this README** («Agent tools by version» + Reference) together. Do not paste the full AGENTS body into README — link the sample.

Policy summary (full text in the sample):

- Verify the server with `get_mcp_server_info`; inspect missing `lite` tools with `list_tool_groups` / `enable_tool_group`
- `load_workspace` before symbol / build / decompile work; prefer `.sln` / `.slnx`
- Missing `Compile` target on load → retry with `targetFramework` (inner TFM); not SDK mismatch
- VS 2026 BuildHost / `XMakeElements` → not SDK mismatch; need MCP 1.0.35+ or a single SDK-style `.csproj`
- C# identifiers → MCP first; plain text → host Grep; never shell `grep` / `dotnet build|test`
- Saved `.cs` (v1.1.0+) sync into symbol search automatically; unsaved editor buffers are ignored; `reset_workspace` after build / generated `obj`
- IDE: host edit/write; headless: MCP `apply_patch` / AST tools
- Secrets: never paste PAT/passwords; app README must document run target / sample args

## Logs
- **Main log:** `logs/mcp-*.log` (relative to `AppContext.BaseDirectory`).
- Global incoming JSON-RPC logging is enabled by default.
- **Tool output:** logged as a one-line summary plus separate warning/error lines (not a duplicated full MCP response). Set `ROSLYN_MCP_LOG_TOOL_OUTPUT=full` to log entire tool responses at Information level.
- Environment Variables:
  - `ROSLYN_MCP_WORKSPACE` — repo root for MSBuild/SDK discovery at startup (see MCP config above).
  - `ROSLYN_MCP_TOOL_PROFILE` — `full` (default) or `lite` (see **Tool profiles**).
  - `ROSLYN_MCP_TOOL_GROUPS` — comma-separated extra groups for `lite` at startup. Valid: `core`, `files`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations` (see **Tool profiles**).
  - `ROSLYN_MCP_LOG_TOOL_OUTPUT=full` — verbose tool response logging.
  - `MCP_LOG_INCOMING_RPC=0` (disable incoming RPC logging).
  - `MCP_LOG_INCOMING_RPC_MAX_CHARS=<N>` (limit payload log length, `0` = unlimited).

## Reference: MCP Tools

**Parameter Naming Rules:**
- `filePath` — a single file (read/edit/diagnostics/logs).
- `directoryPath` — root folder (`list_directory_tree`, optional root for `search_code`).
- `includeExtensions` — optional extension filter for `search_code` (`.cs` by default; `*` = all files).
- `caseSensitive` — optional for `search_code` (default `false`; use `true` for leftover branding checks).
- `workspacePath` — `.sln` / `.slnx` / `.csproj` (and sometimes a directory): `load_workspace`, `run_dotnet_test`, `run_specific_test`, `run_test_by_filter`, `run_format`, optional reload for `list_projects` / `get_project_graph`. **`run_dotnet_build` accepts only a `.csproj`, `.sln`, or `.slnx` file path, not a directory.** Prefer `.sln`/`.slnx` for multi-config solutions.
- `symbolName` — C# identifier for `find_symbol_definition`, `find_symbol_references`, `find_usages`, and `find_implementations` (exact name; matching is case-insensitive for definition/usages/implementations).
- `diagnosticId` — compiler/analyzer id from `get_diagnostics_for_file` (e.g. `CS0246`) for `get_code_fixes` / `apply_code_fix`.
- `fixIndex` — 0-based index from `get_code_fixes` for `apply_code_fix`.
- `path` — `.cs` file or directory for `get_code_skeleton` (absolute path; disk-based, no workspace required).

When a tool accepts `filePath`, relative values are resolved against the loaded workspace root after `load_workspace`; if no workspace is loaded, fallback is `Environment.CurrentDirectory`.

There are **63** registered tools in the default `full` profile (see list below) and **1** MCP prompt (`RefactoringAssistantPrompt`). A `lite` profile starts with **19** core tools; extra groups use `ROSLYN_MCP_TOOL_GROUPS` or `enable_tool_group`.

### Workspace / Roslyn

<details>
<summary><code>load_workspace</code> — Loads .sln/.slnx/.csproj into MSBuildWorkspace.</summary>

**Parameters:**
- `workspacePath: string` — `.sln`, `.slnx`, or `.csproj` file (not a directory). Prefer solution files for multi-config repos.
- `configuration: string?` — optional MSBuild `Configuration` global property (e.g. `Sit-Debug`, `kart`). Inherited by build/test when those tools omit `-c`.
- `platform: string?` — optional MSBuild `Platform` (`Any CPU` → `AnyCPU`). Inherited by build/test as `-p:Platform=`.
- `targetFramework: string?` — optional MSBuild `TargetFramework` (e.g. `net10.0`). Pass when the solution uses `TargetFrameworks` so design-time evaluation is an inner TFM with a `Compile` target. Not inherited by build/test.
- `buildArgs: string?` — optional extra arguments appended to later `dotnet build` (probe and pre-test build). Session-cached; omit to clear. Do not include `-c`, `-p:Platform`, `-v`, or `--no-incremental`.
- `briefOutput: bool = false` — when `true`, collapse successful-load MSBuild/NuGet warnings to category and code counts. Default `false` keeps full messages. Failures always print in full.
- `logProjectOutputDiagnostics: bool = false` — when `true`, logs one Information-level line per project (`OutputFilePath`, exists/last-write UTC, `CompilationOutputInfo.GeneratedFilesOutputDirectory`, exists) and one line per `AnalyzerReference` (`Display`, `FullPath`, exists/last-write UTC) to the MCP server log — not returned in this tool's response. Diagnostic-only aid for `Directory.Build.props` overriding `OutputPath` (e.g. into a shared `artifacts` folder) so an analyzer/generator project's `AnalyzerReference` ends up pointing at a stale or missing DLL. Read with `tail_tool_log` / `read_log_tail`.
- `shadowCopyInSolutionAnalyzers: bool = false` — when `true`, rewrites only `AnalyzerReference`s whose complete load-session provenance exact-joins them to one loaded in-solution project; filename/`AssemblyName` similarity is not evidence. Same-name external, unconfirmed, ambiguous, or incomplete-capture references remain unchanged. The selected project must already have its resolved output on disk. Prepared generations are content-hashed (`v2-main-only`) and reused on document edit without recopying analyzer files. After rebuilding a generator with the same assembly identity, restart the MCP process — `reset_workspace` does not unload CLR assemblies. Private helper DLLs are refused (main-only). This response includes a short summary (`N rewritten` plus execution status); per-reference detail goes to the MCP server log.

**Analyzer shadow-copy activation and side effects:** this flag is opt-in; the MCP client and agent do not enable it automatically. Add a conditional rule to the target repository's `AGENTS.md` when that repository uses in-solution analyzer/generator projects with a custom `OutputPath`. Enabling it copies versioned analyzer DLL/PDB files under the OS temp `v2-main-only/` namespace and re-applies the in-memory overlay after workspace document updates, adding some disk I/O and CPU. Cached `false`/omitted does not disable an already-active overlay; use `reset_workspace` then load without the flag. Old generations (including leftover timestamp directories) are not cleaned automatically and can accumulate; `reset_workspace` does not delete them. The real `.csproj` and analyzer build output are not modified; the MCP process locks only the shadow copy. Path resolution and this anti-lock workaround are independent. `find_symbol_definition` by a generator-emitted type name remains a Roslyn limitation, not a failed overlay.

**Behavior:** Host abort mid-load returns **Workspace Load Cancelled (client abort)** (raise MCP tool timeout; not an MSBuild failure). After a successful load, **saved** `.cs` files are watched and applied before symbol search (unsaved buffers ignored). A changed `.csproj`/`.sln`/`Directory.Build.props` skips the next `load_workspace` cache. NuGet restore warnings (`NU1701` TFM compat, audit, prune) and design-time MSBuild warnings (ASP.NET/SDK deprecation such as `IncludeOpenAPIAnalyzers`/`ASPDEPR007`, processor-architecture mismatch, analyzer project without metadata) are shown as warnings and do not fail load even when wrapped as `Msbuild failed when processing the file`; true MSBuild/SDK errors (`error NU|MSB|NETSDK`) still do. Empty `TargetFramework` (`ResolvePackageAssets`) is a dedicated failure — retry with the IDE solution config, or the `.sln` is Bazel-generated and not MSBuild-evaluable. Missing `Compile` target (CrossTargeting outer build) is a dedicated failure — retry with `targetFramework` from the report / `Directory.Build.props`; `dotnet build` can still succeed. VS 2026 / MSBuild 18 BuildHost crash (`XMakeElements`) is a dedicated failure — **not** `MCP_MSBUILD_SDK_MISMATCH`; use MCP 1.0.35+ or load a single SDK-style `.csproj`.
</details>

<details>
<summary><code>reset_workspace</code> — Clears the in-memory MSBuildWorkspace/solution cache. Call before <code>load_workspace</code> again after building the loaded solution on disk (generated files / refs). Saved <code>.cs</code> edits sync without reset (v1.1.0+). Does <strong>not</strong> unload CLR analyzer assemblies; after a same-identity generator rebuild, restart the MCP process.</summary>

**Parameters:** *(none)*
</details>

<details>
<summary><code>get_file_content</code> — Reads the entire file from disk (safe preview for large files).</summary>

**Parameters:**
- `filePath: string`
</details>

<details>
<summary><code>get_class_skeleton</code> — Returns the C# file structure without method bodies (workspace index after applying saved <code>.cs</code>).</summary>

**Parameters:**
- `filePath: string`
</details>

<details>
<summary><code>get_code_skeleton</code> — Parses `.cs` from disk and returns full-file syntax with bodies stripped (signatures + empty blocks); optional directory scan (max 20 files, skips <code>bin</code>/<code>obj</code>/<code>Test</code>/<code>Tests</code> path segments).</summary>

**Parameters:**
- `path: string` — absolute path to one `.cs` file or a folder to scan recursively.

**Note:** Does not require `load_workspace`. For a file already in the loaded solution, `get_class_skeleton` may still be preferable (workspace-consistent view).
**Important:** This tool is file/folder-only (`path`). Do **not** pass `assemblyName` / `typeName`; for external/NuGet assemblies use `decompile_type` / `get_decompiled_class_skeleton`.
</details>

<details>
<summary><code>get_method_body</code> — Returns the source code of a specific method within a named class.</summary>

**Parameters:**
- `filePath: string`
- `className: string`
- `methodName: string`

**Path resolution:** `filePath` may be absolute or workspace-relative. After `load_workspace`, `src/...` is resolved from the loaded `.sln`/`.slnx`/`.csproj` directory.
</details>

<details>
<summary><code>get_diagnostics_for_file</code> — Returns Roslyn compiler diagnostics for a single file (Warning and Error only; saved <code>.cs</code> applied first).</summary>

**Parameters:**
- `filePath: string`

**Output:** each line includes severity, **line**, **column**, diagnostic id (e.g. `CS0246`, `IDE0001`), and message. Use these values with `get_code_fixes`.
</details>

<details>
<summary><code>get_code_fixes</code> — Lists Roslyn CodeAction fixes available for a diagnostic at a specific location.</summary>

**Parameters:**
- `filePath: string`
- `diagnosticId: string` — from `get_diagnostics_for_file` (e.g. `CS0246`)
- `line: int` — 1-based line where the diagnostic starts
- `column: int = 1` — 1-based column (from diagnostics output)

**Returns:** numbered fixes (`fixIndex` 0-based) with titles such as “Add using …”, “Implement interface”, etc. Up to 20 fixes per call.

**Workflow:** `get_diagnostics_for_file` → `get_code_fixes` → `apply_code_fix`. Do not invent fix code when Roslyn provides a fix.
</details>

<details>
<summary><code>apply_code_fix</code> — Applies a Roslyn CodeAction selected from <code>get_code_fixes</code>.</summary>

**Parameters:**
- `filePath: string`
- `diagnosticId: string`
- `fixIndex: int` — index from `get_code_fixes` (0-based)
- `line: int` — same as in `get_code_fixes`
- `column: int = 1`
- `previewOnly: bool = false` — when `true`, returns a diff preview without writing files

**Behavior:** writes changed files through the workspace write boundary and updates the in-memory workspace. An unsupported or stale analyzer-reference diff is rejected before any server write. If only some files are saved, the reply is not full success: it includes Status, Reason, and the known saved paths. Re-run `get_diagnostics_for_file` to verify remaining issues.

**Note:** `fixIndex` is valid only for the same `filePath` / `diagnosticId` / `line` / `column` pair in the same session (file must not change between `get_code_fixes` and `apply_code_fix`).
</details>

<details>
<summary><code>explore_assembly</code> — Decompiles a referenced external assembly (NuGet/third-party DLL) via ILSpy and returns namespaces with visible top-level classes/interfaces.</summary>

**Parameters:**
- `assemblyName: string?` — simple name without `.dll` (resolved via loaded workspace; call `load_workspace` first).
- `assemblyPath: string?` — absolute path to a `.dll` (e.g. NuGet cache). Provide **one** of the two.
</details>

<details>
<summary><code>decompile_type</code> — Decompiles one specific type from a referenced assembly and returns C# source code (circuit breaker: max 500 lines).</summary>

**Parameters:**
- `assemblyName: string?` / `assemblyPath: string?` — same as `explore_assembly` (one required).
- `fullTypeName: string` (e.g. `Microsoft.AspNetCore.Mvc.ControllerBase`)

**Behavior note:** if decompiled output exceeds 500 lines, the tool returns an error message instructing to use `get_decompiled_class_skeleton` and `get_decompiled_method_body`.
</details>

<details>
<summary><code>get_decompiled_class_skeleton</code> — Returns a signatures-only skeleton (public/protected fields/properties/methods) for one type from a referenced assembly.</summary>

**Parameters:**
- `assemblyName: string?` / `assemblyPath: string?` — same as `explore_assembly` (one required).
- `fullTypeName: string` (e.g. `Microsoft.AspNetCore.Mvc.ControllerBase`)
</details>

<details>
<summary><code>get_decompiled_method_body</code> — Decompiles only matching method overload(s) from one type in a referenced assembly.</summary>

**Parameters:**
- `assemblyName: string?` / `assemblyPath: string?` — same as `explore_assembly` (one required).
- `fullTypeName: string` (e.g. `Microsoft.AspNetCore.Mvc.ControllerBase`)
- `methodName: string`
</details>

<details>
<summary><code>find_symbol_references</code> — Finds usages of a class/interface/method across the solution.</summary>

**Parameters:**
- `filePath: string`
- `symbolName: string`
</details>

<details>
<summary><code>find_symbol_definition</code> — Semantic lookup: where a type or member is declared (file path + line) in the loaded solution.</summary>

**Parameters:**
- `symbolName: string` — class, interface, struct, enum, or member identifier (e.g. `IRunCommand`).

**Model guidance:** after `load_workspace`, use this for “where is X **declared**?” — do **not** answer that with plain-text search or invent a generic tool named `search`. For free-text matches across files, use your client’s built-in **`grep`** tool (not `bash`/`PowerShell` grep). This tool avoids `bin/`/`obj/` and uses Roslyn. **Saved** `.cs` (IDE/git) are applied before search; unsaved buffers are ignored — no `reset_workspace` for ordinary saves.
</details>

<details>
<summary><code>find_usages</code> — Solution-wide references for a declared name: file, line, and source line text (capped at 30 locations).</summary>

**Parameters:**
- `symbolName: string` — declared name of the type or member (e.g. `Guard`, `Format`).

**Behavior:** Requires `load_workspace`. Applies saved `.cs` from disk first. Resolves declarations via Roslyn; if several symbols share the name, one primary symbol is chosen (types preferred over methods, then stable ordering). When you already know the declaring file, `find_symbol_references` may be more precise.
</details>

<details>
<summary><code>find_implementations</code> — Find classes implementing an interface or derived from a base type.</summary>

**Parameters:**
- `symbolName: string` — interface or base class name (e.g. `IRepository`, `BaseController`)
- `transitive: bool = true` — when `true`, includes indirect implementations / derived types in the hierarchy

**Behavior:** Requires `load_workspace`. Applies saved `.cs` from disk first. For **interfaces**, uses Roslyn `FindImplementationsAsync`; for **classes/structs**, uses `FindDerivedClassesAsync`. Returns each matching type with file path and line (capped at 50). Do not use text search or `find_usages` for “who implements X?” / “what inherits from Y?”.

**Model guidance:** after `load_workspace`, use this instead of grep or analyzing usages when you need the OOP hierarchy.
</details>

<details>
<summary><code>get_call_graph</code> — Callers and callees for a method (workspace; saved <code>.cs</code> applied first).</summary>

**Parameters:**
- `filePath: string` — `.cs` file containing the method
- `className: string`
- `methodName: string`
- `maxNodes: int = 25` — cap per callers/callees list
- `includeExternalCallees: bool = false` — include BCL / external calls

**Behavior:** Requires `load_workspace`. Uses `SymbolFinder.FindCallersAsync` and invocation analysis inside the method body. Use for bug investigation instead of loading many bodies via `get_method_body`.

</details>

### File Editing

<details>
<summary><code>update_file_content</code> — Completely overwrites a file. Creates missing directories automatically. New <code>.cs</code> under a loaded project is indexed on the next symbol search.</summary>

**Parameters:**
- `filePath: string`
- `content: string`
</details>

<details>
<summary><code>add_using</code> — Add a using directive via Roslyn AST.</summary>

**Parameters:**
- `filePath: string`
- `namespaceName: string` — e.g. `System.Linq`

Inserts and formats the directive. Requires `load_workspace`. Prefer over `apply_patch` for imports.

</details>

<details>
<summary><code>add_method_to_class</code> — Insert a method into a class via Roslyn AST.</summary>

**Parameters:**
- `filePath: string`
- `className: string` — top-level class name
- `methodSource: string` — full method declaration (modifiers, signature, body)

Parses C# syntax, inserts with DocumentEditor, formats the file. Prefer over `apply_patch` for new methods.

</details>

<details>
<summary><code>update_method_body</code> — Replace a method body via Roslyn AST.</summary>

**Parameters:**
- `filePath: string` — absolute path to `.cs` file
- `className: string` — class containing the method (top-level or nested)
- `methodName: string`
- `newBody: string` — statements only, or a full `{ ... }` block
- `parameterTypes: string[]?` — e.g. `["string", "int"]` to disambiguate overloads; **required** when multiple overloads exist

**Behavior:** Finds `MethodDeclarationSyntax`, parses `newBody` into a `BlockSyntax`, replaces block or expression-bodied body, validates syntax errors before write, formats the file. Use with `get_method_body` — prefer over `apply_patch` for body-only edits.

</details>

<details>
<summary><code>remove_using</code> — Remove a using directive via Roslyn AST.</summary>

**Parameters:** `filePath`, `namespaceName`

</details>

<details>
<summary><code>organize_usings</code> — Sort and optionally remove unused usings.</summary>

**Parameters:** `filePath`, `removeUnused: bool = true`

</details>

<details>
<summary><code>add_property_to_class</code> — Insert a property declaration via Roslyn AST.</summary>

**Parameters:** `filePath`, `className`, `propertySource`

</details>

<details>
<summary><code>add_field_to_class</code> — Insert a field declaration via Roslyn AST.</summary>

**Parameters:** `filePath`, `className`, `fieldSource`

</details>

<details>
<summary><code>remove_member</code> — Remove a class member by name.</summary>

**Parameters:** `filePath`, `className`, `memberName`

</details>

<details>
<summary><code>add_type_to_class_bases</code> — Add base class or interface to class base list.</summary>

**Parameters:** `filePath`, `className`, `typeName`

</details>

<details>
<summary><code>implement_interface</code> — Add interface to class and generate NotImplemented stubs.</summary>

**Parameters:** `filePath`, `className`, `interfaceName`

</details>

<details>
<summary><code>apply_patch</code> — Surgical find-and-replace tool.</summary>

**Parameters:**
- `filePath: string`
- `oldString: string`
- `newString: string`
- `replaceAll: bool = false`

**Behavior:** `replaceAll` continues the search after each inserted `newString` (does not rescan the replacement). Safe when `newString` contains `oldString`. Logs start/match/write with elapsed ms.
</details>

### Build / Test / CLI

<details>
<summary><code>run_dotnet_build</code> — Runs dotnet build and returns a compact diagnostic summary.</summary>

**Parameters:**
- `workspacePath: string` — must be an existing **`.csproj`, `.sln`, or `.slnx` file** (not a directory).
- `configuration: string? = null` — optional `-p:Configuration=` (e.g. `Sit-Debug`, `Dit-Debug`). Omit to inherit `load_workspace` configuration.
- `noIncremental: bool = true` — pass `--no-incremental` on every build step (default). Set `false` only if you explicitly accept MSBuild up-to-date caching.
- `platform: string? = null` — optional `-p:Platform=` (e.g. `x64`). Omit to inherit `load_workspace` platform.
- `projectName: string? = null` — optional. When set, `workspacePath` must be a `.sln`/`.slnx`. Builds that project via its solution-folder MSBuild target (`-t:"Folder\Project"`). Match display name, file name, or virtual path.

**Behavior:** Inherits full process env, then pins SDK via `MSBUILD_EXE_PATH`, `MSBuildSDKsPath`, `DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR` / `SDKS_VER` / `CLI_DIR`, `DOTNET_ROOT`. On MSBuild path mismatch → **`error MCP_MSBUILD_SDK_MISMATCH`** and `dotnet exec …/10.x/MSBuild.dll /restore`. Escalation: minimal build → pinned restore → restore (detailed if empty) → build normal → build detailed. **Effective exit** = last `dotnet build` step (not restore). Metadata reports `Configuration` / `Platform` / `ProjectName` / `SolutionTarget` / `BuildArgs` / `NoIncremental`. Session `buildArgs` from `load_workspace` are appended to build steps only (not restore). **Key lines** include task `-- FAILED` with project context. No in-process result cache — “cached” greens were MSBuild incremental or exit overwrite.

</details>

<details>
<summary><code>run_dotnet_test</code> — Runs dotnet test with condensed failure details.</summary>

**Parameters:**
- `workspacePath: string`
- `timeoutSeconds: int = 300` — process timeout; `0` disables (not recommended). Raise for long integration tests (e.g. 900/1800).
- `noBuild: bool = false` — pass `--no-build` (after a successful `run_dotnet_build`).
- `noRestore: bool = false` — pass `--no-restore`.
- `configuration: string? = null` — optional `dotnet test -c` (e.g. `Sit-Debug`). Omit to inherit `load_workspace` (use the same value as `run_dotnet_build` when `noBuild=true`).
- `platform: string? = null` — optional `-p:Platform=`. Omit to inherit `load_workspace`.
- `binariesPath: string? = null` — optional bin directory containing `{AssemblyName}.dll`. Requires a loaded `.sln`/`.slnx` and a `.csproj` `workspacePath`. When `noBuild=false`, builds that project via the loaded solution `-t` first, then `dotnet test` on the DLL. When `noBuild=true`, the DLL must already exist in that directory.

**Behavior:** When `noBuild=false` and `binariesPath` is omitted, runs incremental `dotnet build` first (same `-c` / platform / session `buildArgs` from `load_workspace`; not the `run_dotnet_build` probe), then `dotnet test --no-build --no-restore`. With `binariesPath`, the compile is `dotnet build <sln> -t` and the test target is the DLL. Parser sees only the test process. `--logger "console;verbosity=normal"`. Summary from `Passed!`, `Test Run Successful` + `Total tests`/`Passed:` (Failed defaults to 0), or `.slnx` fail-only `Total tests` + `Failed:` (Passed inferred as Total − Failed − Skipped), or per-test `  Passed FQN [ms]` / `[1 s]` / `[1 m 28 s]`. MSBuild/prune noise ignored; duplicate NU audit lines deduped. Exit 0 without any summary marker → **`Status: partial`** + last 2KB. `run_specific_test` checks filter matched a test FQN. `timeoutSeconds` is the combined budget for build+test. A DLL test also needs `.runtimeconfig.json` / `.deps.json` beside the assembly.

</details>

<details>
<summary><code>run_specific_test</code> — Run dotnet test filtered to one class and/or method.</summary>

**Parameters:**
- `workspacePath: string`
- `className: string?` — e.g. `UserServiceTests` (simple or fully qualified)
- `methodName: string?` — e.g. `CreateUser_WhenValid_ReturnsOk`
- `timeoutSeconds: int = 300` — same as `run_dotnet_test`.
- `noBuild: bool = false` — pass `--no-build`.
- `noRestore: bool = false` — pass `--no-restore`.
- `configuration: string? = null` — optional `dotnet test -c` (same as `run_dotnet_test`).
- `platform: string? = null` — optional `-p:Platform=`. Omit to inherit `load_workspace`.
- `binariesPath: string? = null` — same as `run_dotnet_test`: bin directory with `{AssemblyName}.dll`; loaded `.sln`/`.slnx` plus `.csproj` `workspacePath`. `noBuild=false` builds via the solution `-t` first.

At least one of `className` or `methodName` is required. The tool builds a VSTest-safe `--filter` internally (`FullyQualifiedName~…`, no method `()`, no extra leading `.` on dotted names). After `load_workspace`, Roslyn resolves the type/method FQN when possible.

**Model guidance:** use this for TDD red/green loops — do not run the full suite. For a raw VSTest `--filter` (`TestCategory=Smoke`, `FullyQualifiedName~…`) use `run_test_by_filter`. Prefer `className` + short `methodName`. After build, prefer `noBuild=true` for faster filtered re-runs. Same pre-test build split as `run_dotnet_test` when `noBuild=false`. Projects that must be built inside their `.sln` should pass `binariesPath` and a `.csproj` `workspacePath`.

</details>

<details>
<summary><code>run_test_by_filter</code> — Run dotnet test with a raw VSTest <code>--filter</code>.</summary>

**Parameters:**
- `workspacePath: string` — `.csproj`, `.sln`, `.slnx`, or test project directory (same as `run_dotnet_test`).
- `filter: string` — passed through as `--filter` (e.g. `FullyQualifiedName~MyClass`, `FullyQualifiedName~CreateUser`, `TestCategory=Smoke`). Empty is an error. Do not put method `()`.
- `timeoutSeconds: int = 300` — same as `run_dotnet_test`.
- `noBuild: bool = true` — skip rebuild (default **true**, unlike the other test tools).
- `noRestore: bool = false` — `--no-restore`.
- `configuration: string? = null` — optional `dotnet test -c`. Omit to inherit `load_workspace`.
- `platform: string? = null` — optional `-p:Platform=`. Omit to inherit `load_workspace`.
- `binariesPath: string? = null` — optional bin directory containing the test DLL. Requires a loaded `.sln`/`.slnx` and a `.csproj` `workspacePath`. Runs `{AssemblyName}.dll` from that directory. When `noBuild=false`, builds the project via the loaded solution `-t` first; when `noBuild=true`, the DLL must already exist.

**Behavior:** Same VSTest parser and pre-test build split as `run_dotnet_test` when `binariesPath` is omitted and `noBuild=false`. Does **not** check that the filter needle appears in a test FQN (category filters would false-positive). Prefer `run_specific_test` for one class or method.

</details>

<details>
<summary><code>get_test_list</code> — List test methods in loaded solution (JSON; saved <code>.cs</code> applied first).</summary>

**Parameters:**
- `maxResults: int = 200` (clamped 1–500)
- `projectName: string? = null` — optional. Limit discovery to one loaded Roslyn project (case-insensitive exact match on display name, file name without extension, or assembly name). Missing or ambiguous names return the project list, not empty JSON.
- `nameContains: string? = null` — optional. Case-insensitive substring of the VSTest FQN (`Namespace.Class.Method`), class name, or method name.

**Behavior:** Detects Fact/Theory/TestMethod/etc. Requires `load_workspace`. Applies saved `.cs` from disk first. Filters run **before** `maxResults`. Each item includes `projectName`. Unfiltered `count: 0` includes agent guidance when the wrong `.csproj` is loaded; a filtered `count: 0` means no match (drop or relax filters first).

</details>

<details>
<summary><code>generate_test_method_stub</code> — Insert a test method stub into a test class.</summary>

**Parameters:** `filePath`, `className`, `methodName`, `testFramework: string?` — `xunit` (default), `nunit`, `mstest`

</details>

<details>
<summary><code>list_nuget_packages</code> — Installed NuGet packages as JSON per project.</summary>

**Parameters:**
- `workspacePath: string` — `.sln`, `.slnx`, `.csproj`, или каталог
- `includeTransitive: bool` — default `true`
- `includeOutdated: bool` — default `false` (adds `--outdated`)
- `includeVulnerable: bool` — default `false` (adds `--vulnerable`)

**Model guidance:** inspect dependency tree before editing `.csproj`; do not use `execute_dotnet_command` for package listing.

</details>

<details>
<summary><code>list_outdated_packages</code> — Outdated NuGet packages as JSON.</summary>

**Parameters:** `workspacePath: string` — shortcut for `list_nuget_packages` with `includeOutdated=true`, no transitive.

</details>

<details>
<summary><code>add_package_reference</code> — Add PackageReference to .csproj.</summary>

**Parameters:** `projectPath: string`, `packageId: string`, `version: string?`

Verify id/version with `search_nuget_registry` first. Clears workspace cache — call `load_workspace` after.

</details>

<details>
<summary><code>remove_package_reference</code> — Remove PackageReference from .csproj.</summary>

**Parameters:** `projectPath: string`, `packageId: string`

</details>

<details>
<summary><code>search_nuget_registry</code> — Search NuGet feeds for package id and latest stable version.</summary>

**Parameters:**
- `query: string` — package id or search term
- `exactMatch: bool` — default `true` (exact id lookup)
- `maxResults: int` — when `exactMatch=false`, default 10

**Model guidance:** verify package exists before `dotnet add package`; never hallucinate package names or versions.

</details>

<details>
<summary><code>execute_dotnet_command</code> — Runs dotnet {command} in the target directory.</summary>

**Parameters:**
- `command: string` (e.g., `test`, `add package Moq`)
- `workingDirectory: string?` (defaults to current directory)
</details>

<details>
<summary><code>run_format</code> — Runs dotnet format (apply or verify-only). Applied <code>.cs</code> are picked up by the next symbol search without reset.</summary>

**Parameters:**
- `workspacePath: string`
- `verifyOnly: bool = false`
</details>

### Filesystem Utility & Search

<details>
<summary><code>list_directory_tree</code> — Builds a directory tree visualization (ignores bin, obj, .git).</summary>

**Parameters:**
- `directoryPath: string`
- `maxDepth: int = 2`
</details>

<details>
<summary><code>search_code</code> — Context-friendly ripgrep alternative (plain text or regex).</summary>

**Parameters:**
- `pattern: string`
- `directoryPath: string? = null`
- `includeExtensions: string? = ".cs"` — comma/semicolon list (`.cs,.csproj,.json`), or `*` for all files.
- `useRegex: bool = false`
- `caseSensitive: bool = false` — default case-insensitive; for leftover branding checks set `true`.
- `maxResults: int = 50`
- `maxScanSeconds: int = 20`

**Agent note:** if your client exposes a built-in **`grep`** tool, prefer that for ad-hoc text search (never shell-driven grep). Use this MCP tool when you need search inside the workspace from the Roslyn MCP process.
</details>

<details>
<summary><code>read_file_range</code> — Reads specific lines and prefixes them with original line numbers.</summary>

**Parameters:**
- `filePath: string`
- `startLine: int` (1-based)
- `lineCount: int`
</details>

<details>
<summary><code>read_log_tail</code> — Context-safe log reader with optional keyword filtering.</summary>

**Parameters:**
- `filePath: string? = null` — omit to read the latest MCP server log (`logs/mcp-*.log`, same as `tail_tool_log`).
- `lastNLines: int = 200`
- `filterKeyword: string? = null` — e.g. `Failure`, `ERR`, `NU1903`.

**Tip:** Prefer `tail_tool_log` for MCP diagnostics; use `filterKeyword` to avoid dumping entire logs.
</details>

<details>
<summary><code>tail_tool_log</code> — Reads the latest server log `logs/mcp-*.log`.</summary>

**Parameters:**
- `lastNLines: int = 200`
- `filterKeyword: string? = null`
</details>

<details>
<summary><code>manage_agent_scratchpad</code> — Manages the agent's long-term memory across sessions.</summary>

**Parameters:**
- `action: string` (`read` | `write` | `append` | `clear`)
- `content: string? = null`
</details>

### Roslyn Refactoring / Solution Insights

<details>
<summary><code>rename_symbol</code> — Semantic C# symbol rename via Roslyn with preview capability.</summary>

**Parameters:**
- `filePath: string`
- `symbolName: string`
- `newName: string`
- `scope: string = "project"` — allowed: `project` | `solution`
- `previewOnly: bool = true`

**Scope:** C# symbols only (types/members/namespaces as symbols). For project folder / `.csproj` / solution graph use `rename_project`. For README/rules/URLs use host Grep/edit.

**Behavior:** persist goes through the same write boundary as `apply_code_fix`. Unsupported analyzer-reference diffs are rejected before writes. Partial save reports Status, Reason, and known saved paths — not full success of the rename.
</details>

<details>
<summary><code>rename_project</code> — Rename SDK-style project directory + `.csproj` and fix MSBuild graph.</summary>

**Parameters:**
- `projectPath: string` — path to `.csproj`
- `newProjectName: string` — single path segment (e.g. `DupFinder.Core`)
- `dryRun: bool = true`
- `searchRoot: string? = null` — where to find sibling projects / `.sln` / `.slnx`

**Behavior:** Moves identically named project folder + renames `.csproj`; updates `AssemblyName`/`RootNamespace` only when they equal the old project name; rewrites `ProjectReference` paths; updates `.sln` and `.slnx` entries. **SDK-style only.** Unsupported layouts hard-fail (no silent partial write). Does **not** rename C# namespaces/types, Docker, launchSettings, CI, or docs. After apply: `load_workspace` → `run_dotnet_build`.
</details>

<details>
<summary><code>extract_interface</code> — Extract a public interface from a class.</summary>

**Parameters:**
- `filePath: string`
- `className: string`
- `interfaceName: string?` — default `I` + className
- `createNewFile: bool = true` — write `{InterfaceName}.cs` in the same folder
- `previewOnly: bool = false`

**Behavior:** Collects public instance methods, properties, and events declared on the class; generates interface signatures; adds `: IInterface` to the class. Supports block and file-scoped namespaces. Partial classes are rejected.
</details>

<details>
<summary><code>move_type_to_new_file</code> — Split top-level types into one-type-per-file.</summary>

**Parameters:**
- `filePath: string`
- `typeName: string?` — omit to move all types whose name does not match the file name
- `previewOnly: bool = false`

**Behavior:** Creates `{TypeName}.cs` next to the source file (SDK-style projects pick up new files automatically), copies usings/namespace, removes the type from the original file. Partial types are rejected.
</details>

<details>
<summary><code>list_projects</code> — Shows projects in the workspace (Name, TFM, Output, Refs).</summary>

**Parameters:**
- `workspacePath: string? = null`
</details>

<details>
<summary><code>get_project_graph</code> — Builds a project-to-project dependency graph.</summary>

**Parameters:**
- `workspacePath: string? = null`
</details>

### Server lifecycle

<details>
<summary><code>get_mcp_server_info</code> — Binary path, tool count, logs, workspace state.</summary>

**Parameters:** *(none)*

Use after `dotnet publish` to verify the MCP host picked up the new binary (expect **v1.3.12** and **63** tools on `full`, or **19** on `lite`).

</details>

<details>
<summary><code>list_tool_groups</code> — Lists groups with purpose, active state, and member names.</summary>

**Parameters:** *(none)*

Does not enable groups. Use `enable_tool_group` or restart with `ROSLYN_MCP_TOOL_GROUPS`. Markdown output; `tools/list` remains JSON Schema.

</details>

<details>
<summary><code>get_tool_help</code> — Markdown help for one public tool (kind, group, live parameters).</summary>

**Parameters:**
- `toolName: string` — exact public tool name (e.g. `find_usages`). Unknown names return close matches.

</details>

<details>
<summary><code>enable_tool_group</code> — Adds every missing tool in one catalog group to this session.</summary>

**Parameters:**
- `group: string` — exact group name, case-insensitive (`files`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations`).

Idempotent. Profile `full` is a no-op. Sends `tools/list_changed` only when tools were added. Clients that ignore the notification: restart with `ROSLYN_MCP_TOOL_GROUPS=<group>`. No disable in this release.

</details>

<details>
<summary><code>stop_mcp_server</code> — Stops this MCP host process after returning (for rebuilding the server binary; restart MCP in the IDE). Not for refreshing saved source — that syncs automatically.</summary>

**Parameters:** *(none)*

</details>

### Prompts

<details>
<summary><code>RefactoringAssistantPrompt</code> — Short system-style instructions for C# refactoring workflows.</summary>

**Parameters:**
- `focus: string? = null`
</details>

## License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
---

## <a name="russian-version"></a>
 🇷🇺 Описание на русском

[🇬🇧 Back to English (Main)](#roslynmcpserver)

RoslynMcpServer предоставляет AI-агенту compiler-aware инструменты для C#-решений: семантическую навигацию, диагностику и code fixes, рефакторинг, анализ зависимостей и компактный вывод `dotnet build/test/run`.

В отличие от файлового MCP, сервер загружает `.sln`, `.slnx` или `.csproj` через Roslyn/MSBuild. Агент работает с символами и контекстом проектов, а не только предполагает структуру по тексту.

## MCP и агент за одну минуту

- **MCP-клиент** — Cursor, OpenCode или другой совместимый хост. Запускает сервер и предоставляет его tools модели.
- **AI-агент** — модель и orchestration клиента. Именно агент выбирает tools; он не является частью этого репозитория.
- **RoslynMcpServer** — локальный stdio-процесс. У него нет собственного чата и автономного цикла.
- **`AGENTS.md`** — правила репозитория для агента. MCP-конфиг подключает tools, а этот файл определяет, когда ими пользоваться.

Типовой поток:

`запрос разработчика → агент → MCP tool → Roslyn/MSBuild/ILSpy/dotnet → компактный результат → агент`

Сервер ориентирован на C#. Он умеет читать другие файлы и запускать отдельные CLI-операции, но не выполняет семантический анализ Python. Компоненты, состояние и обязательные ограничения описаны в [Architecture and constraints](docs/ARCHITECTURE.md). План (ещё не в runtime) кеша загрузки больших решений: [docs/workspace-load-cache/](docs/workspace-load-cache/README.md).

## Основные возможности

- Поиск объявлений, использований, реализаций и связей вызовов через Roslyn.
- Диагностика, CodeAction, AST-правки и семантический rename.
- Анализ подключённых или явно заданных сборок через ILSpy.
- Build, test, run, format и NuGet с выравниванием SDK, таймаутами и структурированным выводом.
- Полный каталог `full` и экономный `lite` с группами tools.

## Граница безопасности

Сервер читает и пишет файлы и запускает дочерние процессы `dotnet`/Git с правами хост-процесса. Это **не sandbox**; не все операции ограничены каталогом загруженного workspace.

- Используйте Git и проверяйте изменения агента.
- Не запускайте сервер от администратора/root.
- Не передавайте PAT, пароли и connection strings в prompts или аргументах tools.
- Параметры входящих MCP-вызовов по умолчанию логируются; проверьте раздел «Логи» для чувствительных репозиториев.

## Требования

- MCP-клиент с поддержкой локального stdio-сервера.
- .NET SDK, совместимый с анализируемым solution; учитывайте его `global.json`.
- **.NET 10 SDK** для сборки самого RoslynMcpServer из исходников.
- Git настоятельно рекомендуется для review и отката изменений.

## Первый запуск

Используйте **published executable**, а не `dotnet run`. Publish self-contained и ReadyToRun, но намеренно не single-file: Roslyn, MSBuild, analyzers и BuildHost загружают сборки динамически.

### 1. Собрать сервер

```bash
dotnet publish RoslynMcpServer.csproj -c Release -r win-x64
```

Для Windows x64 бинарник будет здесь:

- `bin/Release/net10.0/win-x64/publish/RoslynMcpServer.exe`

Для Linux/macOS используется тот же относительный layout с выбранным RID.

### 2. Зарегистрировать локальный stdio-сервер

В Cursor откройте **Cursor Settings → Tools & MCP** или создайте `.cursor/mcp.json` в целевом проекте. В Windows внутри JSON удобно использовать `/`:

```json
{
  "mcpServers": {
    "roslyn-mcp-server": {
      "command": "C:/absolute/path/to/RoslynMcpServer.exe",
      "env": {
        "ROSLYN_MCP_WORKSPACE": "C:/absolute/path/to/YourApp"
      }
    }
  }
}
```

`ROSLYN_MCP_WORKSPACE` необязателен, но рекомендуется: укажите корень целевого репозитория, особенно если там есть `global.json`. Для локальных моделей настройте `ROSLYN_MCP_TOOL_PROFILE` / `ROSLYN_MCP_TOOL_GROUPS` из раздела «Профили инструментов».

После изменения конфигурации перезапустите или reload MCP servers.

### 3. Добавить правила агента в целевой репозиторий

Скопируйте или влейте [`AGENTS.md.sample`](AGENTS.md.sample) в репозиторий приложения как `AGENTS.md`. Специфичные для приложения entry points и команды добавьте туда отдельно. Это не MCP-конфиг: без policy tools могут быть подключены, но агент всё равно выберет обычный текстовый поиск или shell-команды.

### 4. Проверить первую сессию

Попросите агента:

> Используй Roslyn MCP. Вызови `get_mcp_server_info`, затем `load_workspace` с абсолютным путём к solution этого репозитория. Перечисли загруженные проекты и состояние workspace. Файлы не изменяй.

Исправная сессия покажет путь/версию бинарника, активный профиль tools, состояние SDK/restore и хотя бы один проект. Затем проверьте семантический запрос: «найди объявление и использования `SolutionManager`».

Если tools не видны, вызовите `list_tool_groups`. Если не загрузился workspace, следуйте его диагностике, а не переключайтесь на shell `dotnet`.

### Установщик OpenCode

После `dotnet publish` в каталоге publish лежат [`install2opencode.ps1`](install2opencode.ps1) и [`AGENTS.md.sample`](AGENTS.md.sample) рядом с `RoslynMcpServer.exe`.

Из корня **целевого проекта** (вашего приложения, не этого репозитория):

```powershell
cd D:\Devel\YourApp
& "D:\Devel\RoslynMcpServer\bin\Release\net10.0\win-x64\publish\install2opencode.ps1"
```

Скрипт:

- создаёт или обновляет **`opencode.json`**, регистрируя `RoslynMcpServer` как локальный MCP-сервер (`type: local`, `enabled: true`, **`timeout`: `600000`** мс = 10 минут);
- создаёт или дополняет **`AGENTS.md`**: если правил Roslyn MCP ещё нет, подмешивает текст из `AGENTS.md.sample` (новый файл или append к существующему).

**Таймаут MCP-хоста OpenCode:** по умолчанию OpenCode обрывает MCP `tools/call` примерно через **60 секунд**. Это **не** аргумент тула `timeoutSeconds` у `run_dotnet_test` / `run_dotnet_run`. Без увеличения host timeout длинные build/test падают с `McpError: MCP error -32001: Request timed out` (~60 с), даже если передать `timeoutSeconds: 900`. Укажите `"timeout": 600000` (или больше) в записи сервера — см. [`opencode.json.sample`](opencode.json.sample). `install2opencode.ps1` проставляет это сам. Если старая сборка OpenCode игнорирует per-server `timeout`, добавьте также `"experimental": { "mcp_timeout": 600000 }`.

Пример блока сервера:

```json
"roslyn-mcp-server": {
  "type": "local",
  "command": ["C:/path/to/RoslynMcpServer.exe"],
  "timeout": 600000,
  "enabled": true
}
```

| Параметр | Описание |
| --- | --- |
| `-BinaryPath` | Необязательный абсолютный путь к `RoslynMcpServer.exe`. Если не указан — берётся бинарь рядом со скриптом. |
| `-ProjectPath` | Необязательный корень проекта. По умолчанию — текущая рабочая директория. |

После установки перезапустите OpenCode или перезагрузите MCP-серверы.

## Профили инструментов

По умолчанию **`full`** (все публичные тулы). **`lite`** стартует с 19 core-тулов. Дополнительные группы — при старте процесса или, если клиент обрабатывает `notifications/tools/list_changed`, во время сессии.

| Переменная | Значения | Эффект |
| --- | --- | --- |
| `ROSLYN_MCP_TOOL_PROFILE` | `full` (по умолчанию) или `lite` | Стартовый каталог. Пустое/не задано = `full`. |
| `ROSLYN_MCP_TOOL_GROUPS` | `core`, `files`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations` (через запятую, без учёта регистра) | Добавляет группы в **`lite` до первого `tools/list`**. В `full` имена только записываются. Неизвестное имя валит старт. `core` в lite уже есть. |

Пример: `ROSLYN_MCP_TOOL_GROUPS=decompile,nuget`

Примеры OpenCode: [`opencode.json.sample`](opencode.json.sample). Включайте только один сервер.

| Группа | Назначение | Тулов |
| --- | --- | ---: |
| `core` | workspace, навигация, build, test, help | 19 (lite по умолчанию) |
| `files` | чтение/поиск/патч с диска | 7 |
| `editing` | AST, code fixes, format, rename | 17 |
| `decompile` | сторонние сборки | 4 |
| `nuget` | пакеты: list/audit/search/add/remove | 6 |
| `project` | граф solution и rename проекта | 3 |
| `runtime` | `run`, список тестов, сырой `dotnet` | 3 |
| `operations` | логи, scratchpad, stop | 4 |

`list_tool_groups` / `get_tool_help` / `enable_tool_group` — Markdown-справка и runtime-включение. `tools/list` остаётся JSON Schema. Если клиент не обновляет список после `tools/list_changed`, перезапустите с `ROSLYN_MCP_TOOL_GROUPS=<group>`. Замеры minified UTF-8: full **63 / 44 165**; lite **19 / 16 579**.

## История agent-tools по версиям

См. английский раздел [Agent tools by version](#agent-tools-by-version) (v1.0.13–v1.3.12). Правила агента — [`AGENTS.md.sample`](AGENTS.md.sample).

## Cursor: как заставить агента реально вызывать tools

Положите **session policy** в app-репозиторий, иначе клиент часто игнорирует MCP-тулы.

**Канонический файл:** [`AGENTS.md.sample`](AGENTS.md.sample) — скопируйте в приложение как `AGENTS.md` (или влейте во фрагменты `.cursor/rules`). Текст на EN (LLM лучше следуют английским императивам). Это только политика (когда / MCP vs host / запреты); параметры тулов — в Description и в **Reference: MCP Tools** ниже (и в англ. секции).

**OpenCode:** `install2opencode.ps1` (см. блок **OpenCode** выше) сам создаёт или дополняет `AGENTS.md`.

**Maintainers:** при изменении тулов или agent-visible поведения обновляйте **вместе** `AGENTS.md.sample` и этот README («Agent tools by version» + Reference). Полный текст AGENTS в README не дублировать — только ссылка на sample.

Кратко для модели:

- проверить сервер через `get_mcp_server_info`; отсутствующие `lite`-tools — через `list_tool_groups` / `enable_tool_group`
- после `load_workspace` объявления C# — MCP `find_symbol_definition` / `find_usages`, не текстовый поиск и не выдуманный `search`
- нет target `Compile` при load — повторить с `targetFramework` (inner TFM); это не SDK mismatch
- VS 2026 BuildHost / `XMakeElements` — это не SDK mismatch; нужен MCP 1.0.35+ или один SDK-style `.csproj`
- текст по файлам — host Grep IDE, не shell grep
- сборка/тесты — только MCP `run_dotnet_build` / `run_dotnet_test`
- запись файлов — IDE: нативные правки; headless: MCP `apply_patch` / AST
- полный текст политики — в `AGENTS.md.sample`

## Логи
- Основной лог: `logs/mcp-*.log` (относительно `AppContext.BaseDirectory`).
- Включено логирование входящих JSON-RPC сообщений (`MCP_LOG_INCOMING_RPC`, `MCP_LOG_INCOMING_RPC_MAX_CHARS`).
- **Ответы tools:** в лог пишется однострочная сводка и отдельные строки warning/error (без полного дублирования ответа MCP). `ROSLYN_MCP_LOG_TOOL_OUTPUT=full` — полный текст ответов tools.
- Переменные: `ROSLYN_MCP_WORKSPACE` (корень репо для MSBuild/SDK), `ROSLYN_MCP_TOOL_PROFILE` (`full`/`lite`), `ROSLYN_MCP_TOOL_GROUPS` (`core`, `files`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations`; см. **Профили инструментов**), `ROSLYN_MCP_LOG_TOOL_OUTPUT=full`.

## Reference: MCP Tools

**Имена параметров в JSON:**
- `filePath` — один файл (чтение/правка/диагностика/логи).
- `directoryPath` — корневая папка (`list_directory_tree`, опционально корень для `search_code`).
- `includeExtensions` — опциональный фильтр расширений для `search_code` (по умолчанию `.cs`; `*` = все файлы).
- `caseSensitive` — опционально для `search_code` (по умолчанию `false`; для leftover branding — `true`).
- `workspacePath` — `.sln` / `.slnx` / `.csproj` (и иногда каталог): `load_workspace`, `run_dotnet_test`, `run_specific_test`, `run_test_by_filter`, `run_format`, опциональная перезагрузка в `list_projects` / `get_project_graph`. **`run_dotnet_build` принимает только путь к файлу `.csproj`, `.sln` или `.slnx`, не каталог.** Для multi-config solution предпочитайте `.sln`/`.slnx`.
- `symbolName` — идентификатор C# для `find_symbol_definition`, `find_symbol_references`, `find_usages` и `find_implementations` (точное имя; регистр не важен для definition/usages/implementations).
- `diagnosticId` — id компилятора/анализатора из `get_diagnostics_for_file` (например `CS0246`) для `get_code_fixes` / `apply_code_fix`.
- `fixIndex` — индекс (0-based) из `get_code_fixes` для `apply_code_fix`.
- `path` — файл `.cs` или каталог для `get_code_skeleton` (абсолютный путь; с диска, workspace не обязателен).

Зарегистрировано **63** инструмента в профиле `full` (список ниже) и **1** MCP-промпт (`RefactoringAssistantPrompt`). Профиль `lite` стартует с **19** core-тулов; остальные группы — `ROSLYN_MCP_TOOL_GROUPS` или `enable_tool_group`.

### Workspace / Roslyn

<details>
<summary><code>load_workspace</code> — Загружает .sln/.slnx/.csproj в MSBuildWorkspace.</summary>

**Параметры:**
- `workspacePath: string` — файл `.sln`, `.slnx` или `.csproj` (не каталог). Для multi-config — предпочтительно solution.
- `configuration: string?` — опционально MSBuild `Configuration` (например `Sit-Debug`, `kart`). Наследуют build/test, если не передали `-c`.
- `platform: string?` — опционально MSBuild `Platform` (`Any CPU` → `AnyCPU`).
- `targetFramework: string?` — опционально MSBuild `TargetFramework` (например `net10.0`). Нужен, когда в решении `TargetFrameworks` (inner TFM с target `Compile`). Build/test это не наследуют.
- `buildArgs: string?` — опциональные extra-аргументы для последующих `dotnet build` (probe и пребилд тестов). Кэш сессии; пустое значение сбрасывает. Не класть `-c`, `-p:Platform`, `-v`, `--no-incremental`.
- `briefOutput: bool = false` — `true` сворачивает предупреждения MSBuild/NuGet успешного load в счётчики категорий и кодов. По умолчанию полный дамп. Ошибки load всегда печатаются целиком.
- `logProjectOutputDiagnostics: bool = false` — диагностически пишет в MCP-лог resolved output и состояние `AnalyzerReference`; в ответ tool эти данные не попадают. Используйте для in-solution analyzer/generator проектов, если `Directory.Build.props` переопределяет `OutputPath`.
- `shadowCopyInSolutionAnalyzers: bool = false` — заменяет только те `AnalyzerReference`, которые complete provenance snapshot текущей load-сессии exact-join-ит к одному загруженному in-solution проекту. Filename/`AssemblyName` не считаются evidence; external same-name, unconfirmed, ambiguous и incomplete-capture ссылки сохраняются без изменения. Analyzer-проект должен быть предварительно собран. Поколения адресуются по содержимому и повторно применяются при edit без копирования analyzer-файлов. После пересборки генератора с той же assembly identity нужен **новый процесс MCP** (`reset_workspace` не выгружает CLR). Приватные helper DLL отклоняются (main-only). Краткая сводка возвращается в ответе, детали доступны через `tail_tool_log` / `read_log_tail`.

**Активация и побочные эффекты analyzer shadow copy:** флаг opt-in; MCP-клиент и агент автоматически его не включают. Для репозитория с in-solution analyzer/generator и custom `OutputPath` добавьте условное правило в его `AGENTS.md`. При включении versioned DLL/PDB копируются в `v2-main-only/` под OS temp; overlay повторно применяется после обновлений документов. Cached `false`/omitted не выключает уже активный overlay; отключение — `reset_workspace`, затем load без флага. Старые поколения (включая timestamp-каталоги) автоматически не очищаются; reset их не удаляет. Реальные `.csproj` и build output не изменяются; MCP-процесс блокирует только shadow copy. Path resolution и anti-lock независимы. `find_symbol_definition` по имени generated-типа — ограничение Roslyn, не отказ overlay.

**Поведение:** abort хоста mid-load → **Workspace Load Cancelled (client abort)** (поднять MCP timeout; это не ошибка MSBuild). После успешного load **сохранённые** `.cs` вотчатся и подмешиваются в поиск символов (несохранённый буфер игнорируется). Смена `.csproj`/`.sln`/`Directory.Build.props` сбрасывает кэш следующего `load_workspace`. Предупреждения restore (`NU1701` TFM-compat, audit, prune) и design-time MSBuild (deprecation `IncludeOpenAPIAnalyzers`/ASPDEPR007, mismatch архитектуры, analyzer без metadata) не валят load даже в обёртке `Msbuild failed when processing the file`; настоящие ошибки MSBuild/SDK (`error NU|MSB|NETSDK`) — валят. Пустой `TargetFramework` (`ResolvePackageAssets`) — отдельный fail: повторить с IDE-конфигом или это Bazel-generated sln, который MSBuildWorkspace не открывает. Нет target `Compile` (outer CrossTargeting) — отдельный fail: повторить с `targetFramework` из отчёта / `Directory.Build.props`; `dotnet build` при этом может быть зелёным. Падение VS 2026 / MSBuild 18 BuildHost (`XMakeElements`) — отдельный fail, **не** `MCP_MSBUILD_SDK_MISMATCH`; нужен MCP 1.0.35+ или один SDK-style `.csproj`.
</details>

<details>
<summary><code>reset_workspace</code> — Сбрасывает in-memory MSBuildWorkspace и кэш решения. После сборки (generated в <code>obj</code>, refs) вызови снова <code>load_workspace</code>. Сохранённые <code>.cs</code> с v1.1.0 подхватываются без reset. CLR-сборки анализаторов <strong>не</strong> выгружает; после same-identity пересборки генератора нужен новый процесс MCP.</summary>

**Параметры:** *(нет)*
</details>

<details>
<summary><code>get_file_content</code> — Читает файл целиком с диска (возвращает безопасный preview для больших файлов).</summary>

**Параметры:**
- `filePath: string`
</details>

<details>
<summary><code>get_class_skeleton</code> — Возвращает структуру C# файла без тел методов (индекс workspace после saved <code>.cs</code>).</summary>

**Параметры:**
- `filePath: string`
</details>

<details>
<summary><code>get_code_skeleton</code> — Парсит `.cs` с диска и возвращает полный синтаксис файла с вырезанными телами (сигнатуры + пустые блоки); опционально каталог (до 20 файлов, пропуск сегментов пути <code>bin</code>/<code>obj</code>/<code>Test</code>/<code>Tests</code>).</summary>

**Параметры:**
- `path: string` — абсолютный путь к одному файлу `.cs` или к папке для рекурсивного обхода.

**Заметка:** `load_workspace` не требуется. Для файла из уже загруженного solution по-прежнему уместен `get_class_skeleton`.
**Важно:** этот tool работает только с путём к файлу/папке (`path`). Не передавайте сюда `assemblyName` / `typeName`; для внешних/NuGet-сборок используйте `decompile_type` / `get_decompiled_class_skeleton`.
</details>

<details>
<summary><code>get_method_body</code> — Возвращает код одного метода в именованном классе.</summary>

**Параметры:**
- `filePath: string`
- `className: string`
- `methodName: string`

**Разрешение пути:** `filePath` может быть абсолютным или относительным к workspace. После `load_workspace` путь вида `src/...` считается от каталога загруженного `.sln`/`.slnx`/`.csproj`.
</details>

<details>
<summary><code>get_diagnostics_for_file</code> — Возвращает Roslyn-диагностику для одного C# файла (только Warning и Error; сначала saved <code>.cs</code>).</summary>

**Параметры:**
- `filePath: string`

**Вывод:** для каждой диагностики — severity, **строка**, **колонка**, id (например `CS0246`, `IDE0001`) и сообщение. Эти значения нужны для `get_code_fixes`.
</details>

<details>
<summary><code>get_code_fixes</code> — Список доступных Roslyn CodeAction для диагностики в указанной позиции.</summary>

**Параметры:**
- `filePath: string`
- `diagnosticId: string` — из `get_diagnostics_for_file` (например `CS0246`)
- `line: int` — номер строки (1-based), где начинается диагностика
- `column: int = 1` — колонка (1-based, из вывода diagnostics)

**Результат:** пронумерованные фиксы (`fixIndex`, с 0) с заголовками вроде «Add using …», «Implement interface» и т.д. Не более 20 фиксов за вызов.

**Workflow:** `get_diagnostics_for_file` → `get_code_fixes` → `apply_code_fix`. Не придумывайте правку вручную, если Roslyn уже предлагает fix.
</details>

<details>
<summary><code>apply_code_fix</code> — Применяет CodeAction, выбранный из <code>get_code_fixes</code>.</summary>

**Параметры:**
- `filePath: string`
- `diagnosticId: string`
- `fixIndex: int` — индекс из `get_code_fixes` (0-based)
- `line: int` — те же значения, что в `get_code_fixes`
- `column: int = 1`
- `previewOnly: bool = false` — при `true` только diff-превью без записи на диск

**Поведение:** пишет файлы через write boundary и обновляет in-memory workspace. Неподдержанный или stale analyzer-reference diff отклоняется до любой серверной записи. Частичное сохранение — не полный успех: в ответе Status, Reason и известные сохранённые пути. После применения вызовите `get_diagnostics_for_file` для проверки.

**Заметка:** `fixIndex` действителен только для той же пары `filePath` / `diagnosticId` / `line` / `column` в рамках сессии (файл не должен меняться между `get_code_fixes` и `apply_code_fix`).
</details>

<details>
<summary><code>explore_assembly</code> — Декомпилирует подключенную внешнюю сборку (NuGet/сторонний DLL) через ILSpy и возвращает структуру namespaces с видимыми top-level class/interface.</summary>

**Параметры:**
- `assemblyName: string?` — имя без `.dll` (через workspace после `load_workspace`).
- `assemblyPath: string?` — абсолютный путь к `.dll` (например кэш NuGet). Нужен **один** из параметров.
</details>

<details>
<summary><code>decompile_type</code> — Декомпилирует один конкретный тип из подключенной сборки и возвращает C# исходник (circuit breaker: максимум 500 строк).</summary>

**Параметры:**
- `assemblyName: string?` / `assemblyPath: string?` — как у `explore_assembly` (один обязателен).
- `fullTypeName: string` (например `Microsoft.AspNetCore.Mvc.ControllerBase`)

**Поведение:** если результат декомпиляции больше 500 строк, tool возвращает ошибку с рекомендацией использовать `get_decompiled_class_skeleton` и `get_decompiled_method_body`.
</details>

<details>
<summary><code>get_decompiled_class_skeleton</code> — Возвращает только сигнатуры (public/protected fields/properties/methods) для одного типа из подключенной сборки.</summary>

**Параметры:**
- `assemblyName: string?` / `assemblyPath: string?` — как у `explore_assembly` (один обязателен).
- `fullTypeName: string` (например `Microsoft.AspNetCore.Mvc.ControllerBase`)
</details>

<details>
<summary><code>get_decompiled_method_body</code> — Декомпилирует только нужный метод (все совпавшие перегрузки) из одного типа в подключенной сборке.</summary>

**Параметры:**
- `assemblyName: string?` / `assemblyPath: string?` — как у `explore_assembly` (один обязателен).
- `fullTypeName: string` (например `Microsoft.AspNetCore.Mvc.ControllerBase`)
- `methodName: string`
</details>

<details>
<summary><code>find_symbol_references</code> — Ищет использования класса/интерфейса/метода по solution.</summary>

**Параметры:**
- `filePath: string`
- `symbolName: string`
</details>

<details>
<summary><code>find_symbol_definition</code> — Семантический поиск: где объявлен тип или член (путь к файлу и строка) в загруженном solution.</summary>

**Параметры:**
- `symbolName: string` — имя класса, интерфейса, struct, enum или члена (например `IRunCommand`).

**Для модели:** после `load_workspace` для «где **объявлен** X?» используй этот tool — не текстовый поиск и не выдуманный tool вроде `search`. Для произвольного текста по файлам — встроенный **`grep`** среды (IDE), не `bash`/PowerShell с grep. Так не лезем в `bin/`/`obj/` и опираемся на Roslyn. **Сохранённые** `.cs` (IDE/git) подмешиваются до поиска; несохранённый буфер игнорируется — `reset_workspace` для обычных save не нужен.
</details>

<details>
<summary><code>find_usages</code> — Ссылки по всему solution: файл, строка и текст строки исходника (не более 30 вхождений).</summary>

**Параметры:**
- `symbolName: string` — объявленное имя типа или члена (например `Guard`, `Format`).

**Поведение:** нужен `load_workspace`. Сначала подмешиваются сохранённые `.cs` с диска. Поиск объявлений через Roslyn; при нескольких символах с одним именем выбирается один «основной» (типы предпочтительнее методов). Если известен файл объявления, точнее может быть `find_symbol_references`.
</details>

<details>
<summary><code>find_implementations</code> — Классы, реализующие интерфейс, или наследники базового типа.</summary>

**Параметры:**
- `symbolName: string` — имя интерфейса или базового класса (например `IRepository`, `BaseController`)
- `transitive: bool = true` — при `true` включает косвенные реализации / наследников по иерархии

**Поведение:** нужен `load_workspace`. Сначала saved `.cs` с диска. Для **интерфейсов** — Roslyn `FindImplementationsAsync`; для **классов/struct** — `FindDerivedClassesAsync`. Каждый тип с путём к файлу и строкой (не более 50). Не используйте текстовый поиск или `find_usages` для «кто реализует X?» / «кто наследует Y?».

**Для модели:** после `load_workspace` — вместо grep или анализа usages, когда нужна OOP-иерархия.
</details>

<details>
<summary><code>get_call_graph</code> — Кто вызывает метод и что вызывает он (call graph).</summary>

**Параметры:** `filePath`, `className`, `methodName`, `maxNodes: int = 25`, `includeExternalCallees: bool = false`

Нужен `load_workspace`. Для расследования багов — вместо массовой загрузки тел через `get_method_body`.

</details>

### File Editing

<details>
<summary><code>update_file_content</code> — Полная перезапись файла. Автоматически создает отсутствующие каталоги. Новый <code>.cs</code> в загруженном проекте попадает в индекс на следующем поиске символов.</summary>

**Параметры:**
- `filePath: string`
- `content: string`
</details>

<details>
<summary><code>add_using</code> — Добавить using через Roslyn AST.</summary>

**Параметры:**
- `filePath: string`
- `namespaceName: string` — например `System.Linq`

Требует `load_workspace`. Для импортов — вместо `apply_patch`.

</details>

<details>
<summary><code>add_method_to_class</code> — Вставить метод в класс через Roslyn AST.</summary>

**Параметры:**
- `filePath: string`
- `className: string` — top-level класс
- `methodSource: string` — объявление метода (модификаторы, сигнатура, тело)

Парсит C#, вставляет через DocumentEditor, форматирует. Для новых методов — вместо `apply_patch`.

</details>

<details>
<summary><code>update_method_body</code> — Заменить тело метода через Roslyn AST.</summary>

**Параметры:**
- `filePath: string` — абсолютный путь к `.cs`
- `className: string` — класс (top-level или вложенный)
- `methodName: string`
- `newBody: string` — только statements или блок `{ ... }`
- `parameterTypes: string[]?` — например `["string", "int"]` для перегрузок; **обязателен** при нескольких overload

**Поведение:** находит метод, парсит тело в `BlockSyntax`, заменяет block/expression body, проверяет syntax errors до записи, форматирует. Пара с `get_method_body` — вместо `apply_patch` для правок только тела.

</details>

<details>
<summary><code>remove_using</code> — Удалить using через Roslyn AST.</summary>

**Параметры:** `filePath`, `namespaceName`

</details>

<details>
<summary><code>organize_usings</code> — Сортировка и удаление неиспользуемых using.</summary>

**Параметры:** `filePath`, `removeUnused: bool = true`

</details>

<details>
<summary><code>add_property_to_class</code> — Вставить property через Roslyn AST.</summary>

**Параметры:** `filePath`, `className`, `propertySource`

</details>

<details>
<summary><code>add_field_to_class</code> — Вставить field через Roslyn AST.</summary>

**Параметры:** `filePath`, `className`, `fieldSource`

</details>

<details>
<summary><code>remove_member</code> — Удалить член класса по имени.</summary>

**Параметры:** `filePath`, `className`, `memberName`

</details>

<details>
<summary><code>add_type_to_class_bases</code> — Добавить базовый класс или интерфейс в base list.</summary>

**Параметры:** `filePath`, `className`, `typeName`

</details>

<details>
<summary><code>implement_interface</code> — Реализовать интерфейс (stubs NotImplemented).</summary>

**Параметры:** `filePath`, `className`, `interfaceName`

</details>

<details>
<summary><code>apply_patch</code> — Инструмент точечного поиска-замены по фрагменту.</summary>

**Параметры:**
- `filePath: string`
- `oldString: string`
- `newString: string`
- `replaceAll: bool = false`

**Поведение:** `replaceAll` продолжает поиск после вставленного `newString` (замену повторно не сканирует). Безопасно, если `newString` содержит `oldString`. В лог пишутся start/match/write и время.
</details>

### Build / Test / CLI

<details>
<summary><code>run_dotnet_build</code> — Запускает dotnet build и возвращает компактную сводку.</summary>

**Параметры:**
- `workspacePath: string` — только существующий **файл** `.csproj`, `.sln` или `.slnx` (не каталог).
- `configuration: string? = null` — опционально `-p:Configuration=` (например `Sit-Debug`, `Dit-Debug`). Если не задан — берётся с `load_workspace`.
- `noIncremental: bool = true` — `--no-incremental` на каждом build-шаге (по умолчанию). `false` только если явно принимаете up-to-date кэш MSBuild.
- `platform: string? = null` — опционально `-p:Platform=`. Если не задан — с `load_workspace`.
- `projectName: string? = null` — опционально. Если задан, `workspacePath` должен быть `.sln`/`.slnx`. Собирает этот проект через MSBuild-таргет виртуального пути в solution (`-t:"Folder\Project"`). Совпадение по display name, имени файла или виртуальному пути.

**Поведение:** наследование env + pinning (`MSBUILD_EXE_PATH`, `DOTNET_MSBUILD_SDK_RESOLVER_SDKS_*`). Mismatch → **error `MCP_MSBUILD_SDK_MISMATCH`** + pinned `dotnet exec …/MSBuild.dll /restore`. Цепочка minimal → pinned restore → restore/build detailed. **Итоговый exit** = последний `dotnet build` (не restore). В metadata — `Configuration` / `Platform` / `ProjectName` / `SolutionTarget` / `BuildArgs` / `NoIncremental`. Session `buildArgs` с `load_workspace` добавляются только к build-шагам (не к restore). Внутреннего кэша результатов MCP нет.

</details>

<details>
<summary><code>run_dotnet_test</code> — Запускает dotnet test с сокращенным выводом ошибок.</summary>

**Параметры:**
- `workspacePath: string`
- `timeoutSeconds: int = 300` — таймаут процесса; `0` отключает (не рекомендуется). Для долгих интеграционных поднимайте (900/1800).
- `noBuild: bool = false` — `--no-build` (после успешного `run_dotnet_build`).
- `noRestore: bool = false` — `--no-restore`.
- `configuration: string? = null` — опционально `dotnet test -c` (например `Sit-Debug`). Если не задан — с `load_workspace`.
- `platform: string? = null` — опционально `-p:Platform=`.
- `binariesPath: string? = null` — каталог bin с `{AssemblyName}.dll`. Нужен loaded `.sln`/`.slnx` и `workspacePath` на `.csproj`. При `noBuild=false` сначала `dotnet build <sln> -t`, затем `dotnet test` по DLL. При `noBuild=true` DLL уже должна лежать в этом каталоге.

**Поведение:** при `noBuild=false` и без `binariesPath` сначала отдельный incremental `dotnet build` (те же `-c` / platform / session `buildArgs` с `load_workspace`), затем `dotnet test --no-build --no-restore` (парсер видит только тест). С `binariesPath` сборка идёт через solution `-t`, цель теста — DLL. Сводка из `Passed!`, `Test Run Successful` + `Total tests`/`Passed:` (Failed=0 если нет строки), или `.slnx` fail-only `Total tests` + `Failed:` (Passed = Total − Failed − Skipped), FQN-строки тестов (`[ms]` / `[1 s]` / `[1 m 28 s]`); дедуп NU audit. Без маркеров сводки при exit 0 → **partial** + 2KB лога. `timeoutSeconds` — общий бюджет на build+test. Рядом с DLL нужны `.runtimeconfig.json` / `.deps.json`.

</details>

<details>
<summary><code>run_specific_test</code> — dotnet test с фильтром по классу и/или методу.</summary>

**Параметры:**
- `workspacePath: string`
- `className: string?` — например `UserServiceTests`
- `methodName: string?` — например `CreateUser_WhenValid_ReturnsOk`
- `timeoutSeconds: int = 300` — как у `run_dotnet_test`.
- `noBuild: bool = false` — `--no-build`.
- `noRestore: bool = false` — `--no-restore`.
- `configuration: string? = null` — опционально `dotnet test -c` (как у `run_dotnet_test`).
- `platform: string? = null` — опционально `-p:Platform=`.
- `binariesPath: string? = null` — как у `run_dotnet_test`: каталог bin с `{AssemblyName}.dll`; loaded `.sln`/`.slnx` и `workspacePath` на `.csproj`. `noBuild=false` собирает через solution `-t`.

Нужен хотя бы один из `className` / `methodName`. Tool строит VSTest-safe `--filter` (`FullyQualifiedName~…`, без `()` у метода, без лишней ведущей `.` на dotted FQN). После `load_workspace` Roslyn по возможности резолвит FQN типа/метода.

**Для модели:** TDD/фикс бага — этот tool, не полный suite. Сырой VSTest `--filter` (`TestCategory=Smoke`, `FullyQualifiedName~…`) — `run_test_by_filter`. Предпочитайте `className` + короткое `methodName`. После билда предпочитайте `noBuild=true`. При `noBuild=false` — тот же split build/test, что у `run_dotnet_test`. Проекты, которые собираются только внутри своего `.sln`, передавайте с `binariesPath` и `.csproj`.

</details>

<details>
<summary><code>run_test_by_filter</code> — dotnet test с сырым VSTest <code>--filter</code>.</summary>

**Параметры:**
- `workspacePath: string` — `.csproj`, `.sln`, `.slnx` или каталог тестового проекта (как у `run_dotnet_test`).
- `filter: string` — передаётся в `--filter` (например `FullyQualifiedName~MyClass`, `FullyQualifiedName~CreateUser`, `TestCategory=Smoke`). Пустой — ошибка. Не ставьте `()` у метода.
- `timeoutSeconds: int = 300` — как у `run_dotnet_test`.
- `noBuild: bool = true` — пропуск rebuild (по умолчанию **true**, в отличие от остальных test tools).
- `noRestore: bool = false` — `--no-restore`.
- `configuration: string? = null` — опционально `dotnet test -c`. Если не задан — с `load_workspace`.
- `platform: string? = null` — опционально `-p:Platform=`.
- `binariesPath: string? = null` — каталог bin с test DLL. Нужен loaded `.sln`/`.slnx` и `workspacePath` на `.csproj`. Запускает `{AssemblyName}.dll` из этого каталога. При `noBuild=false` сначала solution `-t`; при `noBuild=true` DLL уже должна существовать.

**Поведение:** тот же парсер VSTest и split build/test, что у `run_dotnet_test`, если `binariesPath` не задан и `noBuild=false`. Не проверяет, что needle фильтра есть в FQN теста (для `TestCategory` это дало бы ложный no-match). Для одного класса/метода предпочитайте `run_specific_test`.

</details>

<details>
<summary><code>get_test_list</code> — Список тестов в solution (JSON). Сначала saved <code>.cs</code> с диска.</summary>

**Параметры:**
- `maxResults: int = 200` (кламп 1–500)
- `projectName: string? = null` — опционально. Только этот проект в загруженном workspace (точное совпадение без учёта регистра: display name, имя файла без расширения, assembly). Нет совпадения или неоднозначность — список проектов, не пустой JSON.
- `nameContains: string? = null` — опционально. Подстрока без учёта регистра по VSTest FQN (`Namespace.Class.Method`), имени класса или метода.

**Поведение:** Fact/Theory/TestMethod/и т.д. Нужен `load_workspace`. Фильтры применяются **до** `maxResults`. В каждом элементе есть `projectName`. Без фильтров `count: 0` — подсказка, что загружен не тот `.csproj`; с фильтрами `count: 0` — нет совпадений (сначала ослабьте фильтр).

</details>

<details>
<summary><code>generate_test_method_stub</code> — Вставить заглушку тестового метода.</summary>

**Параметры:** `filePath`, `className`, `methodName`, `testFramework: string?` — `xunit` / `nunit` / `mstest`

</details>

<details>
<summary><code>list_nuget_packages</code> — Установленные NuGet-пакеты (JSON по проектам).</summary>

**Параметры:**
- `workspacePath: string` — `.sln`, `.slnx`, `.csproj` или каталог
- `includeTransitive: bool` — по умолчанию `true`
- `includeOutdated: bool` — по умолчанию `false` (`--outdated`)
- `includeVulnerable: bool` — по умолчанию `false` (`--vulnerable`)

**Для модели:** смотреть дерево зависимостей до правок `.csproj`; не `execute_dotnet_command` для списка пакетов.

</details>

<details>
<summary><code>list_outdated_packages</code> — Устаревшие NuGet-пакеты (JSON).</summary>

**Параметры:** `workspacePath` — shortcut для `list_nuget_packages` с `includeOutdated=true`.

</details>

<details>
<summary><code>add_package_reference</code> — Добавить PackageReference в .csproj.</summary>

**Параметры:** `projectPath`, `packageId`, `version?`. Сначала `search_nuget_registry`. После — `load_workspace`.

</details>

<details>
<summary><code>remove_package_reference</code> — Удалить PackageReference из .csproj.</summary>

**Параметры:** `projectPath`, `packageId`

</details>

<details>
<summary><code>search_nuget_registry</code> — Поиск пакета и последней стабильной версии на NuGet.</summary>

**Параметры:**
- `query: string` — id или поисковый термин
- `exactMatch: bool` — по умолчанию `true`
- `maxResults: int` — при `exactMatch=false`, по умолчанию 10

**Для модели:** проверять id/версию перед `dotnet add package`; не выдумывать названия пакетов.

</details>

<details>
<summary><code>execute_dotnet_command</code> — Запускает dotnet {command} в указанной директории.</summary>

**Параметры:**
- `command: string`
- `workingDirectory: string?`
</details>

<details>
<summary><code>run_format</code> — Запускает dotnet format (применение или verify-only). Отформатированные <code>.cs</code> подхватывает следующий поиск символов без reset.</summary>

**Параметры:**
- `workspacePath: string`
- `verifyOnly: bool = false`
</details>

### Filesystem Utility & Search

<details>
<summary><code>list_directory_tree</code> — Строит дерево файлов и директорий (исключая bin, obj, .git).</summary>

**Параметры:**
- `directoryPath: string`
- `maxDepth: int = 2`
</details>

<details>
<summary><code>search_code</code> — Поиск совпадений по файлам (plain text или regex).</summary>

**Параметры:**
- `pattern: string`
- `directoryPath: string? = null`
- `includeExtensions: string? = ".cs"` — список через запятую/`;` (`.cs,.csproj,.json`) или `*` для всех файлов.
- `useRegex: bool = false`
- `caseSensitive: bool = false` — по умолчанию без учёта регистра; для leftover branding — `true`.
- `maxResults: int = 50`
- `maxScanSeconds: int = 20`

**Для агента:** если в клиенте есть встроенный **`grep`**, для обычного текстового поиска предпочитай его (не grep из терминала). Этот MCP-tool — когда нужен поиск из процесса Roslyn MCP.
</details>

<details>
<summary><code>read_file_range</code> — Читает конкретный диапазон строк из файла с их исходными номерами.</summary>

**Параметры:**
- `filePath: string`
- `startLine: int` (1-based)
- `lineCount: int`
</details>

<details>
<summary><code>read_log_tail</code> — Читает конец лог-файла с опциональной фильтрацией по ключевому слову.</summary>

**Параметры:**
- `filePath: string? = null` — не указывайте, чтобы читать последний лог MCP (`logs/mcp-*.log`, как `tail_tool_log`).
- `lastNLines: int = 200`
- `filterKeyword: string? = null` — например `Failure`, `ERR`, `NU1903`.

**Совет:** для диагностики MCP удобнее `tail_tool_log` и `filterKeyword`.
</details>

<details>
<summary><code>tail_tool_log</code> — Читает последний лог сервера `logs/mcp-*.log`.</summary>

**Параметры:**
- `lastNLines: int = 200`
- `filterKeyword: string? = null`
</details>

<details>
<summary><code>manage_agent_scratchpad</code> — Управляет долговременной памятью агента между сессиями.</summary>

**Параметры:**
- `action: string` (`read` | `write` | `append` | `clear`)
- `content: string? = null`
</details>

### Roslyn Refactoring / Solution Insights

<details>
<summary><code>rename_symbol</code> — Семантический rename C# символа через Roslyn с предпросмотром.</summary>

**Параметры:**
- `filePath: string`
- `symbolName: string`
- `newName: string`
- `scope: string = "project"` — допустимо: `project` | `solution`
- `previewOnly: bool = true`

**Область:** только C# символы. Для папки проекта / `.csproj` / графа solution — `rename_project`. Для README/rules/URL — host Grep/edit.

**Поведение:** сохранение идёт через тот же write boundary, что и `apply_code_fix`. Неподдержанный analyzer-reference diff отклоняется до записи. Частичное сохранение сообщает Status, Reason и известные пути — это не полный успех rename.
</details>

<details>
<summary><code>rename_project</code> — Переименование SDK-style проекта (папка + `.csproj`) и правка MSBuild-графа.</summary>

**Параметры:**
- `projectPath: string` — путь к `.csproj`
- `newProjectName: string` — один сегмент пути (например `DupFinder.Core`)
- `dryRun: bool = true`
- `searchRoot: string? = null` — корень поиска соседних проектов / `.sln` / `.slnx`

**Поведение:** переносит одноимённую папку проекта и `.csproj`; обновляет `AssemblyName`/`RootNamespace` только если они совпадали со старым именем; чинит `ProjectReference`; обновляет `.sln` и `.slnx`. Только **SDK-style**. Нестандартный layout — hard fail. Не трогает C# namespace/типы, Docker, launchSettings, CI, docs. После apply: `load_workspace` → `run_dotnet_build`.
</details>

<details>
<summary><code>extract_interface</code> — Выделить public-интерфейс из класса.</summary>

**Параметры:**
- `filePath: string`
- `className: string`
- `interfaceName: string?` — по умолчанию `I` + className
- `createNewFile: bool = true` — записать `{InterfaceName}.cs` в ту же папку
- `previewOnly: bool = false`

**Поведение:** собирает public instance methods/properties/events класса, генерирует сигнатуры интерфейса, добавляет `: IInterface` к классу. Block/file-scoped namespace. Partial class не поддерживаются.
</details>

<details>
<summary><code>move_type_to_new_file</code> — Разнести top-level типы по файлам (one type per file).</summary>

**Параметры:**
- `filePath: string`
- `typeName: string?` — если не указан, переносит все типы, имя которых не совпадает с именем файла
- `previewOnly: bool = false`

**Поведение:** создаёт `{TypeName}.cs` рядом с исходником, копирует usings/namespace, удаляет тип из исходного файла. Partial types не поддерживаются.
</details>

<details>
<summary><code>list_projects</code> — Показывает проекты текущего workspace.</summary>

**Параметры:**
- `workspacePath: string? = null`
</details>

<details>
<summary><code>get_project_graph</code> — Строит граф зависимостей project-to-project.</summary>

**Параметры:**
- `workspacePath: string? = null`
</details>

### Жизненный цикл сервера

<details>
<summary><code>get_mcp_server_info</code> — Путь к exe, число tools, логи, состояние workspace.</summary>

**Параметры:** *(нет)*

После `dotnet publish` — проверка, что MCP подхватил новый бинарник (ожидай **v1.3.12** и **63** tools в `full`, или **19** в `lite`).

</details>

<details>
<summary><code>list_tool_groups</code> — Группы: назначение, active/inactive, состав.</summary>

**Параметры:** *(нет)*

Группы не включает. Для включения — `enable_tool_group` или рестарт с `ROSLYN_MCP_TOOL_GROUPS`. Markdown; `tools/list` остаётся JSON Schema.

</details>

<details>
<summary><code>get_tool_help</code> — Markdown-справка по одному тулу (kind, group, живые параметры).</summary>

**Параметры:**
- `toolName: string` — точное публичное имя (например `find_usages`). Неизвестные имена — близкие совпадения.

</details>

<details>
<summary><code>enable_tool_group</code> — Добавляет все недостающие тулы одной группы в эту сессию.</summary>

**Параметры:**
- `group: string` — точное имя группы, без учёта регистра (`files`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations`).

Идемпотентно. Профиль `full` — no-op. `tools/list_changed` только при реальном добавлении. Если клиент не обновляет список: рестарт с `ROSLYN_MCP_TOOL_GROUPS=<group>`. Disable в этом релизе нет.

</details>

<details>
<summary><code>stop_mcp_server</code> — Завершает процесс MCP после ответа (чтобы пересобрать бинарник сервера; затем перезапуск MCP в IDE). Не для refresh сохранённых исходников — они синхронизируются сами.</summary>

**Параметры:** *(нет)*

</details>

### Prompts

<details>
<summary><code>RefactoringAssistantPrompt</code> — Краткие инструкции для сценариев C#-рефакторинга.</summary>

**Параметры:**
- `focus: string? = null`
</details>

## Лицензия
Этот проект распространяется под лицензией MIT — подробности см. в файле [LICENSE](LICENSE).