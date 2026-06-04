# RoslynMcpServer

[🇷🇺 Читать на русском (Russian Version)](#russian-version)

**Why use this over standard FileSystem MCPs?**
Unlike basic file-reading servers, this Model Context Protocol (MCP) server leverages **Roslyn**. Your AI agent (Cursor, Cline, etc.) doesn't just read plain text—it sees C# code through the eyes of the compiler. It can get precise diagnostics without a full rebuild, **apply built-in Roslyn code fixes** (add using, implement interface, fix typos), find symbol references, and perform safe semantic refactoring, drastically reducing LLM hallucinations.

It is designed for AI-driven C# development (with secondary Python support), focusing on:
- Parsing and analyzing `*.sln`/`*.csproj` via Roslyn.
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

- Creates or updates **`opencode.json`** in the target directory, registering `RoslynMcpServer` as a local MCP server (`type: local`, `enabled: true`).
- Creates or updates **`AGENTS.md`**: if Roslyn MCP behavioral rules are not already present, merges content from the bundled `AGENTS.md.sample` (creates a new file or appends to an existing one).

| Parameter | Description |
| --- | --- |
| `-BinaryPath` | Optional. Absolute path to `RoslynMcpServer.exe`. When omitted, the script uses the binary next to itself. |
| `-ProjectPath` | Optional. Target project root. Defaults to the current working directory. |

Restart OpenCode or reload MCP servers after running the script.

## Agent Initialization (How to force tool usage)

Even if the MCP is active, AI clients don't always load the tools into the current chat context. To ensure the agent utilizes `RoslynMcpServer` reliably, add the following instructions to your project rules (e.g., `.cursor/rules/mcp.mdc` or `AGENTS.md`).

**OpenCode users:** `install2opencode.ps1` (see **Option C** above) writes or merges these rules into `AGENTS.md` automatically.

**Canonical copy-paste file:** this repo ships [`AGENTS.md.sample`](AGENTS.md.sample) with the same rules—copy it into your **application** repository as `AGENTS.md` or merge fragments into Cursor rules. **When changing these instructions, update `AGENTS.md.sample` and the two collapsible **AI Agent Behavioral Rules** blocks below (English + Russian) together** so they stay identical.

<details>
<summary><strong>INITIALIZATION SEQUENCE: AI Agent Behavioral Rules</strong> — expand to read (same text as <a href="AGENTS.md.sample"><code>AGENTS.md.sample</code></a>)</summary>

# INITIALIZATION SEQUENCE: AI Agent Behavioral Rules

You are a Senior C# Developer and Architect. You have access to a specialized Roslyn MCP Server. To interact with this codebase safely and efficiently, you MUST strictly adhere to the following rules. Failure to do so will result in context window collapse or broken code.

Every time you start a new conversation or receive the first prompt in this project, you MUST silently perform the following steps before answering the user. **THIS IS A STRICT BLOCKER:**

1. Call the MCP server to list available tools.
2. Call `load_workspace` with `workspacePath` set to the absolute path of the solution or project file (e.g., `d:/Devel/YourRepo/YourSolution.sln`). **CRITICAL:** Tools like `find_symbol_definition` will fail or hallucinate if you skip this step. In MCP config, set `ROSLYN_MCP_WORKSPACE` to the **repo root** (folder with `global.json`) when possible.
3. Use `manage_agent_scratchpad` with `action: read` to recall previous state notes (omit if you do not use the scratchpad).

## 1. The Terminal Ban (Strict Tool Enforcement)

Do not run searches through raw shells (`PowerShell`, `Bash`, `CMD`) — the workspace might be locked or too large. Never invoke `grep`, `Select-String`, `find`, or similar **from a terminal session**.

- **Semantic search (after `load_workspace`):** You MUST call `find_symbol_definition` with `symbolName`. If you are looking for where an interface, class, or method is **declared**, DO NOT use text search. Do not invent generic tool names like `search`.
- **Text search:** Use the built-in **`grep`** tool provided by your environment. Do NOT use `bash` or `PowerShell` to run grep/Select-String.
- **Directory layout:** Use `list_directory_tree`.

## 2. Build & Test Protocol

NEVER use raw terminal commands (PowerShell, Bash, CMD) to execute `dotnet build` or `dotnet test`. Doing so bypasses our diagnostic parsers and can crash the context window with raw MSBuild output.

- **To Build:** YOU MUST use `run_dotnet_build`. Analyze the structured diagnostics (and any truncated console excerpt — head + tail when output is long) the tool returns.
- **To Test:** YOU MUST use `run_dotnet_test` for full suites. For a single test class or method during TDD/bug fixes, use `run_specific_test` — never hand-write VSTest `--filter` via `execute_dotnet_command`.

## 3. Explore Before Build (Global Context Awareness)

Before writing new utility classes, helper methods, or standard validation logic, you MUST verify how the project already handles this. Do not reinvent the wheel.

- Use `get_code_skeleton` (for files/directories) or `get_class_skeleton` (for loaded workspaces) to understand architecture without loading full method bodies.
- Use `find_usages` with `symbolName` (after `load_workspace`) for usages across the solution, or `find_symbol_references` with `filePath` + `symbolName` when you already know the declaring file — then mirror the team’s patterns.
- Use `find_implementations` with `symbolName` to list classes that **implement an interface** or **derive from a base class** — do not use text search or `find_usages` for this.

For C# edits, ALWAYS prefer `get_class_skeleton`, `get_code_skeleton`, `get_method_body`, `explore_assembly`, `decompile_type`, `get_decompiled_class_skeleton`, `get_decompiled_method_body`, `run_dotnet_build`, and `get_diagnostics_for_file` instead of inventing code from memory. When fixing compiler/analyzer errors, call `get_code_fixes` and `apply_code_fix` **before** guessing a manual patch—Roslyn already knows the correct fix for most standard diagnostics. **Persisting edits to disk** follows **section 6** (IDE-native tools vs this MCP server's file tools).

## 4. Third-Party Code / NuGet Investigation

If you encounter a bug originating from a compiled `.dll` or NuGet package, DO NOT guess or hallucinate its implementation.

- Before adding or upgrading packages, use `search_nuget_registry` to verify the package id and latest stable version — never invent library names or versions.
- Use `list_nuget_packages` to inspect installed and transitive dependencies across projects (JSON) before changing `.csproj` files.
- Use `explore_assembly` with `assemblyName` (after `load_workspace`) **or** `assemblyPath` (absolute `.dll`, e.g. from NuGet cache) — provide one, not neither.
- Use `decompile_type` / `get_decompiled_class_skeleton` / `get_decompiled_method_body` with the same `assemblyName` or `assemblyPath` rules.
- Use `decompile_type` to read the exact C# source code. For large types, use `get_decompiled_class_skeleton`; for a specific method overload, use `get_decompiled_method_body`.
- Propose a fix only after reading the decompiled material.

## 5. Execution Loop

Think step-by-step. If a tool fails or returns an error, read the error message carefully, adjust your parameters, and try the appropriate MCP tool again. Do not fallback to raw shell commands. For MCP server diagnostics use `tail_tool_log` (or `read_log_tail` without `filePath`); optional `filterKeyword` such as `Failure` or `ERR`. Only after these steps are complete, respond to the user's actual prompt.

## 6. Environment-Specific File Editing Protocol

Before editing any files, identify your host environment:

- **If you are running in a UI-based IDE (Cursor, OpenCode, Windsurf):** You MUST use the built-in native file editing tools (such as `edit`, `write`, or equivalent operations) provided by your environment so the user can review changes in the IDE diff viewer. **Do NOT** use this Roslyn MCP server's `apply_patch` or `update_file_content` when those native tools are available—they bypass the host review workflow.

- **If you are running in a headless/CLI environment (e.g., Aider) or your client does not expose first-class edit tools:** You MUST persist changes using this MCP server's **`apply_patch`** and/or **`update_file_content`** (or the file-write mechanism your CLI integration documents). Do not use raw shell redirection to invent files.
- **For C# insertions (usings, methods, properties, fields):** prefer **`add_using`**, **`add_method_to_class`**, **`add_property_to_class`**, **`add_field_to_class`**, **`organize_usings`** over `apply_patch`.
- **For method body edits:** read with **`get_method_body`**, write with **`update_method_body`** — not `apply_patch`.
- **Bug investigation:** use **`get_call_graph`** to see callers/callees before loading many method bodies.
- **Packages:** use **`search_nuget_registry`** + **`add_package_reference`** — not hand-edited versions in csproj.
- **After server rebuild:** call **`get_mcp_server_info`**; run `publish-and-verify.ps1`.


</details>

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
- `workspacePath` — `.sln` / `.csproj` (and sometimes a directory): `load_workspace`, `run_dotnet_test`, `run_specific_test`, `run_format`, optional reload for `list_projects` / `get_project_graph`. **`run_dotnet_build` accepts only a `.csproj` or `.sln` file path, not a directory.**
- `symbolName` — C# identifier for `find_symbol_definition`, `find_symbol_references`, `find_usages`, and `find_implementations` (exact name; matching is case-insensitive for definition/usages/implementations).
- `diagnosticId` — compiler/analyzer id from `get_diagnostics_for_file` (e.g. `CS0246`) for `get_code_fixes` / `apply_code_fix`.
- `fixIndex` — 0-based index from `get_code_fixes` for `apply_code_fix`.
- `path` — `.cs` file or directory for `get_code_skeleton` (absolute path; disk-based, no workspace required).

There are **55** registered tools (see list below) and **1** MCP prompt (`RefactoringAssistantPrompt`).

### Workspace / Roslyn

<details>
<summary><code>load_workspace</code> — Loads .sln/.csproj into MSBuildWorkspace.</summary>

**Parameters:**
- `workspacePath: string`
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
</details>

<details>
<summary><code>get_method_body</code> — Returns the source code of a specific method within a named class.</summary>

**Parameters:**
- `filePath: string`
- `className: string`
- `methodName: string`
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
- `workspacePath: string` — must be an existing **`.csproj` or `.sln` file** (not a directory).

**Behavior:** Uses 64-bit `dotnet` (Windows: `Program Files\dotnet`). **Working directory** is the nearest ancestor of the `.sln`/`.csproj` that contains `global.json`, so the pinned SDK matches the repo. The response includes a **`### dotnet run`** block (host path, `dotnet --version`, `global.json`). Stdout and stderr are merged; up to 20 lines matching MSBuild `path(line,col): error|warning CODE: message`. On non-zero exit without matches, a **truncated** console excerpt is included (full if ≤3000 chars; else first 1000 + last 1500 with a middle marker).

</details>

<details>
<summary><code>run_dotnet_test</code> — Runs dotnet test with condensed failure details.</summary>

**Parameters:**
- `workspacePath: string`

**Behavior:** stdout and stderr are merged. When a normal VSTest/xUnit totals line is present, the tool summarizes pass/fail counts and up to five failed tests. If the process exits with a non-zero code and **no** standard summary was found (common when tests **fail to compile**), the response includes the same **head+tail truncation** as `run_dotnet_build` (full output if ≤3000 chars; else first 1000 + last 1500 chars with a middle marker) so MSBuild/compiler errors near the start are not lost.

</details>

<details>
<summary><code>run_specific_test</code> — Run dotnet test filtered to one class and/or method.</summary>

**Parameters:**
- `workspacePath: string`
- `className: string?` — e.g. `UserServiceTests` (simple or fully qualified)
- `methodName: string?` — e.g. `CreateUser_WhenValid_ReturnsOk`

At least one of `className` or `methodName` is required. The tool builds `--filter` internally. After `load_workspace`, Roslyn resolves exact fully qualified names when possible.

**Model guidance:** use this for TDD red/green loops — do not run the full suite and do not craft VSTest filter strings manually.

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
- `workspacePath: string` — `.sln`, `.csproj`, or directory
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
- `useRegex: bool = false`
- `maxResults: int = 50`

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
<summary><code>rename_symbol</code> — Semantic rename via Roslyn with preview capability.</summary>

**Parameters:**
- `filePath: string`
- `symbolName: string`
- `newName: string`
- `scope: string = "project"` — allowed: `project` | `solution`
- `previewOnly: bool = true`
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

Use after `dotnet publish` to verify the MCP host picked up the new binary (expect **55** tools).

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
- работе с `*.sln`/`*.csproj` через Roslyn;
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

- создаёт или обновляет **`opencode.json`**, регистрируя `RoslynMcpServer` как локальный MCP-сервер (`type: local`, `enabled: true`);
- создаёт или дополняет **`AGENTS.md`**: если правил Roslyn MCP ещё нет, подмешивает текст из `AGENTS.md.sample` (новый файл или append к существующему).

| Параметр | Описание |
| --- | --- |
| `-BinaryPath` | Необязательный абсолютный путь к `RoslynMcpServer.exe`. Если не указан — берётся бинарь рядом со скриптом. |
| `-ProjectPath` | Необязательный корень проекта. По умолчанию — текущая рабочая директория. |

После установки перезапустите OpenCode или перезагрузите MCP-серверы.

## Cursor: как заставить агента реально вызывать tools

Добавьте этот блок инструкций в правила вашего проекта (`.cursor/rules/mcp.mdc` или `AGENTS.md`). Полный текст в [`AGENTS.md.sample`](AGENTS.md.sample). **При правках обновляйте вместе `AGENTS.md.sample` и два раскрывающихся блока ниже (англ. + рус. секция).**

**Для OpenCode:** `install2opencode.ps1` (см. блок **OpenCode** выше) автоматически создаёт или дополняет `AGENTS.md`.

<details>
<summary><strong>Правила агента (текст на EN)</strong> — развернуть; совпадает с <a href="AGENTS.md.sample"><code>AGENTS.md.sample</code></a></summary>

# INITIALIZATION SEQUENCE: AI Agent Behavioral Rules

You are a Senior C# Developer and Architect. You have access to a specialized Roslyn MCP Server. To interact with this codebase safely and efficiently, you MUST strictly adhere to the following rules. Failure to do so will result in context window collapse or broken code.

Every time you start a new conversation or receive the first prompt in this project, you MUST silently perform the following steps before answering the user. **THIS IS A STRICT BLOCKER:**

1. Call the MCP server to list available tools.
2. Call `load_workspace` with `workspacePath` set to the absolute path of the solution or project file (e.g., `d:/Devel/YourRepo/YourSolution.sln`). **CRITICAL:** Tools like `find_symbol_definition` will fail or hallucinate if you skip this step. In MCP config, set `ROSLYN_MCP_WORKSPACE` to the **repo root** (folder with `global.json`) when possible.
3. Use `manage_agent_scratchpad` with `action: read` to recall previous state notes (omit if you do not use the scratchpad).

## 1. The Terminal Ban (Strict Tool Enforcement)

Do not run searches through raw shells (`PowerShell`, `Bash`, `CMD`) — the workspace might be locked or too large. Never invoke `grep`, `Select-String`, `find`, or similar **from a terminal session**.

- **Semantic search (after `load_workspace`):** You MUST call `find_symbol_definition` with `symbolName`. If you are looking for where an interface, class, or method is **declared**, DO NOT use text search. Do not invent generic tool names like `search`.
- **Text search:** Use the built-in **`grep`** tool provided by your environment. Do NOT use `bash` or `PowerShell` to run grep/Select-String.
- **Directory layout:** Use `list_directory_tree`.

## 2. Build & Test Protocol

NEVER use raw terminal commands (PowerShell, Bash, CMD) to execute `dotnet build` or `dotnet test`. Doing so bypasses our diagnostic parsers and can crash the context window with raw MSBuild output.

- **To Build:** YOU MUST use `run_dotnet_build`. Analyze the structured diagnostics (and any truncated console excerpt — head + tail when output is long) the tool returns.
- **To Test:** YOU MUST use `run_dotnet_test` for full suites. For a single test class or method during TDD/bug fixes, use `run_specific_test` — never hand-write VSTest `--filter` via `execute_dotnet_command`.

## 3. Explore Before Build (Global Context Awareness)

Before writing new utility classes, helper methods, or standard validation logic, you MUST verify how the project already handles this. Do not reinvent the wheel.

- Use `get_code_skeleton` (for files/directories) or `get_class_skeleton` (for loaded workspaces) to understand architecture without loading full method bodies.
- Use `find_usages` with `symbolName` (after `load_workspace`) for usages across the solution, or `find_symbol_references` with `filePath` + `symbolName` when you already know the declaring file — then mirror the team’s patterns.
- Use `find_implementations` with `symbolName` to list classes that **implement an interface** or **derive from a base class** — do not use text search or `find_usages` for this.

For C# edits, ALWAYS prefer `get_class_skeleton`, `get_code_skeleton`, `get_method_body`, `explore_assembly`, `decompile_type`, `get_decompiled_class_skeleton`, `get_decompiled_method_body`, `run_dotnet_build`, and `get_diagnostics_for_file` instead of inventing code from memory. When fixing compiler/analyzer errors, call `get_code_fixes` and `apply_code_fix` **before** guessing a manual patch—Roslyn already knows the correct fix for most standard diagnostics. **Persisting edits to disk** follows **section 6** (IDE-native tools vs this MCP server's file tools).

## 4. Third-Party Code / NuGet Investigation

If you encounter a bug originating from a compiled `.dll` or NuGet package, DO NOT guess or hallucinate its implementation.

- Before adding or upgrading packages, use `search_nuget_registry` to verify the package id and latest stable version — never invent library names or versions.
- Use `list_nuget_packages` to inspect installed and transitive dependencies across projects (JSON) before changing `.csproj` files.
- Use `explore_assembly` with `assemblyName` (after `load_workspace`) **or** `assemblyPath` (absolute `.dll`, e.g. from NuGet cache) — provide one, not neither.
- Use `decompile_type` / `get_decompiled_class_skeleton` / `get_decompiled_method_body` with the same `assemblyName` or `assemblyPath` rules.
- Use `decompile_type` to read the exact C# source code. For large types, use `get_decompiled_class_skeleton`; for a specific method overload, use `get_decompiled_method_body`.
- Propose a fix only after reading the decompiled material.

## 5. Execution Loop

Think step-by-step. If a tool fails or returns an error, read the error message carefully, adjust your parameters, and try the appropriate MCP tool again. Do not fallback to raw shell commands. For MCP server diagnostics use `tail_tool_log` (or `read_log_tail` without `filePath`); optional `filterKeyword` such as `Failure` or `ERR`. Only after these steps are complete, respond to the user's actual prompt.

## 6. Environment-Specific File Editing Protocol

Before editing any files, identify your host environment:

- **If you are running in a UI-based IDE (Cursor, OpenCode, Windsurf):** You MUST use the built-in native file editing tools (such as `edit`, `write`, or equivalent operations) provided by your environment so the user can review changes in the IDE diff viewer. **Do NOT** use this Roslyn MCP server's `apply_patch` or `update_file_content` when those native tools are available—they bypass the host review workflow.

- **If you are running in a headless/CLI environment (e.g., Aider) or your client does not expose first-class edit tools:** You MUST persist changes using this MCP server's **`apply_patch`** and/or **`update_file_content`** (or the file-write mechanism your CLI integration documents). Do not use raw shell redirection to invent files.
- **For C# insertions (usings, methods, properties, fields):** prefer **`add_using`**, **`add_method_to_class`**, **`add_property_to_class`**, **`add_field_to_class`**, **`organize_usings`** over `apply_patch`.
- **For method body edits:** read with **`get_method_body`**, write with **`update_method_body`** — not `apply_patch`.
- **Bug investigation:** use **`get_call_graph`** to see callers/callees before loading many method bodies.
- **Packages:** use **`search_nuget_registry`** + **`add_package_reference`** — not hand-edited versions in csproj.
- **After server rebuild:** call **`get_mcp_server_info`**; run `publish-and-verify.ps1`.


</details>
*(Инструкция намеренно оставлена на английском, так как LLM лучше следуют английским императивам).*

**Отдельно для модели (RU):** для **объявления** типа/члена после `load_workspace` — MCP `find_symbol_definition`, не текстовый поиск и не выдуманный tool вроде `search`. Для **текста по файлам** — встроенный `grep` IDE, не `bash`/PowerShell с grep. Сборку/тесты — только MCP `run_dotnet_build` / `run_dotnet_test`, не сырой терминал. Запись файлов на диск — **п. 6** (IDE: нативные правки; headless: MCP `apply_patch` / `update_file_content`). Раскрывающийся блок выше (как в `AGENTS.md.sample`) задаёт правила **§1–§6** — следуй ему при каждом запуске сессии.

## Логи
- Основной лог: `logs/mcp-*.log` (относительно `AppContext.BaseDirectory`).
- Включено логирование входящих JSON-RPC сообщений (`MCP_LOG_INCOMING_RPC`, `MCP_LOG_INCOMING_RPC_MAX_CHARS`).
- **Ответы tools:** в лог пишется однострочная сводка и отдельные строки warning/error (без полного дублирования ответа MCP). `ROSLYN_MCP_LOG_TOOL_OUTPUT=full` — полный текст ответов tools.
- Переменные: `ROSLYN_MCP_WORKSPACE` (корень репо для MSBuild/SDK), `ROSLYN_MCP_LOG_TOOL_OUTPUT=full`.

## Reference: MCP Tools

**Имена параметров в JSON:**
- `filePath` — один файл (чтение/правка/диагностика/логи).
- `directoryPath` — корневая папка (`list_directory_tree`, опционально корень для `search_code`).
- `workspacePath` — `.sln` / `.csproj` (и иногда каталог): `load_workspace`, `run_dotnet_test`, `run_specific_test`, `run_format`, опциональная перезагрузка в `list_projects` / `get_project_graph`. **`run_dotnet_build` принимает только путь к файлу `.csproj` или `.sln`, не каталог.**
- `symbolName` — идентификатор C# для `find_symbol_definition`, `find_symbol_references`, `find_usages` и `find_implementations` (точное имя; регистр не важен для definition/usages/implementations).
- `diagnosticId` — id компилятора/анализатора из `get_diagnostics_for_file` (например `CS0246`) для `get_code_fixes` / `apply_code_fix`.
- `fixIndex` — индекс (0-based) из `get_code_fixes` для `apply_code_fix`.
- `path` — файл `.cs` или каталог для `get_code_skeleton` (абсолютный путь; с диска, workspace не обязателен).

Зарегистрировано **55** инструментов (список ниже) и **1** MCP-промпт (`RefactoringAssistantPrompt`).

### Workspace / Roslyn

<details>
<summary><code>load_workspace</code> — Загружает .sln/.csproj в MSBuildWorkspace.</summary>

**Параметры:**
- `workspacePath: string`
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
</details>

<details>
<summary><code>get_method_body</code> — Возвращает код одного метода в именованном классе.</summary>

**Параметры:**
- `filePath: string`
- `className: string`
- `methodName: string`
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
- `workspacePath: string` — только существующий **файл** `.csproj` или `.sln` (не каталог).

**Поведение:** 64-bit `dotnet` (Windows: `Program Files\dotnet`). **Working directory** — ближайший предок `.sln`/`.csproj` с `global.json`. В ответе блок **`### dotnet run`** (host, `dotnet --version`, `global.json`). До 20 строк MSBuild `path(line,col): error|warning CODE: message`; при ненулевом коде без совпадений — усечённый вывод (≤3000 целиком, иначе первые 1000 + последние 1500).

</details>

<details>
<summary><code>run_dotnet_test</code> — Запускает dotnet test с сокращенным выводом ошибок.</summary>

**Параметры:**
- `workspacePath: string`

**Поведение:** объединяются stdout и stderr. При наличии стандартной строки итогов VSTest/xUnit выводится сводка и до пяти упавших тестов. Если код выхода ненулевой и **нет** распознанной строки сводки (часто при **ошибке компиляции** тестов), в ответ добавляется такое же **усечение начало+конец**, как у `run_dotnet_build` (см. выше), чтобы не терять ранние сообщения MSBuild/компилятора.

</details>

<details>
<summary><code>run_specific_test</code> — dotnet test с фильтром по классу и/или методу.</summary>

**Параметры:**
- `workspacePath: string`
- `className: string?` — например `UserServiceTests`
- `methodName: string?` — например `CreateUser_WhenValid_ReturnsOk`

Нужен хотя бы один из `className` / `methodName`. `--filter` формируется автоматически. После `load_workspace` Roslyn по возможности резолвит точный FQN.

**Для модели:** TDD/фикс бага — этот tool, не полный suite и не ручной VSTest filter.

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
- `workspacePath: string` — `.sln`, `.csproj` или каталог
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
- `useRegex: bool = false`
- `maxResults: int = 50`

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
<summary><code>rename_symbol</code> — Семантический rename через Roslyn с предпросмотром.</summary>

**Параметры:**
- `filePath: string`
- `symbolName: string`
- `newName: string`
- `scope: string = "project"` — допустимо: `project` | `solution`
- `previewOnly: bool = true`
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

После `dotnet publish` — проверка, что MCP подхватил новый бинарник (ожидай **55** tools).

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