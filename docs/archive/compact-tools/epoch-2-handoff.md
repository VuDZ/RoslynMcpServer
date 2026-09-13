# Epoch 2 handoff — compact descriptions and Markdown help

Status: **implemented, not released**. Part of the unreleased multi-epoch
compact-tools feature. Do not bump `RoslynMcpServer.csproj` or publish until
Epoch 4 (or an intentional independent ship). Current shipping version remains
**1.1.1**.

## What landed

- Permanent `[Description]` text was compacted across all public tools and
  parameters. Routing distinctions, write/process flags, and
  `load_workspace` prerequisites stay in the schema.
- Long operational guidance moved to just-in-time Markdown:
  - `list_tool_groups` — group purpose, active state, member names, startup
    fallback (`ROSLYN_MCP_TOOL_PROFILE` / `ROSLYN_MCP_TOOL_GROUPS`).
  - `get_tool_help` — kind/group, live parameters from the method schema,
    workflow/pitfalls from `Hosting/McpToolHelpCatalog.cs`.
- Both tools are **lite-core** (`core` group). `enable_tool_group` stays
  reserved and unregistered.
- `McpServerOptions.ServerInstructions` is one sentence pointing at group
  discovery and startup enablement. The catalog is not copied there.
- Catalog descriptors now expose `ExecutesProcess` and `Classification`
  (`read` / `write` / `process`).

## Measured surface

Captured **before** description edits (Epoch 1, 59 tools):

| Profile | Tools | Minified `tools/list` UTF-8 | Description chars |
|---|---:|---:|---:|
| `full` | 59 | 64,181 (62.7 KB) | 35,445 (original 59 tools) |
| `lite` | 15 | 23,115 (22.6 KB) | (same verbose core text) |

After Epoch 2:

| Profile | Tools | Minified `tools/list` UTF-8 | Budget |
|---|---:|---:|---:|
| `full` (default) | 61 | 38,047 (37.2 KB) | 45 KB |
| `lite` | 17 | 10,812 (10.6 KB) | 20 KB |

Description text on the original 59 tools: **35,445 → 13,387** (−62%). All 61
tools including help: 13,670 characters.

Automated budgets in `McpToolCatalogTests`: full `45 * 1024`, lite `20 * 1024`.

## Lite core (17)

Epoch 1 fifteen, plus:

`list_tool_groups`, `get_tool_help`.

## Help contract

- Parameter names, JSON types, required/default values, and group membership
  are generated from the catalog method/schema, not copied into attributes.
- Supplemental workflow/pitfalls live only in `McpToolHelpCatalog`.
- Unknown tool names return a short error plus close valid names.
- Help Markdown must not contain machine-specific absolute paths.

## Tests

`McpToolCatalogTests` and `McpToolHelpTests` cover compact descriptions,
schema required/default/type preservation, group Markdown for `full`, `lite`,
and lite+startup groups, unknown-name suggestions, and size/description
budgets.

Full suite: **227 passed**. One pre-existing environment failure remains
unrelated to this epoch:
`NuGetFallbackAssemblyResolverTests.TryFindAssemblyDll_finds_system_io_ports_via_bcl_map_when_package_present`
(package not present on this machine).

## Versioning

Still one unreleased multi-epoch feature. Epoch 4 owns the version bump and
publish/reload. Additive public tools in this epoch (`list_tool_groups`,
`get_tool_help`) do not ship until then.

## Prerequisites for Epoch 3

- Use `list_tool_groups` / `get_tool_help` as they exist; do not add
  `enable_tool_group` placeholders.
- Confirm MCP SDK 1.3.0 `ToolCollection` and `tools/list_changed` APIs before
  mutating the registered set at runtime.
- Startup groups remain the compatibility fallback. Do not implement disable
  or a generic dispatch facade.
- After Epoch 3, retighten counts in server info to include dynamically
  enabled groups; size budgets from this epoch stay valid until tools are
  added at runtime (runtime add does not change the initial `tools/list`
  for clients that never enable groups).
