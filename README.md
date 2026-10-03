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

The server is C#-focused. It can read non-C# files and execute selected CLI operations, but it does not provide Python semantic analysis. See [Architecture and constraints](docs/ARCHITECTURE.md) for component boundaries, state, synchronization, and extension rules. Docs index: [docs/README.md](docs/README.md). Planned (not shipped) large-solution load cache: [docs/workspace-load-cache/](docs/workspace-load-cache/README.md).

Plugin development and setup: [authoring guide](samples/RoslynMcpPlugin/README.md#english-version).

## What it provides

- Compiler-aware declaration, usage, implementation, and call-graph navigation.
- Roslyn diagnostics, code actions, AST edits, and semantic rename.
- ILSpy inspection of referenced or explicitly addressed assemblies.
- Build, test, run, format, and NuGet workflows with SDK alignment, timeouts, parsed diagnostics, and context-safe output.
- `full` and context-saving `lite` tool catalogs, plus in-session tool-group discovery.

## Saved edits and the semantic snapshot

After `load_workspace`, semantic tools trust the file watcher until the next confirmation. A confirmation is another `load_workspace`, or a flush of a save the watcher actually reported. The tools do not hash the tree on every call, and they do not mark the snapshot `freshness=unknown`.

- A saved `.cs` that is already a document is read once and applied to every project document of that path.
- A saved additional file or analyzer config that is already a document, and is not also an evaluation input, is applied on the published snapshot. XAML, resx, and Razor are recorded by role and are not text-synced.
- A new or deleted file does not add or remove a document. The project graph is marked stale; call `load_workspace` again. A project file, a solution, or `Directory.Build.*` marks the graph stale the same way.
- Watcher overflow, a watcher error, or a directory rename asks the next semantic call to re-read known documents and can mark the graph stale.
- An edit the watcher never reported — a file outside the watch set, or a dropped notification with no error — stays invisible until the next `load_workspace`. Standing coverage gaps (imports, restore inputs, custom tasks) do not by themselves block a read.

The other freshness policies that were considered and not chosen are recorded in [epoch 3](docs/workspace-load-cache/epoch-3-live-consistency/spec.md).

## Security boundary

This server can read and write files and start `dotnet`/Git child processes with the permissions of its host process. It is **not a sandbox** and does not confine every operation to the loaded workspace.

- Use Git and review agent changes.
- Do not run the server as Administrator/root.
- Never put PATs, passwords, connection strings, or other secrets in prompts or tool arguments.
- Incoming MCP parameters are logged by default; see [Logs](#logs) before using the server with sensitive repositories.

## Privacy

RoslynMcpServer does not collect telemetry or transmit user data automatically.

Network access occurs only when explicitly requested by the user or MCP client
through functionality that requires access to an external service.

The process is local stdio: no backend and no HTTP client of its own. File logs stay next to the executable. NuGet/registry traffic happens only when an MCP tool runs `dotnet` against a feed (`search_nuget_registry`, outdated/vulnerable listing, restore as part of build/test/run). Those calls send package ids and restore metadata, not source or prompts. Details: [PRIVACY.md](PRIVACY.md).

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

Default is **`full`** (every public tool). A **`lite`** session starts with the 15-tool core so local models spend less context on `tools/list`. Extra groups can be added at process start or, on clients that honor `notifications/tools/list_changed`, during the session.

| Variable | Values | Effect |
| --- | --- | --- |
| `ROSLYN_MCP_TOOL_PROFILE` | `full` (default) or `lite` | Selects the startup catalog. Empty/unset is `full`. |
| `ROSLYN_MCP_TOOL_GROUPS` | `core`, `files`, `navigation`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations` (comma-separated, case-insensitive) | Adds those groups to **`lite` before the first `tools/list`**. In `full` the names are recorded and do not change the set. Unknown names fail startup. `core` is already in lite. |

Example: `ROSLYN_MCP_TOOL_GROUPS=decompile,nuget`

Portable OpenCode examples: [`opencode.json.sample`](opencode.json.sample) (`roslyn-mcp-server` = full, `roslyn-mcp-lite`, `roslyn-mcp-lite-groups`). Enable only one server.

| Group | Intent | Tools |
| --- | --- | ---: |
| `core` | Workspace, symbol lookup, build, test, help | 15 (lite default) |
| `files` | Disk read, search, patch, `get_code_skeleton` | 8 |
| `navigation` | Type hierarchy, references from a known declaration, call graph | 3 |
| `editing` | AST edits, code fixes, format, rename | 8 |
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

**Client compatibility:** do not assume the host refreshes tools after `tools/list_changed`. If a newly enabled tool is missing from the client's catalog, restart with `ROSLYN_MCP_TOOL_GROUPS=<group>` (and `ROSLYN_MCP_TOOL_PROFILE=lite`). A `2025-11-25` initialize session receives `tools/list_changed` when the set grows. A `2026-07-28` session receives it only after `subscriptions/listen` with `toolsListChanged`; the server accepts that revision through the MCP C# SDK. Measured minified `tools/list` (UTF-8): full **54 / 43,029**; lite **15 / 16,906**.

## Agent tools by version

Version history of agent-visible tools and behavior: [CHANGELOG.md](CHANGELOG.md). Current server version: see `RoslynMcpServer.csproj`.

## Agent Initialization (How to force tool usage)

Even if the MCP is active, AI clients don't always load the tools into the current chat context. Put the **session policy** in the app repo so the agent actually uses Roslyn MCP.

**Canonical file:** [`AGENTS.md.sample`](AGENTS.md.sample) — copy into the **application** repository as `AGENTS.md` (or merge into `.cursor/rules`). It is policy only (when / MCP vs host / bans); per-tool parameters live in MCP tool Descriptions and in **Reference: MCP Tools** below.

**OpenCode:** `install2opencode.ps1` (see **Option C** above) writes or merges the sample into `AGENTS.md` automatically.

**Maintainers:** when tools or agent-visible behavior change, update **`AGENTS.md.sample`, [CHANGELOG.md](CHANGELOG.md), and this README** (Reference) together. Do not paste the full AGENTS body into README — link the sample.

Policy summary (full text in the sample):

- Verify the server with `get_mcp_server_info`; inspect missing `lite` tools with `list_tool_groups` / `enable_tool_group`
- Optional `RoslynMcp.jsonc` (exe directory, then cwd; cwd wins). Set `ROSLYN_MCP_WORKSPACE` to the repo root at startup so cwd is not the user profile. See `RoslynMcp.jsonc.sample` and `AGENTS.md.sample`
- `load_workspace` before symbol / build / decompile work (or rely on config `workspace-path`); prefer `.sln` / `.slnx`; compares only arguments you passed
- Missing `Compile` target on load → retry with `targetFramework` (inner TFM); not SDK mismatch
- VS 2026 BuildHost / `XMakeElements` → not SDK mismatch; need MCP 1.0.35+ or a single SDK-style `.csproj`
- C# identifiers → MCP first; plain text → host Grep; never shell `grep` / `dotnet build|test`
- Saved `.cs` of already loaded files (v1.1.0+) sync into symbol search automatically; new or deleted `.cs` needs reload (v1.3.30); unsaved editor buffers are ignored; `reset_workspace` after build / generated `obj`
- IDE: host edit/write; headless: MCP `apply_patch` / AST tools
- Secrets: never paste PAT/passwords; app README must document run target / sample args

## Logs
- **Main log:** `logs/mcp-*.log` (relative to `AppContext.BaseDirectory`).
- Global incoming JSON-RPC logging is enabled by default.
- **Tool output:** logged as a one-line summary plus separate warning/error lines (not a duplicated full MCP response). Set `ROSLYN_MCP_LOG_TOOL_OUTPUT=full` to log entire tool responses at Information level.
- Environment Variables:
  - `ROSLYN_MCP_WORKSPACE` — repo root for MSBuild/SDK discovery at startup (see MCP config above). Also sets process cwd so `RoslynMcp.jsonc` in the working directory is read from the repo, not the user profile.
  - `ROSLYN_MCP_TOOL_PROFILE` — `full` (default) or `lite` (see **Tool profiles**).
  - `ROSLYN_MCP_TOOL_GROUPS` — comma-separated extra groups for `lite` at startup. Valid: `core`, `files`, `navigation`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations` (see **Tool profiles**).
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
- `symbolName` — C# identifier for `find_symbol_definition`, `find_symbol_references`, `find_usages`, and `find_implementations` (exact name; solution-wide definition/usages/implementations and `find_symbol_references` without `filePath` are case-insensitive; `filePath` without `line` on definition and references is ordinal case-sensitive).
- `diagnosticId` — compiler/analyzer id from `get_diagnostics_for_file` (e.g. `CS0246`) for `get_code_fixes` / `apply_code_fix`.
- `fixIndex` — 0-based index from `get_code_fixes` for `apply_code_fix`.
- `path` — `.cs` file or directory for `get_code_skeleton` (absolute path; disk-based, no workspace required).

When a tool accepts `filePath`, relative values are resolved against the loaded workspace root after `load_workspace`; if no workspace is loaded, fallback is `Environment.CurrentDirectory`.

There are **54** registered tools in the default `full` profile (see list below). A `lite` profile starts with **15** core tools; extra groups use `ROSLYN_MCP_TOOL_GROUPS` or `enable_tool_group`.

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
- `shadowCopyInSolutionAnalyzers: bool = false` — when `true`, rewrites only `AnalyzerReference`s whose complete load-session provenance exact-joins them to one loaded in-solution project; filename/`AssemblyName` similarity is not evidence. Same-name external, unconfirmed, or ambiguous references remain unchanged. An inaccessible original path is skipped (U-ARB-01 skip; `access_failure`; not missing; not rewritten) — see [the decision](docs/archive/analyzer-shadow-copy-improvements/v2/u-arb-01-inaccessible-skip.md); rewrite/fail-closed remain alternatives if field evidence warrants a change. A missing, Failed, Incomplete, or other-session capture does not publish a semantic snapshot (the MSBuild graph may still open); cached `true` reuses that capture — call `reset_workspace` then `load_workspace`. A failed opt-in stays fail-closed across edit/flush/reconciliation: raw workspace may keep original refs for persistence, but they are not published or executed. Activation is session-sticky: after enablement, a same-key cached `false`/omitted call preserves the active overlay without preparation/refresh. The selected project must already have its resolved output on disk. Prepared generations are content-hashed (`v2-main-only`) and reused on document edit without recopying analyzer files. After rebuilding a generator with the same assembly identity, restart the MCP process — `reset_workspace` does not unload CLR assemblies. Private helper DLLs are refused (main-only). This response includes a short summary (`N rewritten` plus execution status, or an explicit semantic-unavailable note); per-reference detail goes to the MCP server log.

**Analyzer shadow-copy activation and side effects:** this flag is opt-in; the MCP client and agent do not enable it automatically. Add a conditional rule to the target repository's `AGENTS.md` when that repository uses in-solution analyzer/generator projects with a custom `OutputPath`. Enabling it copies versioned analyzer DLL/PDB files under the OS temp `v2-main-only/` namespace and re-applies the in-memory overlay after workspace document updates, adding some disk I/O and CPU. Activation is session-sticky: cached `false`/omitted preserves an already-active overlay and does not refresh analyzer files; use `reset_workspace` then load without the flag to disable. Old generations (including leftover timestamp directories) are not cleaned automatically and can accumulate; `reset_workspace` does not delete them. The real `.csproj` and analyzer build output are not modified; on the supported overlay paths the MCP process locks the shadow copy, not the real output. Path resolution and this anti-lock workaround are independent. `find_symbol_definition` by a generator-emitted type name remains a Roslyn limitation, not a failed overlay.

**Atomic anti-lock boundary (U-ARB-04):** physical load/cache lookup, opt-in prepare, execution gate, and final snapshot publication are one serialized manager operation. Production semantic readers wait for this boundary (`GetPublishedSolutionAsync` / `FindDocumentAsync`) and never compile an intermediate raw analyzer reference. `GetCurrentSolution()` returns only the published snapshot. If opt-in entered first, a concurrent semantic request receives the shadow overlay; prepare failure/cancellation on a new session publishes only a fail-closed snapshot and does not later re-publish confirmed real refs. A semantic operation completed before later enablement remains an honest no-overlay operation and can make the identity gate require a process restart. See the [evidence](docs/archive/analyzer-shadow-copy-improvements/v2/u-arb-04-load-boundary-evidence.md), [decision](docs/archive/analyzer-shadow-copy-improvements/v2/u-arb-04-decision.md), and [implementation specification](docs/archive/analyzer-shadow-copy-improvements/v2/u-arb-04-atomic-load-prepare.md).

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

**Note:** Does not require `load_workspace`. In `lite`, enable the `files` group (or use `full`) to see this tool. For a file already in the loaded solution, `get_class_skeleton` may still be preferable (workspace-consistent view).
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
<summary><code>find_symbol_references</code> — Finds usages of a type/member across the solution.</summary>

**Parameters:**
- `symbolName: string` — simple name or exact FQN (`Namespace.Type` / `Namespace.Type.Member`; no `global::`, no `()`); solution-wide search is case-insensitive; with `filePath` and no `line`, match is ordinal case-sensitive; with `filePath`+`line`, also the auto-column needle when `column` is omitted
- `filePath: string?` — optional `.cs` file; omit for solution-wide name/FQN search; required when `line` is set; without `line`, select the unique matching declaration in that file (error if several)
- `line: int?` — optional 1-based line; requires `filePath`; when set, resolve the symbol at that position (declaration or usage)
- `column: int?` — optional 1-based column; omit to auto-pick the unique matching identifier token on the line
- `maxResults: int?` — optional listing cap (1–500). Default 50, or env `ROSLYN_MCP_MAX_RESULTS` when a positive int; explicit arg wins
- `preview: bool = false` — when true, append the source line (truncated at 400 chars); default `path:line:col` only
- `overflowCursor: string?` — when set, return the next in-memory overflow chunk (does not start a new search)
- `directOnly: bool = false` — when true (requires `filePath`), for a class virtual/abstract/override method keep only direct-receiver calls; default false returns the same points and may add one virtual-dispatch note. Cross-project source/metadata types match by full name.

**Behavior:** File without line covers class/struct/record/interface/enum/method/ctor/dtor/property/event/field (not operator/indexer/local function). Several matches → error listing FQN and identifier `line:column` (no silent first). Locations beyond `maxResults` are stored in-process behind a cursor (not silent drop; not a host `%Temp%` path). Unknown/expired cursor → human error. Filter (when used) runs after dedupe and before the listing cap / overflow store.
</details>

<details>
<summary><code>find_symbol_definition</code> — Semantic lookup: where a type or member is declared (file, line, column, full name) in the loaded solution.</summary>

**Parameters:**
- `symbolName: string` — class, interface, struct, enum, or member identifier (e.g. `IRunCommand`); solution-wide search is case-insensitive; file without line is ordinal case-sensitive.
- `filePath: string?` — optional; omit for solution-wide name search; without `line`, select the unique matching declaration in that file; required with `line` for positional go-to-definition
- `line: int?` — optional 1-based line in `filePath` (usage or declaration)
- `column: int?` — optional 1-based column; omit to auto-pick the unique matching identifier token on the line
- `maxResults: int?` — optional listing cap (1–500). Default 50, or env `ROSLYN_MCP_MAX_RESULTS` when a positive int; explicit arg wins
- `overflowCursor: string?` — next in-memory overflow chunk; does not start a new search

**Behavior:** Locations beyond `maxResults` are stored in-process behind a cursor (not silent drop; not a host `%Temp%` path). Unknown/expired cursor → human error. File+line mode uses the same cursor scheme.

**Model guidance:** after `load_workspace`, use this for “where is X **declared**?” — do **not** answer that with plain-text search or invent a generic tool named `search`. For free-text matches across files, use your client’s built-in **`grep`** tool (not `bash`/`PowerShell` grep). This tool avoids `bin/`/`obj/` and uses Roslyn. **Saved** `.cs` (IDE/git) are applied before search; unsaved buffers are ignored — no `reset_workspace` for ordinary saves. Omit `filePath`/`line` for solution-wide name search; pass `filePath` without `line` for the single declaration in that file (prints every in-source location, including partial parts, each with column and full name); several matches → error with FQN and identifier coordinates; pass `filePath`+`line` to go to the definition of the symbol under that position.
</details>

<details>
<summary><code>find_usages</code> — Solution-wide references for a declared name (<code>path:line:col</code>; optional preview / overflow).</summary>

**Parameters:**
- `symbolName: string` — simple name or exact FQN (same rules as `find_symbol_references` without `filePath`).
- `maxResults: int?` — optional listing cap (1–500). Default 50, or env `ROSLYN_MCP_MAX_RESULTS` when a positive int; explicit arg wins
- `preview: bool = false` — when true, append the source line (truncated at 400 chars)
- `overflowCursor: string?` — next in-memory overflow chunk; does not start a new search

**Behavior:** Requires `load_workspace`. Name-based alias of `find_symbol_references` without `filePath`. Applies saved `.cs` from disk first. All matching declaration groups are returned (no primary pick). FQN miss lists candidates and does not fall back to simple name. Overloads share one FQN group. Excess locations use the same overflow store as `find_symbol_references`.
</details>

<details>
<summary><code>find_implementations</code> — Find classes implementing an interface or derived from a base type.</summary>

**Parameters:**
- `symbolName: string` — interface or base class name (e.g. `IRepository`, `BaseController`). Simple name returns a section per matching base; exact FQN selects one type
- `transitive: bool = true` — when `true`, includes indirect implementations / derived types in the hierarchy
- `maxResults: int?` — optional listing cap (1–500). Default 50, or env `ROSLYN_MCP_MAX_RESULTS` when a positive int; explicit arg wins
- `preview: bool = false` — when true, append the source line (truncated at 400 chars); default `path:line:col` only
- `overflowCursor: string?` — next in-memory overflow chunk; does not start a new search

**Behavior:** Requires `load_workspace`. Applies saved `.cs` from disk first. For **interfaces**, uses Roslyn `FindImplementationsAsync`; for **classes/structs**, uses `FindDerivedClassesAsync`. Several same-named bases each get a section (empty sections are kept). Exact FQN still selects one type; FQN miss returns the resolver candidate text (no simple-name fallback). Returns each matching type with `path:line:col`. Excess types use the same overflow store. Do not use text search or `find_usages` for “who implements X?” / “what inherits from Y?”.

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
- `line: int?` — optional 1-based line; must be passed together with `column`
- `column: int?` — optional 1-based column; must be passed together with `line`

**Behavior:** Requires `load_workspace`. Uses `SymbolFinder.FindCallersAsync` and invocation analysis inside the method body. Without position, several same-named methods in the class is an error (FQN + line:column), not the first match. With `line`+`column`, resolves a declaration or call site. Use for bug investigation instead of loading many bodies via `get_method_body`.

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

**Behavior:** Inherits full process env, then pins SDK via `MSBUILD_EXE_PATH`, `MSBuildSDKsPath`, `DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR` / `SDKS_VER` / `CLI_DIR`, `DOTNET_ROOT`. On MSBuild path mismatch → **`error MCP_MSBUILD_SDK_MISMATCH`** and `dotnet exec …/10.x/MSBuild.dll /restore`. Escalation: minimal build → pinned restore → restore (detailed if empty) → build normal → build detailed. **Effective exit** = last `dotnet build` step (not restore). Metadata reports `Configuration` / `Platform` / `ProjectName` / `SolutionTarget` / `BuildArgs` / `NoIncremental`. Session `buildArgs` from `load_workspace` are appended to build steps only (not restore). **Key lines** include task `-- FAILED` with project context. No in-process result cache — “cached” greens were MSBuild incremental or exit overwrite. While a step runs, MCP progress heartbeats report the step label, elapsed seconds, and previous exit (no stdout); this is UX only and cannot extend the host `tools/call` timeout.

</details>

<details>
<summary><code>run_dotnet_test</code> — Runs dotnet test with condensed failure details.</summary>

**Parameters:**
- `workspacePath: string`
- `timeoutSeconds: int = 300` — process timeout; `0` disables (not recommended). Raise for long integration tests (e.g. 900/1800).
- `noBuild: bool? = null` — omit means false on the non-DLL route (`--no-build` only when true). With `binariesPath`, `false` means `always` and `true` means `never`; do not pass this together with `buildPolicy`.
- `buildPolicy: string? = null` — `auto`, `always`, or `never`. Requires `binariesPath`. Omit both this and `noBuild` for `auto`.
- `noRestore: bool = false` — pass `--no-restore`.
- `configuration: string? = null` — optional `dotnet test -c` (e.g. `Sit-Debug`). Omit to inherit `load_workspace` (use the same value as `run_dotnet_build` when `noBuild=true`).
- `platform: string? = null` — optional `-p:Platform=`. Omit to inherit `load_workspace`.
- `binariesPath: string? = null` — optional bin directory containing `{AssemblyName}.dll`. Requires a loaded `.sln`/`.slnx` and a `.csproj` `workspacePath`. When `noBuild=false`, builds that project via the loaded solution `-t` first, then `dotnet test` on the DLL. When `noBuild=true`, the DLL must already exist in that directory.
- `includeFullOutput: bool = false` — when `true`, include failed-test Standard Output/Error up to 100000 chars per stream. Default `false` uses `maxOutputChars`.
- `maxOutputChars: int = 0` — per-failure Standard Output budget. `0` = 2500 (head 1600 + tail 700), or 100000 when `includeFullOutput` is true. Capped at 100000. StdErr scales with this.

**Behavior:** When `noBuild=false` and `binariesPath` is omitted, runs incremental `dotnet build` first (same `-c` / platform / session `buildArgs` from `load_workspace`; not the `run_dotnet_build` probe), then `dotnet test --no-build --no-restore`. With `binariesPath`, the compile is `dotnet build <sln> -t` and the test target is the DLL. Parser sees only the test process. `--logger "console;verbosity=normal"`. Summary from `Passed!`, `Test Run Successful` + `Total tests`/`Passed:` (Failed defaults to 0), or `.slnx` fail-only `Total tests` + `Failed:` (Passed inferred as Total − Failed − Skipped), or per-test `  Passed FQN [ms]` / `[1 s]` / `[1 m 28 s]`. Failed-test `Error Message:` is reported multiline (head+tail); Standard Output/Error are separate `StdOut`/`StdErr` blocks (default 2500/1000 chars, head+tail; `includeFullOutput` / `maxOutputChars` raise the cap). The VSTest `Build FAILED` / `0 Error(s)` footer is ignored as a compile result. MSBuild/prune noise ignored; duplicate NU audit lines deduped. Exit 0 without any summary marker → **`Status: partial`** + last 2KB (footer stripped). `run_specific_test` checks the filter matched a test (FQN, method-only xUnit display names, Theory `FQN(args)` Passed/Failed lines). `timeoutSeconds` is the combined budget for build+test. A DLL test also needs `.runtimeconfig.json` / `.deps.json` beside the assembly. While a step runs, MCP progress heartbeats report `dotnet build` / `dotnet test` (no stdout); this is UX only and cannot extend the host `tools/call` timeout.

</details>

<details>
<summary><code>run_specific_test</code> — Run dotnet test filtered to one class and/or method.</summary>

**Parameters:**
- `workspacePath: string`
- `className: string?` — e.g. `UserServiceTests` (simple or fully qualified)
- `methodName: string?` — e.g. `CreateUser_WhenValid_ReturnsOk`
- `timeoutSeconds: int = 300` — same as `run_dotnet_test`.
- `noBuild: bool? = null` — omit means false. With `binariesPath`, `false` means `always` and `true` means `never`; do not pass this together with `buildPolicy`.
- `buildPolicy: string? = null` — same DLL-route policy as `run_dotnet_test`. Requires `binariesPath`.
- `noRestore: bool = false` — pass `--no-restore`.
- `configuration: string? = null` — optional `dotnet test -c` (same as `run_dotnet_test`).
- `platform: string? = null` — optional `-p:Platform=`. Omit to inherit `load_workspace`.
- `binariesPath: string? = null` — same as `run_dotnet_test`: bin directory with `{AssemblyName}.dll`; loaded `.sln`/`.slnx` plus `.csproj` `workspacePath`. `noBuild=false` builds via the solution `-t` first.
- `includeFullOutput: bool = false` — same as `run_dotnet_test`.
- `maxOutputChars: int = 0` — same as `run_dotnet_test`.

At least one of `className` or `methodName` is required. The tool builds a VSTest-safe `--filter` internally (`FullyQualifiedName~…`, no method `()`, no extra leading `.` on dotted names). After `load_workspace`, Roslyn resolves the type/method FQN when possible. A pass is recognized when VSTest prints only the method name or `Method(args)` — that is not **no matching tests**.

**Model guidance:** use this for TDD red/green loops — do not run the full suite. For a raw VSTest `--filter` (`TestCategory=Smoke`, `FullyQualifiedName~…`) use `run_test_by_filter`. Prefer `className` + short `methodName`. After build, prefer `noBuild=true` for faster filtered re-runs. Same pre-test build split as `run_dotnet_test` when `noBuild=false`. Projects that must be built inside their `.sln` should pass `binariesPath` and a `.csproj` `workspacePath`.

</details>

<details>
<summary><code>run_test_by_filter</code> — Run dotnet test with a raw VSTest <code>--filter</code>.</summary>

**Parameters:**
- `workspacePath: string` — `.csproj`, `.sln`, `.slnx`, or test project directory (same as `run_dotnet_test`).
- `filter: string` — passed through as `--filter` (e.g. `FullyQualifiedName~MyClass`, `FullyQualifiedName~CreateUser`, `TestCategory=Smoke`). Empty is an error. Do not put method `()`.
- `timeoutSeconds: int = 300` — same as `run_dotnet_test`.
- `noBuild: bool? = null` — omit means true on the non-DLL route (default **true**, unlike the other test tools). With `binariesPath`, `false` means `always` and `true` means `never`; do not pass this together with `buildPolicy`.
- `buildPolicy: string? = null` — same DLL-route policy as `run_dotnet_test`. Requires `binariesPath`.
- `noRestore: bool = false` — `--no-restore`.
- `configuration: string? = null` — optional `dotnet test -c`. Omit to inherit `load_workspace`.
- `platform: string? = null` — optional `-p:Platform=`. Omit to inherit `load_workspace`.
- `binariesPath: string? = null` — optional bin directory containing the test DLL. Requires a loaded `.sln`/`.slnx` and a `.csproj` `workspacePath`. Runs `{AssemblyName}.dll` from that directory. When `noBuild=false`, builds the project via the loaded solution `-t` first; when `noBuild=true`, the DLL must already exist.
- `includeFullOutput: bool = false` — same as `run_dotnet_test`.
- `maxOutputChars: int = 0` — same as `run_dotnet_test`.

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
<summary><code>search_code</code> — Text search (managed file walk by default; optional ripgrep).</summary>

**Parameters:**
- `pattern: string`
- `directoryPath: string? = null`
- `includeExtensions: string? = ".cs"` — comma/semicolon list (`.cs,.csproj,.json`), or `*` for all files.
- `useRegex: bool = false` — .NET regex for the managed walk; ripgrep (Rust) syntax when `useRipgrep` is true.
- `caseSensitive: bool = false` — default case-insensitive; for leftover branding checks set `true`.
- `maxResults: int = 50`
- `maxScanSeconds: int = 20`
- `useRipgrep: bool = false` — when true, runs `rg` only (no managed-walk fallback). Missing `rg` is an error only with this flag.
- `ripgrepPath: string? = null` — full path to `rg`/`rg.exe`; used only with `useRipgrep=true` (else ignored). Omit to use `RoslynMcp.jsonc` `ripgrep-path` or PATH.

**Agent note:** if your client exposes a built-in **`grep`** tool, prefer that for ad-hoc text search (never shell-driven grep). Use this MCP tool when you need search inside the workspace from the Roslyn MCP process. Do not assume ripgrep is installed.
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
- `line: int?` — optional 1-based line; must be passed together with `column`
- `column: int?` — optional 1-based column; must be passed together with `line`

**Scope:** C# symbols only (types/members/namespaces as symbols). For project folder / `.csproj` / solution graph use `rename_project`. For README/rules/URLs use host Grep/edit.

**Behavior:** Without position, several same-named declarations in the file is an error (FQN + line:column) and nothing is written. With `line`+`column`, resolves a declaration or usage in that file. Persist goes through the same write boundary as `apply_code_fix`. Unsupported analyzer-reference diffs are rejected before writes. Partial save reports Status, Reason, and known saved paths — not full success of the rename. Preview remains the default.
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

Use after `dotnet publish` to verify the MCP host picked up the new binary (expect **v1.3.32** and **63** tools on `full`, or **19** on `lite`).

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

## License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
---

## <a name="russian-version"></a>
 🇷🇺 Описание на русском

[🇬🇧 Back to English (Main)](#roslynmcpserver)

RoslynMcpServer предоставляет AI-агенту compiler-aware инструменты для C#-решений: семантическую навигацию, диагностику и code fixes, рефакторинг, анализ зависимостей и компактный вывод `dotnet build/test/run`.

В отличие от файлового MCP, сервер загружает `.sln`, `.slnx` или `.csproj` через Roslyn/MSBuild. Агент работает с символами и контекстом проектов, а не только предполагает структуру по тексту.

Разработка и подключение плагинов: [руководство автора](samples/RoslynMcpPlugin/README.md#russian-version).

## MCP и агент за одну минуту

- **MCP-клиент** — Cursor, OpenCode или другой совместимый хост. Запускает сервер и предоставляет его tools модели.
- **AI-агент** — модель и orchestration клиента. Именно агент выбирает tools; он не является частью этого репозитория.
- **RoslynMcpServer** — локальный stdio-процесс. У него нет собственного чата и автономного цикла.
- **`AGENTS.md`** — правила репозитория для агента. MCP-конфиг подключает tools, а этот файл определяет, когда ими пользоваться.

Типовой поток:

`запрос разработчика → агент → MCP tool → Roslyn/MSBuild/ILSpy/dotnet → компактный результат → агент`

Сервер ориентирован на C#. Он умеет читать другие файлы и запускать отдельные CLI-операции, но не выполняет семантический анализ Python. Компоненты, состояние и обязательные ограничения описаны в [Architecture and constraints](docs/ARCHITECTURE.md). Индекс документации: [docs/README.md](docs/README.md). План (ещё не в runtime) кеша загрузки больших решений: [docs/workspace-load-cache/](docs/workspace-load-cache/README.md).

## Основные возможности

- Поиск объявлений, использований, реализаций и связей вызовов через Roslyn.
- Диагностика, CodeAction, AST-правки и семантический rename.
- Анализ подключённых или явно заданных сборок через ILSpy.
- Build, test, run, format и NuGet с выравниванием SDK, таймаутами и структурированным выводом.
- Полный каталог `full` и экономный `lite` с группами tools.

## Сохранённые правки и семантический снимок

После `load_workspace` семантические тулы доверяют файловому наблюдателю до следующего подтверждения. Подтверждение — это ещё один `load_workspace` или flush сохранения, о котором наблюдатель действительно сообщил. На каждом вызове дерево не хешируется, и снимок не помечается `freshness=unknown`.

- Сохранённый `.cs`, который уже есть в графе, читается один раз и применяется ко всем документам этого пути.
- Сохранённый additional file или analyzer config, который уже есть в графе и не является входом оценки проекта, применяется к опубликованному снимку. XAML, resx и Razor учитываются по роли, их текст не синхронизируется.
- Новый или удалённый файл документ не добавляет и не удаляет. Граф проекта помечается устаревшим; вызовите `load_workspace` снова. Файл проекта, solution и `Directory.Build.*` помечают граф так же.
- Переполнение наблюдателя, его ошибка или переименование каталога просят следующий семантический вызов перечитать известные документы и могут пометить граф устаревшим.
- Правка, о которой наблюдатель не сообщил — файл вне области наблюдения или потерянное уведомление без ошибки, — остаётся невидимой до следующего `load_workspace`. Постоянные дыры покрытия (импорты, входы restore, custom tasks) сами по себе чтение не блокируют.

Остальные политики свежести, которые рассматривались и не выбраны, записаны в [эпохе 3](docs/workspace-load-cache/epoch-3-live-consistency/spec.md).

## Граница безопасности

Сервер читает и пишет файлы и запускает дочерние процессы `dotnet`/Git с правами хост-процесса. Это **не sandbox**; не все операции ограничены каталогом загруженного workspace.

- Используйте Git и проверяйте изменения агента.
- Не запускайте сервер от администратора/root.
- Не передавайте PAT, пароли и connection strings в prompts или аргументах tools.
- Параметры входящих MCP-вызовов по умолчанию логируются; проверьте раздел «Логи» для чувствительных репозиториев.

## Конфиденциальность

RoslynMcpServer не собирает телеметрию и не передаёт пользовательские данные автоматически.

Сетевой доступ возникает только когда пользователь или MCP-клиент явно запрашивают функцию, которой нужен внешний сервис.

Процесс локальный (stdio): своего бэкенда и HTTP-клиента нет. Логи пишутся на диск рядом с исполняемым файлом. Обращения к NuGet/реестру — только если MCP-tool запускает `dotnet` к фиду (`search_nuget_registry`, outdated/vulnerable, restore в составе build/test/run). Уходят идентификаторы пакетов и метаданные restore, не исходники и не промпты. Подробности: [PRIVACY.md](PRIVACY.md).

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

По умолчанию **`full`** (все публичные тулы). **`lite`** стартует с 15 core-тулов. Дополнительные группы — при старте процесса или, если клиент обрабатывает `notifications/tools/list_changed`, во время сессии.

| Переменная | Значения | Эффект |
| --- | --- | --- |
| `ROSLYN_MCP_TOOL_PROFILE` | `full` (по умолчанию) или `lite` | Стартовый каталог. Пустое/не задано = `full`. |
| `ROSLYN_MCP_TOOL_GROUPS` | `core`, `files`, `navigation`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations` (через запятую, без учёта регистра) | Добавляет группы в **`lite` до первого `tools/list`**. В `full` имена только записываются. Неизвестное имя валит старт. `core` в lite уже есть. |

Пример: `ROSLYN_MCP_TOOL_GROUPS=decompile,nuget`

Примеры OpenCode: [`opencode.json.sample`](opencode.json.sample). Включайте только один сервер.

| Группа | Назначение | Тулов |
| --- | --- | ---: |
| `core` | workspace, поиск символов, build, test, help | 15 (lite по умолчанию) |
| `files` | чтение/поиск/патч с диска, `get_code_skeleton` | 8 |
| `navigation` | иерархия типов, ссылки по известному объявлению, граф вызовов | 3 |
| `editing` | AST, code fixes, format, rename | 8 |
| `decompile` | сторонние сборки | 4 |
| `nuget` | пакеты: list/audit/search/add/remove | 6 |
| `project` | граф solution и rename проекта | 3 |
| `runtime` | `run`, список тестов, сырой `dotnet` | 3 |
| `operations` | логи, scratchpad, stop | 4 |

`list_tool_groups` / `get_tool_help` / `enable_tool_group` — Markdown-справка и runtime-включение. `tools/list` остаётся JSON Schema. Если клиент не обновляет список после `tools/list_changed`, перезапустите с `ROSLYN_MCP_TOOL_GROUPS=<group>`. Замеры minified UTF-8: full **54 / 43 029**; lite **15 / 16 906**.

## История agent-tools по версиям

[CHANGELOG.md](CHANGELOG.md). Правила агента — [`AGENTS.md.sample`](AGENTS.md.sample).

## Cursor: как заставить агента реально вызывать tools

Положите **session policy** в app-репозиторий, иначе клиент часто игнорирует MCP-тулы.

**Канонический файл:** [`AGENTS.md.sample`](AGENTS.md.sample) — скопируйте в приложение как `AGENTS.md` (или влейте во фрагменты `.cursor/rules`). Текст на EN (LLM лучше следуют английским императивам). Это только политика (когда / MCP vs host / запреты); параметры тулов — в Description и в **Reference: MCP Tools** ниже (и в англ. секции).

**OpenCode:** `install2opencode.ps1` (см. блок **OpenCode** выше) сам создаёт или дополняет `AGENTS.md`.

**Maintainers:** при изменении тулов или agent-visible поведения обновляйте **вместе** `AGENTS.md.sample`, [CHANGELOG.md](CHANGELOG.md) и этот README (Reference). Полный текст AGENTS в README не дублировать — только ссылка на sample.

Кратко для модели:

- проверить сервер через `get_mcp_server_info`; отсутствующие `lite`-tools — через `list_tool_groups` / `enable_tool_group`
- опциональный `RoslynMcp.jsonc` (каталог exe, затем cwd; cwd перекрывает). `ROSLYN_MCP_WORKSPACE` в начале процесса ставит cwd на корень репо — без него cwd часто домашний каталог, и файл проекта читается не оттуда. Ключи: `workspace-path`, `configuration`, `platform`, `target-framework`, `max-results`, `preview`, `ripgrep-path`. Нет файла — вызывайте `load_workspace` сами. Первый семантический tool при `workspace-path` грузит решение и ждёт; чужую загрузку не отменяет. `load_workspace` сравнивает только переданные аргументы (опущенные не сбрасывают уже загруженные). Sample: `RoslynMcp.jsonc.sample`
- после `load_workspace` (или lazy из jsonc) объявления C# — MCP `find_symbol_definition` / `find_usages`, не текстовый поиск и не выдуманный `search`
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
- Переменные: `ROSLYN_MCP_WORKSPACE` (корень репо для MSBuild/SDK), `ROSLYN_MCP_TOOL_PROFILE` (`full`/`lite`), `ROSLYN_MCP_TOOL_GROUPS` (`core`, `files`, `navigation`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations`; см. **Профили инструментов**), `ROSLYN_MCP_LOG_TOOL_OUTPUT=full`.

## Reference: MCP Tools

**Имена параметров в JSON:**
- `filePath` — один файл (чтение/правка/диагностика/логи).
- `directoryPath` — корневая папка (`list_directory_tree`, опционально корень для `search_code`).
- `includeExtensions` — опциональный фильтр расширений для `search_code` (по умолчанию `.cs`; `*` = все файлы).
- `caseSensitive` — опционально для `search_code` (по умолчанию `false`; для leftover branding — `true`).
- `workspacePath` — `.sln` / `.slnx` / `.csproj` (и иногда каталог): `load_workspace`, `run_dotnet_test`, `run_specific_test`, `run_test_by_filter`, `run_format`, опциональная перезагрузка в `list_projects` / `get_project_graph`. **`run_dotnet_build` принимает только путь к файлу `.csproj`, `.sln` или `.slnx`, не каталог.** Для multi-config solution предпочитайте `.sln`/`.slnx`.
- `symbolName` — идентификатор C# для `find_symbol_definition`, `find_symbol_references`, `find_usages` и `find_implementations` (точное имя; поиск по solution для definition/usages/implementations и `find_symbol_references` без `filePath` без учёта регистра; `filePath` без `line` у definition и references — порядковое сравнение с учётом регистра).
- `diagnosticId` — id компилятора/анализатора из `get_diagnostics_for_file` (например `CS0246`) для `get_code_fixes` / `apply_code_fix`.
- `fixIndex` — индекс (0-based) из `get_code_fixes` для `apply_code_fix`.
- `path` — файл `.cs` или каталог для `get_code_skeleton` (абсолютный путь; с диска, workspace не обязателен).

Зарегистрировано **54** инструмента в профиле `full` (список ниже). Профиль `lite` стартует с **15** core-тулов; остальные группы — `ROSLYN_MCP_TOOL_GROUPS` или `enable_tool_group`.

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
- `shadowCopyInSolutionAnalyzers: bool = false` — заменяет только те `AnalyzerReference`, которые complete provenance snapshot текущей load-сессии exact-join-ит к одному загруженному in-solution проекту. Filename/`AssemblyName` не считаются evidence; external same-name, unconfirmed и ambiguous ссылки сохраняются без изменения. Недоступный original path — skip (U-ARB-01; `access_failure`; не missing; не rewrite); см. [решение](docs/archive/analyzer-shadow-copy-improvements/v2/u-arb-01-inaccessible-skip.md). Rewrite/fail-closed остаются альтернативами, если полевые репозитории потребуют смены. Missing/Failed/Incomplete или чужой session capture не публикует semantic snapshot (MSBuild graph при этом может открыться); cached `true` переиспользует этот capture — нужны `reset_workspace` и повторный `load_workspace`. Неуспешный opt-in остаётся fail-closed после edit/flush/reconciliation: raw workspace может хранить original refs для persistence, но не публикует и не исполняет их. Активация session-sticky: после включения same-key cached `false`/omitted сохраняет активный overlay без preparation/refresh. Analyzer-проект должен быть предварительно собран. Поколения адресуются по содержимому и повторно применяются при edit без копирования analyzer-файлов. После пересборки генератора с той же assembly identity нужен **новый процесс MCP** (`reset_workspace` не выгружает CLR). Приватные helper DLL отклоняются (main-only). Краткая сводка возвращается в ответе, детали доступны через `tail_tool_log` / `read_log_tail`.

**Активация и побочные эффекты analyzer shadow copy:** флаг opt-in; MCP-клиент и агент автоматически его не включают. Для репозитория с in-solution analyzer/generator и custom `OutputPath` добавьте условное правило в его `AGENTS.md`. При включении versioned DLL/PDB копируются в `v2-main-only/` под OS temp; overlay повторно применяется после обновлений документов. Активация session-sticky: cached `false`/omitted сохраняет уже активный overlay и не обновляет analyzer-файлы; отключение — `reset_workspace`, затем load без флага. Старые поколения (включая timestamp-каталоги) автоматически не очищаются; reset их не удаляет. Реальные `.csproj` и build output не изменяются; на поддержанных overlay-путях MCP-процесс блокирует shadow copy, а не real output. Path resolution и anti-lock независимы. `find_symbol_definition` по имени generated-типа — ограничение Roslyn, не отказ overlay.

**Atomic anti-lock boundary (U-ARB-04):** physical load/cache lookup, opt-in prepare, execution gate и публикация итогового snapshot выполняются как одна сериализованная manager-операция. Production semantic readers ждут эту boundary (`GetPublishedSolutionAsync` / `FindDocumentAsync`) и не компилируют промежуточные raw analyzer references. `GetCurrentSolution()` возвращает только опубликованный snapshot. Если opt-in вошёл первым, конкурентный semantic получает shadow overlay; prepare failure/cancellation новой сессии публикует только fail-closed snapshot и позже не возвращает confirmed real refs. Semantic, завершённый до более позднего enable, остаётся честной no-overlay операцией и может привести identity gate к restart-required. См. [evidence](docs/archive/analyzer-shadow-copy-improvements/v2/u-arb-04-load-boundary-evidence.md), [решение](docs/archive/analyzer-shadow-copy-improvements/v2/u-arb-04-decision.md) и [спецификацию реализации](docs/archive/analyzer-shadow-copy-improvements/v2/u-arb-04-atomic-load-prepare.md).

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

**Заметка:** `load_workspace` не требуется. В `lite` включите группу `files` (или используйте `full`), чтобы увидеть этот tool. Для файла из уже загруженного solution по-прежнему уместен `get_class_skeleton`.
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
<summary><code>find_symbol_references</code> — Ищет использования типа/члена по solution.</summary>

**Параметры:**
- `symbolName: string` — простое имя или точный FQN (`Namespace.Type` / `Namespace.Type.Member`; без `global::`, без `()`); поиск по solution без учёта регистра; при `filePath` без `line` — порядковое сравнение с учётом регистра; при `filePath`+`line` также needle для auto-column без `column`
- `filePath: string?` — опциональный `.cs`; без него — поиск по имени/FQN по solution; обязателен с `line`; без `line` — единственное совпавшее объявление в файле (несколько → ошибка)
- `line: int?` — опциональная 1-based строка; требует `filePath`; резолвит символ в этой позиции
- `column: int?` — опциональная 1-based колонка; без неё — единственный identifier token с именем `symbolName` на строке
- `maxResults: int?` — лимит списка (1–500). По умолчанию 50 или env `ROSLYN_MCP_MAX_RESULTS` (положительное int); явный arg важнее env
- `preview: bool = false` — true: добавить текст строки исходника (до 400 символов); иначе только `path:line:col`
- `overflowCursor: string?` — следующий chunk in-memory overflow (новый поиск не запускается)
- `directOnly: bool = false` — при true (нужен `filePath`) для virtual/abstract/override метода класса оставляет только прямые вызовы; по умолчанию false — те же точки и при необходимости одна строка про virtual dispatch. Межпроектные source/metadata типы сравниваются по полному имени.

**Поведение:** файл без строки покрывает class/struct/record/interface/enum/method/ctor/dtor/property/event/field (не operator/indexer/local function). Несколько совпадений → ошибка со списком FQN и `строка:колонка` идентификатора (без тихого первого). Локации сверх `maxResults` хранятся в процессе за cursor (не тихая обрезка; не `%Temp%` на хосте). Неизвестный/истёкший cursor — человекочитаемая ошибка. Фильтр (если включён) применяется после дедупа и до лимита списка / overflow store.
</details>

<details>
<summary><code>find_symbol_definition</code> — Семантический поиск: где объявлен тип или член (файл, строка, колонка, полное имя) в загруженном solution.</summary>

**Параметры:**
- `symbolName: string` — имя класса, интерфейса, struct, enum или члена (например `IRunCommand`); поиск по solution без учёта регистра; файл без строки — с учётом регистра.
- `filePath: string?` — опционально; без него — поиск по имени по solution; без `line` — единственное совпавшее объявление в файле; обязателен вместе с `line` для позиционного go-to-definition
- `line: int?` — опциональная 1-based строка в `filePath` (usage или объявление)
- `column: int?` — опциональная 1-based колонка; без неё — единственный совпадающий identifier token на строке
- `maxResults: int?` — лимит списка (1–500). По умолчанию 50 или env `ROSLYN_MCP_MAX_RESULTS`; явный arg важнее env
- `overflowCursor: string?` — следующий chunk in-memory overflow; новый поиск не запускается

**Поведение:** локации сверх `maxResults` хранятся в процессе за cursor (не тихая обрезка; не `%Temp%` на хосте). Неизвестный/истёкший cursor — человекочитаемая ошибка. Режим file+line использует ту же схему cursor.

**Для модели:** после `load_workspace` для «где **объявлен** X?» используй этот tool — не текстовый поиск и не выдуманный tool вроде `search`. Для произвольного текста по файлам — встроенный **`grep`** среды (IDE), не `bash`/PowerShell с grep. Так не лезем в `bin/`/`obj/` и опираемся на Roslyn. **Сохранённые** `.cs` (IDE/git) подмешиваются до поиска; несохранённый буфер игнорируется — `reset_workspace` для обычных save не нужен. Без `filePath`/`line` — поиск по имени по solution; `filePath` без `line` — одно объявление в файле (печатает все исходные места, включая части `partial`, каждое с колонкой и полным именем); несколько совпадений → ошибка с FQN и координатами идентификатора; с `filePath`+`line` — определение символа в этой позиции.
</details>

<details>
<summary><code>find_usages</code> — Ссылки по всему solution (<code>path:line:col</code>; опционально preview / overflow).</summary>

**Параметры:**
- `symbolName: string` — простое имя или точный FQN (те же правила, что у `find_symbol_references` без `filePath`).
- `maxResults: int?` — лимит списка (1–500). По умолчанию 50 или env `ROSLYN_MCP_MAX_RESULTS`; явный arg важнее env
- `preview: bool = false` — true: добавить текст строки исходника (до 400 символов)
- `overflowCursor: string?` — следующий chunk in-memory overflow

**Поведение:** нужен `load_workspace`. Name-based alias `find_symbol_references` без `filePath`. Сначала saved `.cs` с диска. Все группы объявлений (без выбора primary). FQN-промах перечисляет кандидатов и не падает на простое имя. Перегрузки — одна FQN-группа. Избыток локаций — тот же overflow store.
</details>

<details>
<summary><code>find_implementations</code> — Классы, реализующие интерфейс, или наследники базового типа.</summary>

**Параметры:**
- `symbolName: string` — имя интерфейса или базового класса (например `IRepository`, `BaseController`). Короткое имя — секция на каждую совпавшую базу; точное FQN — один тип
- `transitive: bool = true` — при `true` включает косвенные реализации / наследников по иерархии
- `maxResults: int?` — лимит списка (1–500). По умолчанию 50 или env `ROSLYN_MCP_MAX_RESULTS`; явный arg важнее env
- `preview: bool = false` — true: добавить текст строки исходника (до 400 символов); иначе только `path:line:col`
- `overflowCursor: string?` — следующий chunk in-memory overflow

**Поведение:** нужен `load_workspace`. Сначала saved `.cs` с диска. Для **интерфейсов** — Roslyn `FindImplementationsAsync`; для **классов/struct** — `FindDerivedClassesAsync`. Несколько одноимённых баз — секция на каждую (пустые секции сохраняются). Точное FQN по-прежнему один тип; промах FQN — текст резолвера с кандидатами (без отката на короткое имя). Каждый тип с `path:line:col`. Избыток — overflow store. Не используйте текстовый поиск или `find_usages` для «кто реализует X?» / «кто наследует Y?».

**Для модели:** после `load_workspace` — вместо grep или анализа usages, когда нужна OOP-иерархия.
</details>

<details>
<summary><code>get_call_graph</code> — Кто вызывает метод и что вызывает он (call graph).</summary>

**Параметры:** `filePath`, `className`, `methodName`, `maxNodes: int = 25`, `includeExternalCallees: bool = false`, `line: int?`, `column: int?` (строка и колонка только вместе)

Нужен `load_workspace`. Без позиции несколько одноимённых методов в классе — ошибка (FQN + line:column), не первый. С `line`+`column` — объявление или место вызова. Для расследования багов — вместо массовой загрузки тел через `get_method_body`.

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

**Поведение:** наследование env + pinning (`MSBUILD_EXE_PATH`, `DOTNET_MSBUILD_SDK_RESOLVER_SDKS_*`). Mismatch → **error `MCP_MSBUILD_SDK_MISMATCH`** + pinned `dotnet exec …/MSBuild.dll /restore`. Цепочка minimal → pinned restore → restore/build detailed. **Итоговый exit** = последний `dotnet build` (не restore). В metadata — `Configuration` / `Platform` / `ProjectName` / `SolutionTarget` / `BuildArgs` / `NoIncremental`. Session `buildArgs` с `load_workspace` добавляются только к build-шагам (не к restore). Внутреннего кэша результатов MCP нет. Пока шаг выполняется, идут MCP progress-heartbeat'ы (метка шага, elapsed, предыдущий exit; без stdout) — это UX, а не обход хост-таймаута `tools/call`.

</details>

<details>
<summary><code>run_dotnet_test</code> — Запускает dotnet test с сокращенным выводом ошибок.</summary>

**Параметры:**
- `workspacePath: string`
- `timeoutSeconds: int = 300` — таймаут процесса; `0` отключает (не рекомендуется). Для долгих интеграционных поднимайте (900/1800).
- `noBuild: bool? = null` — без `binariesPath` пропуск значит false. С `binariesPath` `false` — это `always`, `true` — `never`; вместе с `buildPolicy` не передавать.
- `buildPolicy: string? = null` — `auto`, `always` или `never`. Нужен `binariesPath`. Если не переданы оба параметра, DLL-маршрут берёт `auto`.
- `noRestore: bool = false` — `--no-restore`.
- `configuration: string? = null` — опционально `dotnet test -c` (например `Sit-Debug`). Если не задан — с `load_workspace`.
- `platform: string? = null` — опционально `-p:Platform=`.
- `binariesPath: string? = null` — каталог bin с `{AssemblyName}.dll`. Нужен loaded `.sln`/`.slnx` и `workspacePath` на `.csproj`. При `noBuild=false` сначала `dotnet build <sln> -t`, затем `dotnet test` по DLL. При `noBuild=true` DLL уже должна лежать в этом каталоге.
- `includeFullOutput: bool = false` — полный Standard Output/Error упавшего теста до 100000 символов на поток. По умолчанию `false` — бюджет `maxOutputChars`.
- `maxOutputChars: int = 0` — бюджет StdOut на один failed test. `0` = 2500 (голова 1600 + хвост 700), или 100000 при `includeFullOutput`. Потолок 100000. StdErr масштабируется.

**Поведение:** при `noBuild=false` и без `binariesPath` сначала отдельный incremental `dotnet build` (те же `-c` / platform / session `buildArgs` с `load_workspace`), затем `dotnet test --no-build --no-restore` (парсер видит только тест). С `binariesPath` сборка идёт через solution `-t`, цель теста — DLL. Сводка из `Passed!`, `Test Run Successful` + `Total tests`/`Passed:` (Failed=0 если нет строки), или `.slnx` fail-only `Total tests` + `Failed:` (Passed = Total − Failed − Skipped), FQN-строки тестов (`[ms]` / `[1 s]` / `[1 m 28 s]`); `Error Message:` многострочный (head+tail); StdOut/StdErr отдельными блоками (по умолчанию 2500/1000, head+tail; `includeFullOutput` / `maxOutputChars` поднимают лимит); футер VSTest `Build FAILED` / `0 Error(s)` не считается ошибкой компиляции; дедуп NU audit. Без маркеров сводки при exit 0 → **partial** + 2KB лога (футер срезан). `timeoutSeconds` — общий бюджет на build+test. Рядом с DLL нужны `.runtimeconfig.json` / `.deps.json`. Пока шаг выполняется, идут MCP progress-heartbeat'ы (`dotnet build` / `dotnet test`, без stdout) — UX, не обход хост-таймаута `tools/call`.

</details>

<details>
<summary><code>run_specific_test</code> — dotnet test с фильтром по классу и/или методу.</summary>

**Параметры:**
- `workspacePath: string`
- `className: string?` — например `UserServiceTests`
- `methodName: string?` — например `CreateUser_WhenValid_ReturnsOk`
- `timeoutSeconds: int = 300` — как у `run_dotnet_test`.
- `noBuild: bool? = null` — без `binariesPath` пропуск значит false. С `binariesPath` `false` — это `always`, `true` — `never`; вместе с `buildPolicy` не передавать.
- `buildPolicy: string? = null` — та же политика DLL-маршрута, что у `run_dotnet_test`. Нужен `binariesPath`.
- `noRestore: bool = false` — `--no-restore`.
- `configuration: string? = null` — опционально `dotnet test -c` (как у `run_dotnet_test`).
- `platform: string? = null` — опционально `-p:Platform=`.
- `binariesPath: string? = null` — как у `run_dotnet_test`: каталог bin с `{AssemblyName}.dll`; loaded `.sln`/`.slnx` и `workspacePath` на `.csproj`. `noBuild=false` собирает через solution `-t`.
- `includeFullOutput: bool = false` — как у `run_dotnet_test`.
- `maxOutputChars: int = 0` — как у `run_dotnet_test`.

Нужен хотя бы один из `className` / `methodName`. Tool строит VSTest-safe `--filter` (`FullyQualifiedName~…`, без `()` у метода, без лишней ведущей `.` на dotted FQN). После `load_workspace` Roslyn по возможности резолвит FQN типа/метода. Строка `Passed` только с именем метода или `Method(args)` — это успех, не **no matching tests**.

**Для модели:** TDD/фикс бага — этот tool, не полный suite. Сырой VSTest `--filter` (`TestCategory=Smoke`, `FullyQualifiedName~…`) — `run_test_by_filter`. Предпочитайте `className` + короткое `methodName`. После билда предпочитайте `noBuild=true`. При `noBuild=false` — тот же split build/test, что у `run_dotnet_test`. Проекты, которые собираются только внутри своего `.sln`, передавайте с `binariesPath` и `.csproj`.

</details>

<details>
<summary><code>run_test_by_filter</code> — dotnet test с сырым VSTest <code>--filter</code>.</summary>

**Параметры:**
- `workspacePath: string` — `.csproj`, `.sln`, `.slnx` или каталог тестового проекта (как у `run_dotnet_test`).
- `filter: string` — передаётся в `--filter` (например `FullyQualifiedName~MyClass`, `FullyQualifiedName~CreateUser`, `TestCategory=Smoke`). Пустой — ошибка. Не ставьте `()` у метода.
- `timeoutSeconds: int = 300` — как у `run_dotnet_test`.
- `noBuild: bool? = null` — без `binariesPath` пропуск значит true (по умолчанию **true**, в отличие от остальных test tools). С `binariesPath` `false` — это `always`, `true` — `never`; вместе с `buildPolicy` не передавать.
- `buildPolicy: string? = null` — та же политика DLL-маршрута, что у `run_dotnet_test`. Нужен `binariesPath`.
- `noRestore: bool = false` — `--no-restore`.
- `configuration: string? = null` — опционально `dotnet test -c`. Если не задан — с `load_workspace`.
- `platform: string? = null` — опционально `-p:Platform=`.
- `binariesPath: string? = null` — каталог bin с test DLL. Нужен loaded `.sln`/`.slnx` и `workspacePath` на `.csproj`. Запускает `{AssemblyName}.dll` из этого каталога. При `noBuild=false` сначала solution `-t`; при `noBuild=true` DLL уже должна существовать.
- `includeFullOutput: bool = false` — как у `run_dotnet_test`.
- `maxOutputChars: int = 0` — как у `run_dotnet_test`.

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
<summary><code>search_code</code> — Текстовый поиск (по умолчанию управляемый обход; опционально ripgrep).</summary>

**Параметры:**
- `pattern: string`
- `directoryPath: string? = null`
- `includeExtensions: string? = ".cs"` — список через запятую/`;` (`.cs,.csproj,.json`) или `*` для всех файлов.
- `useRegex: bool = false` — .NET regex для управляемого обхода; синтаксис ripgrep (Rust) при `useRipgrep: true`.
- `caseSensitive: bool = false` — по умолчанию без учёта регистра; для leftover branding — `true`.
- `maxResults: int = 50`
- `maxScanSeconds: int = 20`
- `useRipgrep: bool = false` — при `true` только `rg` (без fallback на управляемый обход). Отсутствие `rg` — ошибка только с этим флагом.
- `ripgrepPath: string? = null` — полный путь к `rg`/`rg.exe`; учитывается только с `useRipgrep=true` (иначе игнорируется). Без аргумента — `ripgrep-path` из `RoslynMcp.jsonc` или PATH.

**Для агента:** если в клиенте есть встроенный **`grep`**, для обычного текстового поиска предпочитай его (не grep из терминала). Этот MCP-tool — когда нужен поиск из процесса Roslyn MCP. Не предполагай, что ripgrep установлен.
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
- `line: int?` — необязательная 1-based строка; только вместе с `column`
- `column: int?` — необязательная 1-based колонка; только вместе с `line`

**Область:** только C# символы. Для папки проекта / `.csproj` / графа solution — `rename_project`. Для README/rules/URL — host Grep/edit.

**Поведение:** без позиции несколько одноимённых объявлений в файле — ошибка (FQN + line:column), запись на диск не выполняется. С `line`+`column` — объявление или использование в этом файле. Сохранение идёт через тот же write boundary, что и `apply_code_fix`. Неподдержанный analyzer-reference diff отклоняется до записи. Частичное сохранение сообщает Status, Reason и известные пути — это не полный успех rename. Предпросмотр по-прежнему по умолчанию.
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

После `dotnet publish` — проверка, что MCP подхватил новый бинарник (ожидай **v1.3.32** и **63** tools в `full`, или **19** в `lite`).

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

## Лицензия
Этот проект распространяется под лицензией MIT — подробности см. в файле [LICENSE](LICENSE).
