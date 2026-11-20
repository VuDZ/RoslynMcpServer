# Privacy

RoslynMcpServer does not transmit usage statistics or user data automatically.

Network access occurs only through operations the user or MCP client requests:
NuGet commands, and the builds, tests, and applications that tools start, which
can reach the network themselves. See [When network is used](#when-network-is-used).

## Scope

The server is a local stdio process. It has no backend, no analytics SDK, and
no HTTP client of its own. Serilog writes rolling files under `logs/` in the
server's per-user state directory, or in `ROSLYN_MCP_LOG_DIR` when set. The internal `ToolTelemetry` helper is that local file log, not
a remote product-telemetry channel.

`load_workspace`, symbol search, diagnostics, AST edits, ILSpy decompile, and
`get_changed_files` (`git status --porcelain`) stay on the machine. Incoming
MCP parameters are logged locally by default; see the Logs section in
[docs/setup.md](docs/setup.md#logs).

## Local usage records

Usage collection is enabled by default and writes only to the local `usage/`
directory. The default roots are `%LOCALAPPDATA%\RoslynMcpServer\` on Windows,
`$XDG_STATE_HOME/roslyn-mcp/` (or `~/.local/state/roslyn-mcp/`) on Linux,
and `~/Library/Application Support/RoslynMcpServer/` on macOS.
`ROSLYN_MCP_HOME` can override that root; `ROSLYN_MCP_LOG_DIR` changes only
text logs.

Records contain process/client identity, tool names and timings, workspace
paths and load properties, and memory/activity measurements. Workspace paths
and cwd are plain text. Records contain no tool arguments, response text,
symbol names, environment values, or command lines. Nothing is uploaded.

Set `ROSLYN_MCP_USAGE=0` (or `false`) before launching the server to disable
collection. Files rotate at 32 MiB and expire after 60 days. Deleting the
`usage/` files is safe; an enabled collector creates new files.

The optional `scripts/Measure-McpUsage.ps1` analyzer runs locally and produces
JSON/Markdown aggregates with per-machine workspace labels such as `K1`.
Raw files stay with their owner or in private storage; only these aggregates
are suitable for sharing or committing. Collection on colleagues' machines
requires their agreement.

## Local index cache

From 1.5.14 Roslyn keeps the syntax indexes of loaded solutions in a SQLite
database on the local disk, as Visual Studio does, so a restarted server does not
re-parse unchanged files. The cache lives under
`%LOCALAPPDATA%\Microsoft\VisualStudio\Roslyn\Cache\` on Windows,
`~/.local/share/Microsoft/VisualStudio/Roslyn/Cache/` on Linux (or `$XDG_DATA_HOME`)
and `~/Library/Application Support/Microsoft/VisualStudio/Roslyn/Cache/` on macOS,
in a folder named after the executable of the process and its path, with one
subfolder per solution file. A server started as `dotnet RoslynMcpServer.dll` is
named after `dotnet`, so every framework-dependent server version and every other
Roslyn-based tool started through the same `dotnet` share that folder.
It holds names and locations of declarations from your source code, never leaves
the machine, and is not cleaned up automatically. Deleting the folder is safe; the
next search rebuilds it. A workspace opened from a single project file without a
solution has no cache.

## When network is used

The server process has no network client of its own and sends nothing to a
RoslynMcpServer service. Network access is a side effect of an MCP tool call
that starts MSBuild or another process, and it is whatever that process does:

- NuGet commands contact nuget.org or the configured feeds:
  `search_nuget_registry` (`dotnet package search`), `list_outdated_packages`,
  `run_nuget_audit`, and `list_nuget_packages` with `--outdated` / `--vulnerable`.
- Builds and restores: `run_dotnet_build`, `run_dotnet_test`,
  `run_specific_test`, `run_test_by_filter`, `run_dotnet_run`, and
  `estimate_affected_tests` with `run: true`. Restore may contact configured
  NuGet feeds, and custom MSBuild targets and tasks in your projects run as
  written.
- `load_workspace` evaluates your projects through MSBuild. Project SDKs that
  are distributed as NuGet packages may be resolved from feeds, and
  design-time targets in your projects run as written.
- Your own code: the tests started by the test tools and the application
  started by `run_dotnet_run` run with your permissions and can reach any
  network service they are written to use, such as databases, HTTP APIs, or
  cloud services. The server neither restricts nor observes that traffic.
- `execute_dotnet_command` does whatever the supplied arguments do, for example
  `add package` or `restore`.

NuGet operations send the package ids, versions, and restore metadata that the
NuGet / `dotnet` CLI requires. The server does not upload source code, prompts,
or workspace contents.

`add_package_reference` only edits the `.csproj` on disk. It does not query
nuget.org until a later restore/build.

## Child `dotnet` processes

This server does not enable or add product telemetry. Child `dotnet` processes
inherit the machine's .NET SDK telemetry setting (`DOTNET_CLI_TELEMETRY_OPTOUT`).
That policy belongs to the SDK, not to RoslynMcpServer.
