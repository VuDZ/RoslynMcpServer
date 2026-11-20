# Installation and configuration

[Русская версия](setup.ru.md) · [Back to README](../README.md)

This page collects the setup details that the [README](../README.md) leaves out: platform notes, client registration, tool profiles, configuration, logs, and how the server tracks your edits. Per-tool parameters are in the [tool reference](tools.md).

## MCP and agent: the 60-second model

- **MCP client** — Cursor, OpenCode, or another compatible host. It starts the server and exposes its tools to the model.
- **AI agent** — the model plus the client's orchestration. It decides which tool to call; it is not part of this repository.
- **RoslynMcpServer** — a local stdio process. It exposes tools but has no chat UI and does not act autonomously.
- **`AGENTS.md`** — repository policy for the agent. MCP configuration makes tools available; this file tells the agent when to use them.

Typical flow:

`developer request → agent → MCP tool call → Roslyn/MSBuild/ILSpy/dotnet → compact result → agent`

## Prerequisites

- An MCP client with local stdio-server support.
- A .NET SDK compatible with the solution being analyzed; honor the repository's `global.json`.
- The **.NET 10 SDK** when building this server from source.
- Git is strongly recommended for reviewing and reverting agent changes.

## Supported platforms

| RID | Release archive | Support |
|---|---|---|
| `win-x64` | `.zip`, self-contained and framework-dependent | Supported |
| `linux-x64` | `.tar.gz`, self-contained and framework-dependent | Supported |
| `osx-arm64` | `.tar.gz`, self-contained and framework-dependent | Native CI passed; release smoke pending |
| `TEST_HOST`, `linux-arm64`, `linux-musl-x64` | Present in the project RID list and can be built | Not supported |

Details and validation status: [release platforms](github-releases/README.md), [macOS ARM64](github-releases/task-01-macos-arm64-med.md).

macOS ARM64 releases use the same `RoslynMCP/` archive root and `RoslynMcpServer` executable as Linux.
Framework-dependent needs the ARM64 .NET 10 runtime; both variants need an ARM64 SDK compatible with your project.

### Install a Linux release

GitHub Releases publishes `RoslynMcpServer-<version>-linux-x64-self-contained.tar.gz` and `RoslynMcpServer-<version>-linux-x64-framework-dependent.tar.gz`. The archive root is `RoslynMCP/`.

```bash
tar -xzf RoslynMcpServer-<version>-linux-x64-self-contained.tar.gz
```

The host is `RoslynMCP/RoslynMcpServer`.

- Self-contained needs `libicu` and glibc **2.27** or newer (the .NET 10 baseline for x64).
- Framework-dependent needs the .NET 10 runtime. Set `DOTNET_ROOT` when that runtime is not in a standard location (`/usr/share/dotnet`, `/usr/lib/dotnet`, or `~/.dotnet`).
- The project being analyzed needs a .NET SDK. An SDK from distribution packages is best effort.
- A loaded solution keeps disk watchers, and on Linux each one is an inotify instance. `fs.inotify.max_user_instances` defaults to 128 for all processes of the user, so several MCP copies, an IDE and `dotnet watch` can reach it; `get_mcp_server_info` then lists the watcher start failures. Raise the limit persistently: `echo 'fs.inotify.max_user_instances = 1024' | sudo tee /etc/sysctl.d/90-inotify.conf && sudo sysctl --system`.

```json
{
  "mcpServers": {
    "roslyn-mcp-server": {
      "command": "/absolute/path/to/RoslynMCP/RoslynMcpServer",
      "env": {
        "ROSLYN_MCP_WORKSPACE": "/absolute/path/to/YourApp"
      }
    }
  }
}
```

## First setup

Prebuilt archives are on [GitHub Releases](https://github.com/VuDZ/RoslynMcpServer/releases); the [README quick start](../README.md#quick-start) covers them. The steps below build the server from source.

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
      "command": "REPO_ROOT",
      "env": {
        "ROSLYN_MCP_WORKSPACE": "LOCAL_ROOT/YourApp"
      }
    }
  }
}
```

`ROSLYN_MCP_WORKSPACE` is optional but recommended: set it to the target repository root, especially when that repository contains `global.json`. For smaller local models, configure `ROSLYN_MCP_TOOL_PROFILE` / `ROSLYN_MCP_TOOL_GROUPS` as described in [Tool profiles](#tool-profiles).

Restart or reload MCP servers after changing client configuration.

### 3. Add agent policy to the target repository

Copy or merge [`AGENTS.md.sample`](../AGENTS.md.sample) into the application repository as `AGENTS.md`. Keep repository-specific commands and entry points there. This is separate from MCP registration: without policy, the tools may be connected but the agent can still choose generic text search or shell commands.

### 4. Verify the first session

Ask the agent:

> Use Roslyn MCP. Call `get_mcp_server_info`, then `load_workspace` for the absolute path to this repository's solution. List the loaded projects and report workspace health. Do not modify files.

A healthy session reports the server binary/version, the active tool profile, SDK/restore health, and at least one loaded project. Then try a semantic request such as “find the definition and usages of `SolutionManager`.”

If tools are missing, call `list_tool_groups`. If workspace loading fails, follow the returned diagnostic rather than switching to shell `dotnet`.

### OpenCode installer

After `dotnet publish`, the publish folder contains [`install2opencode.ps1`](../install2opencode.ps1) and [`AGENTS.md.sample`](../AGENTS.md.sample) next to the host executable. On Linux, run the script with `pwsh`; bash does not execute it.

From the root of the **project you want to configure** (your application repo):

```powershell
cd LOCAL_ROOT/YourApp
& "REPO_ROOT/bin/Release/net10.0/win-x64/publish/install2opencode.ps1"
```

The script:

- Creates or updates **`opencode.json`** in the target directory, registering `RoslynMcpServer` as a local MCP server (`type: local`, `enabled: true`, **`timeout`: `600000`** ms = 10 minutes).
- Creates or updates **`AGENTS.md`**: if Roslyn MCP behavioral rules are not already present, merges content from the bundled `AGENTS.md.sample` (creates a new file or appends to an existing one).

**OpenCode MCP host timeout:** OpenCode’s default for MCP `tools/call` is about **60 seconds**. That is **not** the tool argument `timeoutSeconds` on `run_dotnet_test` / `run_dotnet_run`. Without raising the host timeout, long build/test calls fail with `McpError: MCP error -32001: Request timed out` (~60s) even if you pass `timeoutSeconds: 900`. Set `"timeout": 600000` (or higher) on the server entry — see [`opencode.json.sample`](../opencode.json.sample). `install2opencode.ps1` writes this for you. If an older OpenCode build ignores per-server `timeout`, also try `"experimental": { "mcp_timeout": 600000 }` in `opencode.json`.

Example server block:

```json
"roslyn-mcp-server": {
  "type": "local",
  "command": ["REPO_ROOT"],
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

Default is **`full`** (every public tool). A **`lite`** session starts with the 16-tool core so local models spend less context on `tools/list`. Extra groups can be added at process start or, on clients that honor `notifications/tools/list_changed`, during the session.

| Variable | Values | Effect |
| --- | --- | --- |
| `ROSLYN_MCP_TOOL_PROFILE` | `full` (default) or `lite` | Selects the startup catalog. Empty/unset is `full`. |
| `ROSLYN_MCP_TOOL_GROUPS` | `core`, `files`, `navigation`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations` (comma-separated, case-insensitive) | Adds those groups to **`lite` before the first `tools/list`**. In `full` the names are recorded and do not change the set. Unknown names fail startup. `core` is already in lite. |

Example: `ROSLYN_MCP_TOOL_GROUPS=decompile,nuget`

Portable OpenCode examples: [`opencode.json.sample`](../opencode.json.sample) (`roslyn-mcp-server` = full, `roslyn-mcp-lite`, `roslyn-mcp-lite-groups`). Enable only one server.

| Group | Intent | Tools |
| --- | --- | ---: |
| `core` | Workspace, symbol lookup, build, test, help | 16 (lite default) |
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

**Client compatibility:** do not assume the host refreshes tools after `tools/list_changed`. If a newly enabled tool is missing from the client's catalog, restart with `ROSLYN_MCP_TOOL_GROUPS=<group>` (and `ROSLYN_MCP_TOOL_PROFILE=lite`). A `2025-11-25` initialize session receives `tools/list_changed` when the set grows. A `2026-07-28` session receives it only after `subscriptions/listen` with `toolsListChanged`; the server accepts that revision through the MCP C# SDK. The minified UTF-8 `tools/list` measures **47,557 bytes for 55 full tools** and **21,120 bytes for 16 lite tools**.

## Agent Initialization (How to force tool usage)

Even if the MCP is active, AI clients don't always load the tools into the current chat context. Put the **session policy** in the app repo so the agent actually uses Roslyn MCP.

**Canonical file:** [`AGENTS.md.sample`](../AGENTS.md.sample) — copy into the **application** repository as `AGENTS.md` (or merge into `.cursor/rules`). It is policy only (when / MCP vs host / bans); per-tool parameters live in MCP tool Descriptions and in the [tool reference](tools.md).

**OpenCode:** `install2opencode.ps1` (see [OpenCode installer](#opencode-installer)) writes or merges the sample into `AGENTS.md` automatically.

**Maintainers:** when tools or agent-visible behavior change, update **`AGENTS.md.sample`, [CHANGELOG.md](../CHANGELOG.md), and the [tool reference](tools.md)** together. Do not paste the full AGENTS body into the docs — link the sample.

Policy summary (full text in the sample):

- Verify the server with `get_mcp_server_info`; inspect missing `lite` tools with `list_tool_groups` / `enable_tool_group`
- Optional `RoslynMcp.jsonc` (exe directory, per-user configuration directory, then cwd; cwd wins). Set `ROSLYN_MCP_WORKSPACE` to the repo root at startup so cwd is not the user profile. See `RoslynMcp.jsonc.sample` and `AGENTS.md.sample`
- `load_workspace` before symbol / build / decompile work (or rely on config `workspace-path`); prefer `.sln` / `.slnx`; compares only arguments you passed
- Missing `Compile` target on load → retry with `targetFramework` (inner TFM); not SDK mismatch
- VS 2026 BuildHost / `XMakeElements` → not SDK mismatch; need MCP 1.0.35+ or a single SDK-style `.csproj`
- C# identifiers → MCP first; plain text → host Grep; never shell `grep` / `dotnet build|test`
- Saved `.cs` of already loaded files (v1.1.0+) sync into symbol search automatically; new or deleted `.cs` needs reload (v1.3.30); unsaved editor buffers are ignored; `reset_workspace` after build / generated `obj`
- IDE: host edit/write; headless: MCP `apply_patch` / AST tools
- Secrets: never paste PAT/passwords; app README must document run target / sample args

## Agent tools by version

Version history of agent-visible tools and behavior: [CHANGELOG.md](../CHANGELOG.md). Current server version: see `RoslynMcpServer.csproj`.

Rapid TIA was introduced in **1.6.0**. `estimate_affected_tests` uses saved edits and compiled type dependencies to select VSTest projects and classes. With `run: true`, it builds the selected projects in order before running their tests in parallel. Use `maxParallelProjects: 1` to build and test each project in turn when projects share resources. See the [tool reference](tools.md) and [architecture](ARCHITECTURE.md#rapid-tia).

In 1.5.9, name-based declaration search uses source-only lookup, preserving the exclusion of metadata-only results. Provenance replay reads newer compatible BuildHost binlogs and reports each file's format; any recoverable read error still rejects that file's provenance.

## Per-user data

Configuration and drop-in plugins survive an npx package update. Configuration is merged in order: executable directory, per-user configuration directory, then process cwd. Plugin discovery checks executable drop-ins, user drop-ins, configured paths, then `ROSLYN_MCP_PLUGINS`; the first validated plugin ID wins.

- Windows: `%LOCALAPPDATA%\RoslynMcpServer\`.
- Linux: `$XDG_CONFIG_HOME/roslyn-mcp/` (default `~/.config/roslyn-mcp/`) for configuration/plugins; `$XDG_STATE_HOME/roslyn-mcp/` (default `~/.local/state/roslyn-mcp/`) for logs/usage.
- macOS: `~/Library/Application Support/RoslynMcpServer/`.

`ROSLYN_MCP_HOME` overrides both roots: `RoslynMcp.jsonc`, `plugins/`, `logs/`, and `usage/` sit inside it. `ROSLYN_MCP_LOG_DIR` overrides only text logs and takes priority over `ROSLYN_MCP_HOME`. Overrides must be absolute paths. `get_mcp_server_info` shows the resolved paths. Existing exe-local config and plugins remain supported.

## Logs
- **Main log:** `logs/mcp-*.log` under the per-user state directory, or `ROSLYN_MCP_LOG_DIR`.
- Global incoming JSON-RPC logging is enabled by default.
- **Tool output:** logged as a one-line summary plus separate warning/error lines (not a duplicated full MCP response). Set `ROSLYN_MCP_LOG_TOOL_OUTPUT=full` to log entire tool responses at Information level.
- Environment Variables:
  - `ROSLYN_MCP_WORKSPACE` — repo root for MSBuild/SDK discovery at startup (see MCP config above). Also sets process cwd so `RoslynMcp.jsonc` in the working directory is read from the repo, not the user profile.
  - `ROSLYN_MCP_TOOL_PROFILE` — `full` (default) or `lite` (see **Tool profiles**).
  - `ROSLYN_MCP_TOOL_GROUPS` — comma-separated extra groups for `lite` at startup. Valid: `core`, `files`, `navigation`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations` (see **Tool profiles**).
  - `ROSLYN_MCP_LOG_TOOL_OUTPUT=full` — verbose tool response logging.
  - `ROSLYN_MCP_SEARCH_PREWARM=0` — turn off the background search-index prewarm after a load (1.5.15+, on by default; wins over `RoslynMcp.jsonc` `search-prewarm`). The prewarm logs one `workspace_search_prewarm` line and never changes tool responses.
  - `ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS=0` — load projects without the optimizations of the server's BuildHost (on by default; wins over `RoslynMcp.jsonc` `buildhost-optimizations`). The server ships a fork of Roslyn's BuildHost that builds referenced projects first, evaluates projects ahead on worker threads and shares one SDK resolution cache between the evaluations of a project; the loaded workspace is the same, the load is shorter.
  - `ROSLYN_MCP_BUILDHOST_EVALUATE_AHEAD_WORKERS=<N>` — worker threads of that evaluation ahead (default 4; `0` turns off only it; wins over `buildhost-evaluate-ahead-workers`).
  - `MCP_LOG_INCOMING_RPC=0` (disable incoming RPC logging).
  - `MCP_LOG_INCOMING_RPC_MAX_CHARS=<N>` (limit payload log length, `0` = unlimited).

## Local usage statistics

Usage collection is enabled by default. Set `ROSLYN_MCP_USAGE=0` (or `false`) before starting the server to disable it. Local `usage/usage-YYYYMMDD-N.jsonl` files record process/client identity, workspace paths and load properties, tool names/timings, and memory/activity snapshots. They contain no tool arguments, result text, environment values, or command lines. Files rotate at 32 MiB and expire after 60 days; deleting them is safe, and an enabled collector creates new records. Nothing is uploaded.

Run `pwsh ./scripts/Measure-McpUsage.ps1 -InputDirectory <usage-directory> -Machine <label> -OutputPath <report-prefix>` to write JSON and Markdown reports. Pass directory and label arrays for multiple machines. Reports replace workspace identities with per-machine labels such as `K1`; share reports, keeping raw files private. All timeout variants are reported; the owner chooses a strategy from the results. Check the analyzer with `pwsh ./scripts/Test-McpUsage.ps1`. See [the schema](usage-statistics/schema.md) and [privacy](../PRIVACY.md).

## Saved edits and the semantic snapshot

After `load_workspace`, semantic tools trust the file watcher until the next confirmation. A confirmation is another `load_workspace`, or a flush of a save the watcher actually reported. The tools do not hash the tree on every call, and they do not mark the snapshot `freshness=unknown`.

- A saved `.cs` that is already a document is read once and applied to every project document of that path.
- A saved additional file or analyzer config that is already a document, and is not also an evaluation input, is applied on the published snapshot. XAML, resx, and Razor are recorded by role and are not text-synced.
- A new or deleted file does not add or remove a document. The project graph is marked stale; call `load_workspace` again. A project file, a solution, or `Directory.Build.*` marks the graph stale the same way.
- Watcher overflow, a watcher error, or a directory rename asks the next semantic call to re-read known documents and can mark the graph stale.
- An edit the watcher never reported — a file outside the watch set, or a dropped notification with no error — stays invisible until the next `load_workspace`. Standing coverage gaps (imports, restore inputs, custom tasks) do not by themselves block a read.

The other freshness policies that were considered and not chosen are recorded in [epoch 3](backlog/workspace-load-cache/epoch-3-live-consistency/spec.md).

## Security boundary

This server can read and write files and start `dotnet`/Git child processes with the permissions of its host process. It is **not a sandbox** and does not confine every operation to the loaded workspace.

- Use Git and review agent changes.
- Do not run the server as Administrator/root.
- Never put PATs, passwords, connection strings, or other secrets in prompts or tool arguments.
- Incoming MCP parameters are logged by default; see [Logs](#logs) before using the server with sensitive repositories.

## Privacy

RoslynMcpServer does not transmit usage statistics or user data automatically.

The process is local stdio: no backend and no HTTP client of its own. File logs and local usage records stay in the server's per-user data directories.

Network access occurs only through operations the user or MCP client requests. NuGet commands and restore during build, test, and run contact the configured feeds; they send package ids and restore metadata, not source or prompts. Builds run your MSBuild targets, and the tests and applications that tools start run your code, which can reach the network itself. Details: [PRIVACY.md](../PRIVACY.md#when-network-is-used).
