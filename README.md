# RoslynMcpServer

**Your coding agent reads C# as text. RoslynMcpServer lets it ask the compiler.**

RoslynMcpServer is a local [MCP](https://modelcontextprotocol.io) server for .NET solutions. It loads your `.sln`, `.slnx` or `.csproj` through MSBuild and Roslyn, the same project files, SDK and references that `dotnet build` uses. Claude Code, Codex, Cursor, OpenCode, or any other MCP client then gets the answers an IDE has: what a name actually refers to, who calls it, what implements it, what a NuGet dependency does inside, and why the build failed. No IDE has to be running.

[![Release](https://img.shields.io/github/v/release/VuDZ/RoslynMcpServer)](https://github.com/VuDZ/RoslynMcpServer/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Platforms](https://img.shields.io/badge/platform-win--x64%20%7C%20linux--x64%20%7C%20osx--arm64-lightgrey)

[Quick start](#quick-start) · [Documentation](#documentation) · [Русская версия](README.ru.md)

## Why the agent needs more than grep

In a small project, grep and a few file reads get an agent surprisingly far. In a real .NET solution the same approach hits the same walls every time. A method name matches its overloads, an interface member and its implementation, an extension method and some unrelated private helper, and text search can't tell them apart. Much of the behavior lives in NuGet packages whose source isn't in the repository, so the agent describes them from memory, possibly of another version. A failed `dotnet test` prints thousands of lines when the answer fits in a dozen. A rename done by search-and-replace changes whatever matches the text, which is rarely the set of references the compiler sees.

RoslynMcpServer answers these questions from the compiler's model of your solution. The examples below are real calls against [Jellyfin](https://github.com/jellyfin/jellyfin), a 41-project open-source media server. The exact calls, the Jellyfin commit and the full responses are in [docs/readme-examples](docs/readme-examples/README.md).

## What it looks like

### "Rename this method, and only this one"

`GetItemById` appears on 278 lines of Jellyfin's C#. You ask the agent:

> Rename `ILibraryManager.GetItemById(Guid)` to `FindItemById`. Leave the other overloads alone, and show me the change before you write it.

First the agent checks what the name refers to. `find_usages` lists the declarations behind it (excerpt):

```text
- `Emby.Server.Implementations.Library.LibraryManager.GetItemById` (method, 4 overloads, 246 location(s))
- `Jellyfin.LiveTv.Channels.ChannelManager.GetItemById` (method, 12 location(s))
- `MediaBrowser.Controller.Library.ILibraryManager.GetItemById` (method, 4 overloads, 246 location(s))
- `MediaBrowser.Controller.Library.LibraryManagerExtensions.GetItemById` (method, 10 location(s))
```

The interface and `LibraryManager` are one method family with four overloads, which is why both report 246 locations. The extension method takes a `string` id and wraps the interface. `ChannelManager.GetItemById<T>` is an unrelated private helper that calls `_libraryManager.GetItemById(id)` in its own body. Search-and-replace would rename the helper and the generic overloads along with the method you meant.

`rename_symbol` shows a preview first, which is its default: 152 locations bound to the `Guid` overload, tests included. Applied, the rename changed 50 files. The generic overloads and the helper kept their names, and the solution built without errors before and after the rename.

### "What does this library actually do?"

Jellyfin creates its TMDb client like this:

```csharp
// Not really interested in NotFoundException
_tmDbClient.ThrowApiExceptions = false;
```

> TMDb sometimes rate-limits us. With this setting, what happens on a 429, a 401 or a 404?

TMDbLib 3.0.0 is a NuGet package, so its source isn't in the repository. The agent finds the request code in the assembly Jellyfin actually references with `explore_assembly` and reads it with `get_decompiled_method_body`. The part that answers the question (ILSpy output, abridged):

```csharp
case HttpStatusCode.Unauthorized:
    throw new UnauthorizedAccessException("Call to TMDb returned unauthorized. ...");
case HttpStatusCode.NotFound:
    if (_client.ThrowApiExceptions)
        throw new NotFoundException(statusMessage);
    return null;
default:
    throw new GeneralHttpException(resp.StatusCode);
```

So the setting does what the comment says and nothing more. A 404 becomes `null`, while a 401 or a server error still throws. A 429 has its own branch: the loop waits for `Retry-After` (one second if the header is missing), tries again, and throws `RequestLimitExceededException` when the retries run out. The property name doesn't tell you that, and a model recalling the library from memory might not either.

### Other things worth asking

- *"List every scheduled task the server can run."* `find_implementations` on `IScheduledTask` returned 20 classes from four projects.
- *"Who calls this `DeleteItem` overload, and what does it call?"* `get_call_graph` resolves the exact overload. Jellyfin's `LibraryManager` has three.
- *"Fix the errors in this file."* `get_diagnostics_for_file` reports a file's compiler errors and warnings without a build. `get_code_fixes` and `apply_code_fix` apply Roslyn's own code actions, the ones behind the IDE light bulb, instead of a hand-written patch.
- *"Run the tests for what I just changed."* `run_dotnet_test` and `run_specific_test` return the failing tests and compiler errors, not the whole log. `estimate_affected_tests` suggests which test projects and classes your saved edits can reach and can run them. Treat it as a hint. Your full suite and CI still decide.
- *"Are any of our packages vulnerable or outdated?"* `run_nuget_audit` and `list_outdated_packages`.

## What the agent gets

55 tools, grouped by what you'd use them for:

| Area | What it covers |
|---|---|
| **Understand the solution** | Definitions, usages, implementations and derived types, call graphs, class skeletons without method bodies, the project graph, test lists. |
| **Look inside dependencies** | Browse and decompile any referenced assembly with ILSpy, at the exact version your project resolves. |
| **Change code** | Semantic rename with preview, Roslyn code fixes, syntax-tree edits (usings, methods, method bodies), project rename, NuGet add/remove, `dotnet format`. |
| **Verify** | Build, test (by project, class, method or VSTest filter), run, NuGet audit. Results come back parsed. The full log stays on the server and is read by cursor when needed. |

The tools are built for an agent's context window. Long result lists are capped and continue through an overflow cursor. A `lite` profile starts with 16 core tools for small local models, and the agent can enable more groups during the session. `get_tool_help` explains any tool on request. Plugins can add your own tools that work on the loaded solution ([plugin guide](samples/RoslynMcpPlugin/README.md)).

## Quick start

**1. Download** the archive for your platform from [Releases](https://github.com/VuDZ/RoslynMcpServer/releases) and extract it. The `self-contained` variant needs no .NET runtime for the server itself. It doesn't include an SDK, though. To load your solution, the machine needs the .NET SDK your solution builds with (see its `global.json`), and the solution should be restored, as after `dotnet restore` or a first build.

For .NET 11 builds starting with `1.6.3`, framework-dependent archives require a .NET 11 runtime. Check [supported operating systems](https://github.com/dotnet/core/blob/main/release-notes/11.0/supported-os.json) and [minimum hardware requirements](https://learn.microsoft.com/dotnet/core/compatibility/jit/11/minimum-hardware-requirements) for these builds.

On Linux (on macOS, use `osx-arm64` instead of `linux-x64`):

```bash
VERSION=1.6.0   # example; use the version you picked on the Releases page
ARCHIVE=RoslynMcpServer-$VERSION-linux-x64-self-contained.tar.gz
curl -fLO "https://github.com/VuDZ/RoslynMcpServer/releases/download/v$VERSION/$ARCHIVE"
tar -xzf "$ARCHIVE"   # creates RoslynMCP/RoslynMcpServer
```

On Windows, in PowerShell:

```powershell
$Version = "1.6.0"   # example; use the version you picked on the Releases page
$Archive = "RoslynMcpServer-$Version-win-x64-self-contained.zip"
Invoke-WebRequest "https://github.com/VuDZ/RoslynMcpServer/releases/download/v$Version/$Archive" -OutFile $Archive
Expand-Archive $Archive -DestinationPath C:\Tools   # creates C:\Tools\RoslynMCP\RoslynMcpServer.exe
```

**2. Register it** with your MCP client as a local stdio server. Most clients accept this shape (Cursor: `.cursor/mcp.json`; Claude Desktop, Windsurf and others have the same `mcpServers` block):

```json
{
  "mcpServers": {
    "roslyn": {
      "command": "/absolute/path/to/RoslynMCP/RoslynMcpServer",
      "env": { "ROSLYN_MCP_WORKSPACE": "/absolute/path/to/your/repo" }
    }
  }
}
```

On Windows, point `command` at the `.exe`. Forward slashes work in JSON and need no escaping:

```json
"command": "C:/Tools/RoslynMCP/RoslynMcpServer.exe",
"env": { "ROSLYN_MCP_WORKSPACE": "C:/src/YourApp" }
```

`ROSLYN_MCP_WORKSPACE` is optional, but set it when you can. It points the server at your repository root, so `global.json` picks the right SDK.

<details>
<summary>Claude Code</summary>

From the root of your repository:

```bash
claude mcp add roslyn --scope project -e ROSLYN_MCP_WORKSPACE="$PWD" -- /absolute/path/to/RoslynMCP/RoslynMcpServer
```

</details>

<details>
<summary>Codex</summary>

In `~/.codex/config.toml`. Raise the tool timeout, because builds and test runs often take longer than the default minute:

```toml
[mcp_servers.roslyn]
command = "/absolute/path/to/RoslynMCP/RoslynMcpServer"
env = { ROSLYN_MCP_WORKSPACE = "/absolute/path/to/your/repo" }
tool_timeout_sec = 600
```

</details>

<details>
<summary>VS Code</summary>

In `.vscode/mcp.json`:

```json
{
  "servers": {
    "roslyn": {
      "type": "stdio",
      "command": "/absolute/path/to/RoslynMCP/RoslynMcpServer",
      "env": { "ROSLYN_MCP_WORKSPACE": "${workspaceFolder}" }
    }
  }
}
```

</details>

<details>
<summary>OpenCode</summary>

The archive includes `install2opencode.ps1`. Run it with PowerShell (`pwsh` on Linux and macOS) from the root of your repository. It registers the server in `opencode.json` with a 10-minute call timeout, which OpenCode needs for builds and tests, and adds the agent rules from step 3 to `AGENTS.md`.

```powershell
& /absolute/path/to/RoslynMCP/install2opencode.ps1
```

</details>

**3. Tell the agent to use it.** This step is easy to skip, and it matters more than it looks. A connected server only makes tools *available*. Left to its habits, the agent still reaches for grep and `dotnet build` in the shell. The archive ships [`AGENTS.md.sample`](AGENTS.md.sample), a policy that tells the agent when to use which tool: load the workspace first, look up C# identifiers through Roslyn, run builds and tests through the MCP tools. Copy it into your repository as `AGENTS.md`, or merge it into an existing one. Codex, Cursor and OpenCode read `AGENTS.md`. For Claude Code, add a line with `@AGENTS.md` to `CLAUDE.md`.

**4. Try it.** Restart the client, then ask:

> Use Roslyn MCP: call `get_mcp_server_info`, load the solution in this repository, and list its projects. Then find every implementation of `<an interface you know>`.

The first response shows the server version, the SDK it resolved, restore health and the loaded projects. If a load fails, the error says why, for example a missing SDK or a multi-target project that needs a `targetFramework`.

More setup detail is in [Installation and configuration](docs/setup.md): all platforms, building from source, tool profiles, `RoslynMcp.jsonc`, environment variables, logs. The [solution entry settings](docs/setup.md#direct-project-loads-and-builds) can require a solution and customize the guidance for direct project loads and builds.

## Good to know

- **C# only.** Semantic tools work on C# projects that MSBuild can load. Other files can be read and searched as text.
- **Edits are picked up.** Saved changes to `.cs` files already in the solution sync into the semantic model automatically. After adding or deleting files, or changing project files, the agent calls `load_workspace` again. Unsaved editor buffers aren't visible.
- **It runs with your permissions.** The server reads and writes files and starts `dotnet` and Git as your user. It is not a sandbox. Use version control and review what the agent changes.
- **It stays local.** No remote telemetry or hosted backend: the server sends nothing on its own. Network access happens only through operations you or the agent request: NuGet commands, and the builds, tests and applications the tools start, which can reach the network themselves. Logs, including incoming tool arguments, stay in your user profile, and so do local usage statistics, which are on by default and turned off with `ROSLYN_MCP_USAGE=0`. See [PRIVACY.md](PRIVACY.md) and [Logs](docs/setup.md#logs).
- **Platforms.** Windows x64, Linux x64 and macOS ARM64. Native archive smoke passed on all three for the .NET 11 candidate. [Platform validation](docs/github-releases/README.md)

## Documentation

- [Installation and configuration](docs/setup.md): platforms, clients, tool profiles, configuration, logs, security
- [Tool reference](docs/tools.md): every tool and its parameters
- [`AGENTS.md.sample`](AGENTS.md.sample): the agent policy to copy into your repository
- [Architecture](docs/ARCHITECTURE.md): how the workspace, file watching, build and test tools fit together
- [CHANGELOG](CHANGELOG.md): agent-visible changes by version
- [Plugin guide](samples/RoslynMcpPlugin/README.md): adding your own tools

## Building from source

Requires the .NET 11 SDK pinned in [`global.json`](global.json). Run the published executable:

```bash
dotnet publish RoslynMcpServer.csproj -c Release -r linux-x64   # or win-x64, osx-arm64
# → bin/Release/net11.0/linux-x64/publish/RoslynMcpServer
```

## License

[MIT](LICENSE)
