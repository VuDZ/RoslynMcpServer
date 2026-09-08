# Epoch 1 handoff — catalog and startup profiles

Status: **implemented, not released**. This is part of the unreleased multi-epoch
compact-tools feature. Do not bump `RoslynMcpServer.csproj` or publish until
Epoch 4 (or an intentional independent ship). Current shipping version remains
**1.1.1**.

## What landed

- `Hosting/McpToolCatalog.cs` is the single source of truth (59 tools, method
  granularity). It drives DI host types, MCP SDK registration, profile/group
  selection, counts, and tests.
- Class-level `WithTools<T>()` lists are gone. `UtilityTools` is filtered by
  public tool name.
- `ROSLYN_MCP_TOOL_PROFILE=full|lite` (empty/unset = `full`).
- `ROSLYN_MCP_TOOL_GROUPS=decompile,nuget` expands `lite` before the first
  `tools/list`. In `full` it is recorded but does not change the set.
- Unknown profile/group fails startup with an actionable message.
- `get_mcp_server_info` and workspace health report **active** profile, startup
  groups, and registered count (not the static 59).
- Reserved names for later epochs are **not** registered:
  `list_tool_groups`, `get_tool_help`, `enable_tool_group`.

## Developer note (run / test)

No extra env is required for the test suite. Tests pass explicit
`McpToolProfileOptions` so ambient `ROSLYN_MCP_*` cannot change results.

To run the server in lite locally:

```text
ROSLYN_MCP_TOOL_PROFILE=lite
ROSLYN_MCP_TOOL_GROUPS=decompile,nuget
```

PowerShell:

```powershell
$env:ROSLYN_MCP_TOOL_PROFILE = "lite"
$env:ROSLYN_MCP_TOOL_GROUPS = "decompile,nuget"
```

Valid groups: `core`, `files`, `editing`, `decompile`, `nuget`, `project`,
`runtime`, `operations`.

## Measured surface

| Profile | Tools | Minified `tools/list` UTF-8 |
|---|---:|---:|
| `full` (default) | 59 | 64,181 bytes (62.7 KB) — under 70 KB |
| `lite` | 15 | 23,115 bytes (22.6 KB) |

Epoch 1 asked for `lite <= 20 KB` before description compaction. The 15 core
tools still carry the current verbose `[Description]` text (including
`load_workspace`, `run_specific_test`, `find_symbol_definition`,
`run_dotnet_test`, `run_dotnet_build`, `get_code_skeleton`, `find_usages`).
Epoch 1 must not shorten those. The automated budget is **24 KB** for this
epoch; Epoch 2 still owns the **20 KB** target after compaction.

## Lite core (15)

`get_mcp_server_info`, `load_workspace`, `reset_workspace`, `get_code_skeleton`,
`get_class_skeleton`, `get_diagnostics_for_file`, `find_symbol_definition`,
`find_usages`, `find_symbol_references`, `find_implementations`,
`get_call_graph`, `run_dotnet_build`, `run_dotnet_test`, `run_specific_test`,
`get_changed_files`.

## Prerequisites for Epoch 2

- Catalog/group membership is stable; compact `[Description]` in place, do not
  add `enable_tool_group` or `tools/list_changed`.
- Capture the sizes above **before** editing descriptions.
- Add `list_tool_groups` and `get_tool_help` using this catalog (no fake
  placeholders exist today).
- After compaction, retighten the lite size budget to 20 KB and full to 45 KB
  (Epoch 2 targets).
