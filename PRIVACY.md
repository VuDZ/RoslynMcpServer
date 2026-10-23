# Privacy

RoslynMcpServer does not collect telemetry or transmit user data automatically.

Network access occurs only when explicitly requested by the user or MCP client
through functionality that requires access to an external service.

## Scope

The server is a local stdio process. It has no backend, no analytics SDK, and
no HTTP client of its own. Serilog writes rolling files under `logs/` next to
the executable. The internal `ToolTelemetry` helper is that local file log, not
a remote product-telemetry channel.

`load_workspace`, symbol search, diagnostics, AST edits, ILSpy decompile, and
`get_changed_files` (`git status --porcelain`) stay on the machine. Incoming
MCP parameters are logged locally by default; see the Logs section in
[README.md](README.md#logs).

## Local index cache

From 1.5.14 Roslyn keeps the syntax indexes of loaded solutions in a SQLite
database on the local disk, as Visual Studio does, so a restarted server does not
re-parse unchanged files. The cache lives under
`%LOCALAPPDATA%\Microsoft\VisualStudio\Roslyn\Cache\` on Windows and
`~/.local/share/Microsoft/VisualStudio/Roslyn/Cache/` on Linux, in a folder named
after the server executable and its path, with one subfolder per solution file.
It holds names and locations of declarations from your source code, never leaves
the machine, and is not cleaned up automatically. Deleting the folder is safe; the
next search rebuilds it. A workspace opened from a single project file without a
solution has no cache.

## When network is used

The process talks to the network only as a side effect of an explicit MCP tool
call that runs `dotnet` against a configured feed or service:

- `search_nuget_registry` — `dotnet package search` (nuget.org or configured feeds)
- `list_outdated_packages`, `run_nuget_audit`, and `list_nuget_packages` with
  `--outdated` / `--vulnerable`
- `run_dotnet_build` / `run_dotnet_test` / `run_specific_test` /
  `run_test_by_filter` / `run_dotnet_run` — restore or build may contact
  configured NuGet feeds
- `execute_dotnet_command` — only if the supplied arguments need the network
  (for example `add package` or `restore`)

Those calls send package ids, versions, and restore metadata required by the
NuGet / `dotnet` CLI. They do not upload source code, prompts, or workspace
contents to a RoslynMcpServer service.

`add_package_reference` only edits the `.csproj` on disk. It does not query
nuget.org until a later restore/build.

## Child `dotnet` processes

This server does not enable or add product telemetry. Child `dotnet` processes
inherit the machine's .NET SDK telemetry setting (`DOTNET_CLI_TELEMETRY_OPTOUT`).
That policy belongs to the SDK, not to RoslynMcpServer.
