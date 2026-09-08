# Epoch 4 handoff — release and client validation

Status: **shipped as v1.2.0**. Epochs 1–3 are one integrated minor release.
`PackageVersion` stays `0.1.0-beta`.

## Shipped version and binary

- **Version:** `1.2.0` (`AssemblyVersion` / `FileVersion` `1.2.0.0`)
- **Binary (win-x64):** `E:\Devel\RoslynMcpServer\bin\Release\net10.0\win-x64\publish\RoslynMcpServer.exe`
- **FileVersion on disk:** `1.2.0.0` (published 2026-09-08 18:34:43 +03, 162,816 bytes)
- Confirm after Reload MCP: `get_mcp_server_info` must report this version, profile
  `full` (default), and **62** tools.

## Code review (no redesign)

- `full` is the default and registers every catalog tool.
- `lite` excludes non-core tools before SDK registration (`RegisterSelectedTools`
  from `McpToolCatalog`; no class-level `WithTools<T>`).
- Startup groups appear in the first `tools/list`.
- `enable_tool_group` is thread-safe and idempotent; `tools/list_changed` only
  on a real addition.
- Markdown is JIT help/output. `tools/list` stays JSON Schema.
- Compact descriptions keep prerequisites and write/process flags.
- No generic dispatcher and no runtime disable.
- Catalog remains the single source of truth.
- No secrets or machine-specific paths in help or samples.

## Measured surface (minified `tools/list` UTF-8)

| Surface | Tools | Bytes | KB |
|---|---:|---:|---:|
| `full` (default) | 62 | 38,500 | 37.6 |
| `lite` | 18 | 11,265 | 11.0 |
| `lite` + `files` (startup or `enable_tool_group`) | 25 | 15,493 | 15.1 |
| `lite` + `editing` | 35 | 22,141 | 21.6 |
| `lite` + `decompile` | 22 | 14,183 | 13.8 |
| `lite` + `nuget` | 24 | 14,878 | 14.5 |
| `lite` + `project` | 21 | 12,902 | 12.6 |
| `lite` + `runtime` | 21 | 13,311 | 13.0 |
| `lite` + `operations` | 22 | 13,182 | 12.9 |

Budgets: full `<= 45 KB` (pass), lite startup `<= 20 KB` (pass).
`lite`+`editing` is **21.6 KB** — above the *startup-lite* budget because it adds
17 editing tools. Not a failure of the lite target.

Description characters on the original 59 tools: **35,445 → 13,387 (−62%)**.
All 62 tools including help: 13,855.

Recorded in `McpToolCatalogTests.Epoch4_surface_sizes_match_recorded_release_numbers`.

## Automated tests

Non-incremental `dotnet build` succeeded. Full suite: **238 passed**, **1 failed**
(pre-existing environment, unrelated to this feature):
`NuGetFallbackAssemblyResolverTests.TryFindAssemblyDll_finds_system_io_ports_via_bcl_map_when_package_present`
(package not present on this machine). Same leftover as Epochs 2–3.

Focused: catalog size recording, help, and `McpToolGroupEnablement*` all green.

## Client compatibility

Do **not** treat empty cells as “supported”. Only observed results are filled.

| Client | Instructions to model | Initial tool count | Context cost | `tools/list_changed` refresh | New typed tool callable | Startup-group fallback | Notes |
|---|---|---|---|---|---|---|---|
| Cursor (this release chat) | not observed as Qwen prompt injection | 59 on stale **v1.1.1** binary before publish | not exposed | **not claimed** | **not claimed** on the stale binary | not exercised live | This session used the published 1.1.1 process, not Qwen. Reload MCP after publish, then re-check `get_mcp_server_info`. |
| OpenCode + local model | not observed | not observed | not observed | **not claimed** | **not claimed** | config samples only | [`opencode.json.sample`](../../opencode.json.sample) has full / lite / lite+groups. |
| MCP SDK test client | n/a | lite 18 | n/a | **yes** (in-process) | **yes** (`list_directory_tree` after `files`) | startup-group no-op covered in unit tests | `McpToolGroupEnablementIntegrationTests` |

### Observed Cursor smoke (pre-publish, this chat)

- Semantic navigation: `load_workspace` on this solution succeeded (2 projects).
- Build/test: `run_specific_test` used for catalog size dump and prior Epoch 3 tests.
- Dynamic group: **not available** on the 1.1.1 binary still loaded in this chat.

### Known limitations

- Clients that ignore `tools/list_changed` must restart with
  `ROSLYN_MCP_TOOL_GROUPS`.
- No runtime disable/removal.
- Stdio host: one process-wide tool collection (not per HTTP session).
- Qwen-on-Cursor and OpenCode live routing were **not** validated in this epoch
  chat. Do not advertise them as confirmed.

## Documentation changed

- `README.md` — tool count 56→62, profiles, three new tools, v1.2.0, RU pointer
- `opencode.json.sample` — full / lite / lite+groups
- `AGENTS.md.sample` — expect v1.2.0+ after rebuild (no profile parameter copy)
- `docs/compact-tools/README.md` — Epoch 4 shipped
- `.cursor/rules/roslyn-mcp-overview.mdc`, `roslyn-mcp-docs-agents.mdc` — 1.2.0

## Follow-up (outside shipped scope)

- Live Cursor + Qwen: after Reload MCP, record whether the host refreshes
  `tools/list` without reconnect and whether the model calls a newly enabled tool.
- Same matrix on OpenCode.
- MCP Inspector as a second protocol control if needed.
- Do not add disable, extra profiles, or a dispatcher based on those results.
