# Agent-facing MCP tools — changelog

Tracks tools and behaviors relevant to **`AGENTS.md`** / application repos. Server package version in `RoslynMcpServer.csproj`.

## v1.0.14

| Tool / behavior | Notes |
|-----------------|--------|
| `run_dotnet_run` | SDK-pinned `dotnet run`, separate stdout/stderr, timeout, truncated output (stderr tail for progress) |
| `run_nuget_audit` | Structured vulnerability table from `dotnet list package --vulnerable` |
| `get_changed_files` | Git porcelain status + suggested test projects (no diff body) |
| `load_workspace` | **Workspace health** block: SDK/global.json, restore assets, tool count |
| `execute_dotnet_command` | SDK pinning + truncated stdout/stderr |
| `find_usages` / `find_symbol_references` | Documented as **find_references** family; prefer `find_usages` when only `symbolName` is known |
| `get_project_graph` / `list_projects` | Project dependency graph |
| `rename_symbol` | `previewOnly=true` default workflow |
| `run_format` | Already present; listed for backlog closure |

## v1.0.13

| Tool / behavior | Notes |
|-----------------|--------|
| `AssemblyReferenceResolver` | Exact `{name}.dll` only; deps.json + NuGet fallback (no fuzzy `Contains`) |
| `DecompilerHost` | NuGet / BCL / runtime pack resolver for ILSpy tools |

## v1.0.10–v1.0.12

Build/test/SDK pinning, VSTest parser, decompiler `assemblyPath`, `MCP_MSBUILD_SDK_MISMATCH` — see git history / README.

## Not implemented (documented in AGENTS.md §7)

- Read-only TFS / HTTP probe MCP tools
- MCP tool reporting «secret configured: yes/no» without reading values
- `get_changed_files` including unified diff (use host/shell `git diff` when allowed)
