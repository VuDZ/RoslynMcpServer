# Epoch 3 handoff — dynamic tool groups

Status: **implemented, not released**. Part of the unreleased multi-epoch
compact-tools feature. Do not bump `RoslynMcpServer.csproj` or publish until
Epoch 4 (or an intentional independent ship). Current shipping version remains
**1.1.1**.

## What landed

- `enable_tool_group` is a lite-core (`core`) tool. It validates one catalog
  group, adds every missing typed tool to the live SDK `ToolCollection`, and
  is idempotent.
- Runtime state lives in `McpToolActivationService` (profile, startup groups,
  dynamic groups, live count). It uses the Epoch 1 catalog factories.
- The process owns one `McpRuntimeToolCollection`. SDK 1.3 sends
  `notifications/tools/list_changed` from `ToolCollection.Changed`. The
  derived collection batches inserts so one group enablement raises **one**
  notification. No-op and unknown-group paths do not notify.
- There is no disable/removal and no generic dispatcher.
- `get_mcp_server_info`, workspace health, and `list_tool_groups` now report
  profile, startup groups, **dynamic groups**, and the **current** tool count.
- Compatibility fallback is always present:

  `Restart with ROSLYN_MCP_TOOL_GROUPS=<group>`

## SDK 1.3 notes

- Tools are registered as `Func<IServiceProvider, McpServerTool>` and copied
  into `McpServerOptions.ToolCollection` by `McpServerOptionsSetup`.
- `McpServerImpl` subscribes to `ToolCollection.Changed` and sends
  `notifications/tools/list_changed`. Do not add a custom `ListToolsHandler`
  to hide tools — the SDK concatenates handler results with the collection.
- `TryAdd` notifies per tool. `McpRuntimeToolCollection.TryAddMany` inserts
  into the SDK dictionary and calls `RaiseChanged()` once.

## Stdio constraint

The current host is stdio: one session per process. The activation service and
tool collection are process-wide singletons. A future HTTP/multi-session host
must not reuse them as per-session state.

## Measured surface (startup, before any enable)

Lite remains compact. Adding `enable_tool_group` does not change the Epoch 2
size budgets (full 45 KB, lite 20 KB). Recapture exact UTF-8 sizes from
`McpToolCatalogTests.Minified_tools_list_sizes_are_within_epoch2_budgets`.

| Profile | Tools at startup |
|---|---:|
| `full` (default) | 62 |
| `lite` | 18 |

Lite core (18): Epoch 2 seventeen plus `enable_tool_group`.

## Tests

- Unit: unknown group, case normalization, first enable, repeated enable,
  concurrent enable, `full` no-op, startup-group no-op, live count / active
  groups, notification once on change and never on no-op/error.
- Integration (`McpToolGroupEnablementIntegrationTests`): in-process MCP
  client, lite `tools/list`, `enable_tool_group` for `files`, observe
  `tools/list_changed`, list again, invoke read-only `list_directory_tree`.

Full suite: **237 passed**. One pre-existing environment failure remains
unrelated to this epoch:
`NuGetFallbackAssemblyResolverTests.TryFindAssemblyDll_finds_system_io_ports_via_bcl_map_when_package_present`
(package not present on this machine).

## Manual compatibility matrix (prepare only)

Record in Epoch 4, do not claim now:

| Client | Notification received | List refreshed without reconnect | Newly enabled tool callable | Catalog size before/after | Startup-group fallback |
|---|---|---|---|---|---|
| Cursor + target local model | | | | | |
| OpenCode + target local model | | | | | |
| MCP Inspector / SDK client | | | | | |

## Versioning

Still one unreleased multi-epoch feature. Epoch 4 owns the version bump,
publish/reload, README tool-count correction, and client compatibility claims.
`enable_tool_group` is additive and does not ship until then.

## Prerequisites for Epoch 4

- Publish `win-x64` Release and reload MCP before claiming live client behavior.
- Keep startup groups as the fallback for clients that ignore
  `tools/list_changed`.
- Do not add disable, pagination-as-reduction, aliases, or a dispatch facade.
- Retighten docs and the public README tool count against the 62-tool catalog.
