# RoslynMcpServer

[🇷🇺 Читать на русском (Russian Version)](#russian-version)

**Why use this over standard FileSystem MCPs?**
Unlike basic file-reading servers, this Model Context Protocol (MCP) server leverages **Roslyn**. Your AI agent (Cursor, Cline, etc.) doesn't just read plain text—it sees C# code through the eyes of the compiler. It can get precise diagnostics without a full rebuild, **apply built-in Roslyn code fixes** (add using, implement interface, fix typos), find symbol references, and perform safe semantic refactoring, drastically reducing LLM hallucinations.

It is designed for AI-driven C# development (with secondary Python support), focusing on:
- Parsing and analyzing `*.sln`/`*.slnx`/`*.csproj` via Roslyn.
- Safe, context-aware file read/write operations.
- CLI integration: running `dotnet build`, `dotnet test`, and custom commands.
- Compact responses optimized for LLM context windows, backed by detailed server-side logging.

## ⚠️ Security Disclaimer
This server grants the AI agent read, write, and execution privileges (via the `dotnet` CLI) within your workspace. **Always use source control (Git)** to track changes. Do not run the server as Administrator.

## Prerequisites
- Strictly **.NET 10 SDK** installed on your machine. *(Earlier SDK versions are not supported and will fail to build).*
- An MCP-compatible IDE or client (e.g., Cursor, OpenCode, VS Code with Cline).

## Setup & Connection (Local)

It is highly recommended to run the **published executable**. This ensures instant startup without the overhead of `dotnet run`. The project is configured with `ReadyToRun` enabled and `PublishSingleFile` disabled (to ensure Roslyn can dynamically load MSBuild and analyzer dependencies).

### 1. Publish the Server (Run once or after code updates)
From the repository root (replace the RID if you are not on Windows x64):

```bash
dotnet publish RoslynMcpServer.csproj -c Release -r win-x64
```

Your compiled binary will be at:
- Windows x64: `bin/Release/net10.0/win-x64/publish/RoslynMcpServer.exe`
- Linux/macOS: Same relative path under your specified `-r` flag.

### 2. Configure the MCP Client

**Option A: Via Cursor UI**
1. Go to **Cursor Settings** -> **Features** -> **MCP** -> **+ Add New MCP Server**.
2. Type: `stdio`.
3. Command: Enter the **absolute path** to your published executable (e.g., `D:\Devel\RoslynMcpServer\bin\Release\net10.0\win-x64\publish\RoslynMcpServer.exe`).

**Option B: Via `mcp.json`**
Add the absolute path to your configuration file (see the provided `mcp.json` and `.cursor/mcp.json` examples in the repo).
*Optional:* Pass `env` with `ROSLYN_MCP_WORKSPACE` set to the **repository root** (folder containing `global.json`) so MSBuild.Locator and `run_dotnet_build` use the same SDK as your solution.

**Option C: OpenCode (`install2opencode.ps1`)**

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

## Agent tools by version

Tracks MCP tools relevant to [`AGENTS.md.sample`](AGENTS.md.sample) (copy into app repos as `AGENTS.md`). Current server version: see `RoslynMcpServer.csproj`.

### v1.0.22

| Tool / behavior | Notes |
|-----------------|--------|
| `run_specific_test` method filter | VSTest-safe filters: Roslyn method FQN without `()`; always `FullyQualifiedName~` (not `=`); no bogus leading `.` when `methodName` is already a dotted FQN; escape `\ ( ) & \| = ! ~` |

### v1.0.21

| Tool / behavior | Notes |
|-----------------|--------|
| `load_workspace` soft prune | Unused `PackageReference` / NuGet prune advisories (`will not be pruned`, prune package data) remapped to warnings — do not fail load when projects opened |

### v1.0.20

| Tool / behavior | Notes |
|-----------------|--------|
| `configuration` | Optional `configuration` (`dotnet -c`) on `run_dotnet_build`, `run_dotnet_test`, `run_specific_test` for multi-config solutions (`Sit-Debug`, `Dit-Debug`, …) |

### v1.0.19

| Tool / behavior | Notes |
|-----------------|--------|
| `.slnx` support | `load_workspace`, `run_dotnet_build`, `run_dotnet_test` / `run_specific_test`, discovery, and NuGet/format paths accept `.slnx`; prefer solution files for multi-config repos |
| OpenCode host timeout | Document + `opencode.json.sample` / `install2opencode.ps1`: `"timeout": 600000` ms — avoids MCP `-32001` (~60s); separate from tool `timeoutSeconds` |

### v1.0.18

| Tool / behavior | Notes |
|-----------------|--------|
| Tool descriptions | Accurate agent-facing `[Description]` across build/test/decompile/NuGet/navigation/AST params |

### v1.0.17

| Tool / behavior | Notes |
|-----------------|--------|
| `run_dotnet_test` / `run_specific_test` | Optional `noBuild` / `noRestore` (`--no-build` / `--no-restore`); after build use `noBuild=true` for faster re-runs |

### v1.0.16

| Tool / behavior | Notes |
|-----------------|--------|
| `run_dotnet_test` / `run_specific_test` | `timeoutSeconds` default **300**; kill process tree on timeout/cancel |
| `run_dotnet_build` probe | Overall wall-clock budget (~300s) + per-step timeout; skip escalate when budget exhausted |
| `execute_dotnet_command` | Same default timeout + kill on cancel |
| Silent fail UX | Hints for zombie `dotnet` / locked `obj` when exit≠0 and no parsed diagnostics |

### v1.0.15

| Tool / behavior | Notes |
|-----------------|--------|
| `search_code` | `caseSensitive` (default `false`); leftover branding → `caseSensitive=true` |
| No-workspace UX | Semantic tools list candidate `.sln`/`.slnx` under `ROSLYN_MCP_WORKSPACE` / cwd (no auto-load) |
| `rename_project` | SDK-style dir+csproj+ProjectReference+.sln/.slnx; `dryRun`; no namespace chain |
| Branding recipe | Documented in `AGENTS.md.sample` (hybrid MCP + host edit) |

### v1.0.14

| Tool / behavior | Notes |
|-----------------|--------|
| `run_dotnet_run` | SDK-pinned `dotnet run`, separate stdout/stderr, timeout, truncated output (stderr tail for progress) |
| `run_nuget_audit` | Structured vulnerability table from `dotnet list package --vulnerable` |
| `get_changed_files` | Git porcelain status + suggested test projects (no diff body) |
| `load_workspace` | **Workspace health** block: SDK/global.json, restore assets, tool count |
| `execute_dotnet_command` | SDK pinning + truncated stdout/stderr |
| `find_usages` / `find_symbol_references` | **find_references** family; prefer `find_usages` when only `symbolName` is known |
| `get_project_graph` / `list_projects` | Project dependency graph |
| `rename_symbol` | `previewOnly=true` default workflow; **C# symbols only** |
| `run_format` | `dotnet format` wrapper |

### v1.0.13

| Tool / behavior | Notes |
|-----------------|--------|
| `AssemblyReferenceResolver` | Exact `{name}.dll`; deps.json + NuGet fallback |
| `DecompilerHost` | NuGet / BCL / runtime pack resolver for ILSpy tools |

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

- `load_workspace` before symbol / build / decompile work; prefer `.sln` / `.slnx`
- C# identifiers → MCP first; plain text → host Grep; never shell `grep` / `dotnet build|test`
- IDE: host edit/write; headless: MCP `apply_patch` / AST tools
- Secrets: never paste PAT/passwords; app README must document run target / sample args

## Logs
- **Main log:** `logs/mcp-*.log` (relative to `AppContext.BaseDirectory`).
- Global incoming JSON-RPC logging is enabled by default.
- **Tool output:** logged as a one-line summary plus separate warning/error lines (not a duplicated full MCP response). Set `ROSLYN_MCP_LOG_TOOL_OUTPUT=full` to log entire tool responses at Information level.
- Environment Variables:
  - `ROSLYN_MCP_WORKSPACE` — repo root for MSBuild/SDK discovery at startup (see MCP config above).
  - `ROSLYN_MCP_LOG_TOOL_OUTPUT=full` — verbose tool response logging.
  - `MCP_LOG_INCOMING_RPC=0` (disable incoming RPC logging).
  - `MCP_LOG_INCOMING_RPC_MAX_CHARS=<N>` (limit payload log length, `0` = unlimited).

## Reference: MCP Tools

**Parameter Naming Rules:**
- `filePath` — a single file (read/edit/diagnostics/logs).
- `directoryPath` — root folder (`list_directory_tree`, optional root for `search_code`).
- `includeExtensions` — optional extension filter for `search_code` (`.cs` by default; `*` = all files).
- `caseSensitive` — optional for `search_code` (default `false`; use `true` for leftover branding checks).
- `workspacePath` — `.sln` / `.slnx` / `.csproj` (and sometimes a directory): `load_workspace`, `run_dotnet_test`, `run_specific_test`, `run_format`, optional reload for `list_projects` / `get_project_graph`. **`run_dotnet_build` accepts only a `.csproj`, `.sln`, or `.slnx` file path, not a directory.** Prefer `.sln`/`.slnx` for multi-config solutions.
- `symbolName` — C# identifier for `find_symbol_definition`, `find_symbol_references`, `find_usages`, and `find_implementations` (exact name; matching is case-insensitive for definition/usages/implementations).
- `diagnosticId` — compiler/analyzer id from `get_diagnostics_for_file` (e.g. `CS0246`) for `get_code_fixes` / `apply_code_fix`.
- `fixIndex` — 0-based index from `get_code_fixes` for `apply_code_fix`.
- `path` — `.cs` file or directory for `get_code_skeleton` (absolute path; disk-based, no workspace required).

When a tool accepts `filePath`, relative values are resolved against the loaded workspace root after `load_workspace`; if no workspace is loaded, fallback is `Environment.CurrentDirectory`.

There are **56** registered tools (see list below) and **1** MCP prompt (`RefactoringAssistantPrompt`).

### Workspace / Roslyn

<details>
<summary><code>load_workspace</code> — Loads .sln/.slnx/.csproj into MSBuildWorkspace.</summary>

**Parameters:**
- `workspacePath: string` — `.sln`, `.slnx`, or `.csproj` file (not a directory). Prefer solution files for multi-config repos.
</details>

<details>
<summary><code>reset_workspace</code> — Clears the in-memory MSBuildWorkspace/solution cache. Call before <code>load_workspace</code> again after building the loaded solution on disk.</summary>

**Parameters:** *(none)*
</details>

<details>
<summary><code>get_file_content</code> — Reads the entire file (safe preview for large files).</summary>

**Parameters:**
- `filePath: string`
</details>

<details>
<summary><code>get_class_skeleton</code> — Returns the C# file structure without method bodies.</summary>

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
<summary><code>get_diagnostics_for_file</code> — Returns Roslyn compiler diagnostics for a single file (Warning and Error only).</summary>

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

**Behavior:** writes changed files to disk and updates the in-memory workspace. Re-run `get_diagnostics_for_file` to verify remaining issues.

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

**Model guidance:** after `load_workspace`, use this for “where is X **declared**?” — do **not** answer that with plain-text search or invent a generic tool named `search`. For free-text matches across files, use your client’s built-in **`grep`** tool (not `bash`/`PowerShell` grep). This tool avoids `bin/`/`obj/` and uses Roslyn.
</details>

<details>
<summary><code>find_usages</code> — Solution-wide references for a declared name: file, line, and source line text (capped at 30 locations).</summary>

**Parameters:**
- `symbolName: string` — declared name of the type or member (e.g. `Guard`, `Format`).

**Behavior:** Requires `load_workspace`. Resolves declarations via Roslyn; if several symbols share the name, one primary symbol is chosen (types preferred over methods, then stable ordering). When you already know the declaring file, `find_symbol_references` may be more precise.
</details>

<details>
<summary><code>find_implementations</code> — Find classes implementing an interface or derived from a base type.</summary>

**Parameters:**
- `symbolName: string` — interface or base class name (e.g. `IRepository`, `BaseController`)
- `transitive: bool = true` — when `true`, includes indirect implementations / derived types in the hierarchy

**Behavior:** Requires `load_workspace`. For **interfaces**, uses Roslyn `FindImplementationsAsync`; for **classes/structs**, uses `FindDerivedClassesAsync`. Returns each matching type with file path and line (capped at 50). Do not use text search or `find_usages` for “who implements X?” / “what inherits from Y?”.

**Model guidance:** after `load_workspace`, use this instead of grep or analyzing usages when you need the OOP hierarchy.
</details>

<details>
<summary><code>get_call_graph</code> — Callers and callees for a method.</summary>

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
<summary><code>update_file_content</code> — Completely overwrites a file. Creates missing directories automatically.</summary>

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
</details>

### Build / Test / CLI

<details>
<summary><code>run_dotnet_build</code> — Runs dotnet build and returns a compact diagnostic summary.</summary>

**Parameters:**
- `workspacePath: string` — must be an existing **`.csproj`, `.sln`, or `.slnx` file** (not a directory).
- `configuration: string? = null` — optional `dotnet build -c` (e.g. `Sit-Debug`, `Dit-Debug`). Omit only when a single default config is correct.

**Behavior:** Inherits full process env, then pins SDK via `MSBUILD_EXE_PATH`, `MSBuildSDKsPath`, `DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR` / `SDKS_VER` / `CLI_DIR`, `DOTNET_ROOT`. On MSBuild path mismatch → **`error MCP_MSBUILD_SDK_MISMATCH`** and `dotnet exec …/10.x/MSBuild.dll /restore`. Escalation: minimal build → pinned restore → restore (detailed if empty) → build normal → build detailed. **Key lines** include task `-- FAILED` with project context.

</details>

<details>
<summary><code>run_dotnet_test</code> — Runs dotnet test with condensed failure details.</summary>

**Parameters:**
- `workspacePath: string`
- `timeoutSeconds: int = 300` — process timeout; `0` disables (not recommended). Raise for long integration tests (e.g. 900/1800).
- `noBuild: bool = false` — pass `--no-build` (after a successful `run_dotnet_build`).
- `noRestore: bool = false` — pass `--no-restore`.
- `configuration: string? = null` — optional `dotnet test -c` (e.g. `Sit-Debug`). Use the same value as `run_dotnet_build` when `noBuild=true`.

**Behavior:** `--logger "console;verbosity=normal"`. Summary from `Passed!`, `Test Run Successful` + `Total tests`/`Passed:` (Failed defaults to 0), or per-test `  Passed FQN [ms]`. MSBuild/prune noise ignored; duplicate NU audit lines deduped. Exit 0 without any summary marker → **`Status: partial`** + last 2KB. `run_specific_test` checks filter matched a test FQN.

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

At least one of `className` or `methodName` is required. The tool builds a VSTest-safe `--filter` internally (`FullyQualifiedName~…`, no method `()`, no extra leading `.` on dotted names). After `load_workspace`, Roslyn resolves the type/method FQN when possible.

**Model guidance:** use this for TDD red/green loops — do not run the full suite and do not craft VSTest filter strings manually. Prefer `className` + short `methodName`. After build, prefer `noBuild=true` for faster filtered re-runs.

</details>

<details>
<summary><code>get_test_list</code> — List test methods in loaded solution (JSON).</summary>

**Parameters:** `maxResults: int = 200`

Detects Fact/Theory/TestMethod/etc. Requires `load_workspace`.

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
<summary><code>run_format</code> — Runs dotnet format (apply or verify-only).</summary>

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

Use after `dotnet publish` to verify the MCP host picked up the new binary (expect **56** tools).

</details>

<details>
<summary><code>stop_mcp_server</code> — Stops this MCP host process after returning (for rebuilding the server binary; restart MCP in the IDE).</summary>

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

**Почему это лучше стандартных файловых MCP?**
В отличие от базовых серверов, которые умеют только читать и писать файлы, этот сервер использует **Roslyn**. Ваш ИИ-агент (в Cursor, Cline и др.) не просто читает текст, он видит C#-код глазами компилятора. Он может получать точечную диагностику без полной пересборки, **применять встроенные Roslyn code fixes** (add using, implement interface, опечатки), искать ссылки на символы и делать безопасный семантический рефакторинг, что кардинально снижает риск галлюцинаций.

Сервер работает по `stdio` и регистрирует инструменты через MCP C# SDK, фокусируясь на:
- работе с `*.sln`/`*.slnx`/`*.csproj` через Roslyn;
- безопасных операциях чтения/правки файлов;
- запуске `dotnet build` / `dotnet test` / произвольных `dotnet` команд;
- компактных ответах для LLM и подробных логах.

## ⚠️ Предупреждение о безопасности
Этот сервер предоставляет ИИ-агенту права на чтение, запись и выполнение консольных команд (`dotnet`) в вашем рабочем пространстве. **Всегда используйте системы контроля версий (Git)**. Не запускайте сервер от имени администратора.

## Требования
- Строго **.NET 10 SDK**. *(Сборка более старыми версиями SDK не поддерживается и завершится ошибкой).*
- IDE с поддержкой MCP (Cursor, OpenCode, VS Code + Cline).

## Подключение к Cursor / VS Code (локально)

Рекомендуется запускать **собранный exe**. Это обеспечивает мгновенный старт без оверхеда от `dotnet run`. Проект настроен с флагом `ReadyToRun`, но **без** `PublishSingleFile` (чтобы Roslyn мог динамически загружать зависимости).

### 1. Собрать publish (один раз и после изменений)

```bash
dotnet publish RoslynMcpServer.csproj -c Release -r win-x64
```

Готовый exe:
- Windows x64: `bin/Release/net10.0/win-x64/publish/RoslynMcpServer.exe`
- Linux/macOS: тот же относительный путь под ваш `-r`.

### 2. Конфиг MCP

**Через интерфейс Cursor:**
1. Перейдите в **Cursor Settings** -> **Features** -> **MCP** -> **+ Add New MCP Server**.
2. Выберите тип: `stdio`.
3. Вставьте **абсолютный путь** к `RoslynMcpServer.exe`.

**Через файл конфига:**
Укажите абсолютный путь (примеры лежат в `mcp.json` и `.cursor/mcp.json`).
Опционально добавьте `env` с `ROSLYN_MCP_WORKSPACE` — **корень репозитория** (каталог с `global.json`), чтобы MSBuild.Locator и `run_dotnet_build` использовали тот же SDK, что и решение.

**OpenCode (`install2opencode.ps1`):**

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

## История agent-tools по версиям

См. английский раздел [Agent tools by version](#agent-tools-by-version) (таблицы v1.0.13–v1.0.22). Правила агента — [`AGENTS.md.sample`](AGENTS.md.sample).

## Cursor: как заставить агента реально вызывать tools

Положите **session policy** в app-репозиторий, иначе клиент часто игнорирует MCP-тулы.

**Канонический файл:** [`AGENTS.md.sample`](AGENTS.md.sample) — скопируйте в приложение как `AGENTS.md` (или влейте во фрагменты `.cursor/rules`). Текст на EN (LLM лучше следуют английским императивам). Это только политика (когда / MCP vs host / запреты); параметры тулов — в Description и в **Reference: MCP Tools** ниже (и в англ. секции).

**OpenCode:** `install2opencode.ps1` (см. блок **OpenCode** выше) сам создаёт или дополняет `AGENTS.md`.

**Maintainers:** при изменении тулов или agent-visible поведения обновляйте **вместе** `AGENTS.md.sample` и этот README («Agent tools by version» + Reference). Полный текст AGENTS в README не дублировать — только ссылка на sample.

Кратко для модели:

- после `load_workspace` объявления C# — MCP `find_symbol_definition` / `find_usages`, не текстовый поиск и не выдуманный `search`
- текст по файлам — host Grep IDE, не shell grep
- сборка/тесты — только MCP `run_dotnet_build` / `run_dotnet_test`
- запись файлов — IDE: нативные правки; headless: MCP `apply_patch` / AST
- полный текст политики — в `AGENTS.md.sample`

## Логи
- Основной лог: `logs/mcp-*.log` (относительно `AppContext.BaseDirectory`).
- Включено логирование входящих JSON-RPC сообщений (`MCP_LOG_INCOMING_RPC`, `MCP_LOG_INCOMING_RPC_MAX_CHARS`).
- **Ответы tools:** в лог пишется однострочная сводка и отдельные строки warning/error (без полного дублирования ответа MCP). `ROSLYN_MCP_LOG_TOOL_OUTPUT=full` — полный текст ответов tools.
- Переменные: `ROSLYN_MCP_WORKSPACE` (корень репо для MSBuild/SDK), `ROSLYN_MCP_LOG_TOOL_OUTPUT=full`.

## Reference: MCP Tools

**Имена параметров в JSON:**
- `filePath` — один файл (чтение/правка/диагностика/логи).
- `directoryPath` — корневая папка (`list_directory_tree`, опционально корень для `search_code`).
- `includeExtensions` — опциональный фильтр расширений для `search_code` (по умолчанию `.cs`; `*` = все файлы).
- `caseSensitive` — опционально для `search_code` (по умолчанию `false`; для leftover branding — `true`).
- `workspacePath` — `.sln` / `.slnx` / `.csproj` (и иногда каталог): `load_workspace`, `run_dotnet_test`, `run_specific_test`, `run_format`, опциональная перезагрузка в `list_projects` / `get_project_graph`. **`run_dotnet_build` принимает только путь к файлу `.csproj`, `.sln` или `.slnx`, не каталог.** Для multi-config solution предпочитайте `.sln`/`.slnx`.
- `symbolName` — идентификатор C# для `find_symbol_definition`, `find_symbol_references`, `find_usages` и `find_implementations` (точное имя; регистр не важен для definition/usages/implementations).
- `diagnosticId` — id компилятора/анализатора из `get_diagnostics_for_file` (например `CS0246`) для `get_code_fixes` / `apply_code_fix`.
- `fixIndex` — индекс (0-based) из `get_code_fixes` для `apply_code_fix`.
- `path` — файл `.cs` или каталог для `get_code_skeleton` (абсолютный путь; с диска, workspace не обязателен).

Зарегистрировано **56** инструментов (список ниже) и **1** MCP-промпт (`RefactoringAssistantPrompt`).

### Workspace / Roslyn

<details>
<summary><code>load_workspace</code> — Загружает .sln/.slnx/.csproj в MSBuildWorkspace.</summary>

**Параметры:**
- `workspacePath: string` — файл `.sln`, `.slnx` или `.csproj` (не каталог). Для multi-config — предпочтительно solution.
</details>

<details>
<summary><code>reset_workspace</code> — Сбрасывает in-memory MSBuildWorkspace и кэш решения. После сборки загруженного решения на диске вызови снова <code>load_workspace</code>.</summary>

**Параметры:** *(нет)*
</details>

<details>
<summary><code>get_file_content</code> — Читает файл целиком (возвращает безопасный preview для больших файлов).</summary>

**Параметры:**
- `filePath: string`
</details>

<details>
<summary><code>get_class_skeleton</code> — Возвращает структуру C# файла без тел методов.</summary>

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
<summary><code>get_diagnostics_for_file</code> — Возвращает Roslyn-диагностику для одного C# файла (только Warning и Error).</summary>

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

**Поведение:** записывает изменённые файлы на диск и обновляет in-memory workspace. После применения вызовите `get_diagnostics_for_file` для проверки.

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

**Для модели:** после `load_workspace` для «где **объявлен** X?» используй этот tool — не текстовый поиск и не выдуманный tool вроде `search`. Для произвольного текста по файлам — встроенный **`grep`** среды (IDE), не `bash`/PowerShell с grep. Так не лезем в `bin/`/`obj/` и опираемся на Roslyn.
</details>

<details>
<summary><code>find_usages</code> — Ссылки по всему solution: файл, строка и текст строки исходника (не более 30 вхождений).</summary>

**Параметры:**
- `symbolName: string` — объявленное имя типа или члена (например `Guard`, `Format`).

**Поведение:** нужен `load_workspace`. Поиск объявлений через Roslyn; при нескольких символах с одним именем выбирается один «основной» (типы предпочтительнее методов). Если известен файл объявления, точнее может быть `find_symbol_references`.
</details>

<details>
<summary><code>find_implementations</code> — Классы, реализующие интерфейс, или наследники базового типа.</summary>

**Параметры:**
- `symbolName: string` — имя интерфейса или базового класса (например `IRepository`, `BaseController`)
- `transitive: bool = true` — при `true` включает косвенные реализации / наследников по иерархии

**Поведение:** нужен `load_workspace`. Для **интерфейсов** — Roslyn `FindImplementationsAsync`; для **классов/struct** — `FindDerivedClassesAsync`. Каждый тип с путём к файлу и строкой (не более 50). Не используйте текстовый поиск или `find_usages` для «кто реализует X?» / «кто наследует Y?».

**Для модели:** после `load_workspace` — вместо grep или анализа usages, когда нужна OOP-иерархия.
</details>

<details>
<summary><code>get_call_graph</code> — Кто вызывает метод и что вызывает он (call graph).</summary>

**Параметры:** `filePath`, `className`, `methodName`, `maxNodes: int = 25`, `includeExternalCallees: bool = false`

Нужен `load_workspace`. Для расследования багов — вместо массовой загрузки тел через `get_method_body`.

</details>

### File Editing

<details>
<summary><code>update_file_content</code> — Полная перезапись файла. Автоматически создает отсутствующие каталоги.</summary>

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
</details>

### Build / Test / CLI

<details>
<summary><code>run_dotnet_build</code> — Запускает dotnet build и возвращает компактную сводку.</summary>

**Параметры:**
- `workspacePath: string` — только существующий **файл** `.csproj`, `.sln` или `.slnx` (не каталог).
- `configuration: string? = null` — опционально `dotnet build -c` (например `Sit-Debug`, `Dit-Debug`). Не опускайте на multi-config `.slnx`.

**Поведение:** наследование env + pinning (`MSBUILD_EXE_PATH`, `DOTNET_MSBUILD_SDK_RESOLVER_SDKS_*`). Mismatch → **error `MCP_MSBUILD_SDK_MISMATCH`** + pinned `dotnet exec …/MSBuild.dll /restore`. Цепочка minimal → pinned restore → restore/build detailed. **Key lines** с проектом у `-- FAILED`.

</details>

<details>
<summary><code>run_dotnet_test</code> — Запускает dotnet test с сокращенным выводом ошибок.</summary>

**Параметры:**
- `workspacePath: string`
- `timeoutSeconds: int = 300` — таймаут процесса; `0` отключает (не рекомендуется). Для долгих интеграционных поднимайте (900/1800).
- `noBuild: bool = false` — `--no-build` (после успешного `run_dotnet_build`).
- `noRestore: bool = false` — `--no-restore`.
- `configuration: string? = null` — опционально `dotnet test -c` (например `Sit-Debug`). При `noBuild=true` — тот же config, что у build.

**Поведение:** сводка из `Passed!`, `Test Run Successful` + `Total tests`/`Passed:` (Failed=0 если нет строки), FQN-строки тестов; дедуп NU audit. Без маркеров сводки при exit 0 → **partial** + 2KB лога.

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

Нужен хотя бы один из `className` / `methodName`. Tool строит VSTest-safe `--filter` (`FullyQualifiedName~…`, без `()` у метода, без лишней ведущей `.` на dotted FQN). После `load_workspace` Roslyn по возможности резолвит FQN типа/метода.

**Для модели:** TDD/фикс бага — этот tool, не полный suite и не ручной VSTest filter. Предпочитайте `className` + короткое `methodName`. После билда предпочитайте `noBuild=true`.

</details>

<details>
<summary><code>get_test_list</code> — Список тестов в solution (JSON).</summary>

**Параметры:** `maxResults: int = 200`. Нужен `load_workspace`.

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
<summary><code>run_format</code> — Запускает dotnet format (применение или verify-only).</summary>

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

После `dotnet publish` — проверка, что MCP подхватил новый бинарник (ожидай **56** tools).

</details>

<details>
<summary><code>stop_mcp_server</code> — Завершает процесс MCP после ответа (чтобы пересобрать бинарник сервера; затем перезапуск MCP в IDE).</summary>

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