# Epoch 3 — Dynamic tool groups

## Mission

Allow a lite MCP session to enable additional typed tool groups at runtime and
notify compatible clients through `tools/list_changed`. Startup groups remain
the mandatory fallback. This epoch must not add dynamic disable/removal or a
generic dispatch tool.

## Prerequisites

- Epochs 1 and 2 are complete.
- Read [README.md](README.md) in this directory and both previous handoffs.
- Confirm the exact `ModelContextProtocol` 1.3.0 `ToolCollection` and
  notification APIs from the installed package/source.
- Confirm that excluded lite tools are not registered through `WithTools<T>`.

## Required tool

Add `enable_tool_group`.

Input:

- `group`: one exact group name, matched case-insensitively.

Behavior:

- validate the group through the central catalog;
- add every missing tool in that group to the active SDK tool collection;
- preserve typed schemas and the existing handler activation path;
- be thread-safe and idempotent;
- never create duplicate tool names;
- send `tools/list_changed` only when the active collection actually changes;
- return compact Markdown listing newly enabled tools and all active groups.

For an unknown group, return valid groups and suggest `list_tool_groups`.

For an already enabled group or `full` profile, return a clear no-op result and
do not send a misleading notification.

## Runtime state

Create a focused singleton service responsible for:

- active profile;
- startup groups;
- dynamically enabled groups;
- descriptor/factory lookup;
- atomic group enablement;
- actual active tool count.

The service must not contain business logic from individual tools. It should
use the catalog created in Epoch 1 and the same factories used at startup.

Current transport is stdio, but avoid unsafe global mutation assumptions that
would break if HTTP/multiple sessions are added later. Document any unavoidable
stdio-only lifecycle constraint.

## Client notification

After a successful change, use the SDK notification API for
`tools/list_changed`.

Do not assume notification support merely because the server sends it.
`enable_tool_group` output must include the compatibility fallback:

```text
Restart with ROSLYN_MCP_TOOL_GROUPS=<group>
```

Do not use a custom `ListToolsHandler` to filter registered tools; SDK 1.3
combines custom handler results with its registered collection.

## No disable in this epoch

Do not implement `disable_tool_group` or a mutable profile switch:

- removing schemas may not reclaim context already consumed by the client;
- in-flight calls and concurrent list requests complicate correctness;
- the first release only needs progressive expansion.

## Observability

Update existing info/health output to distinguish:

- profile;
- startup groups;
- dynamic groups;
- actual current tool count.

Log group enablement as a concise event without serializing full schemas.

## Primary files

- Epoch 1 catalog and profile service under `Hosting/`
- new runtime group manager service
- registration code in `Hosting/RoslynMcpServiceCollectionExtensions.cs`
- bootstrap/help tool host from Epoch 2
- `Tools/ServerLifecycleTools.cs`
- `Services/WorkspaceHealthReporter.cs`
- integration and unit tests

## Tests

Unit coverage:

- unknown group;
- case normalization;
- first enable adds expected tools;
- repeated enable is a no-op;
- concurrent enables do not duplicate tools;
- `full` enable is a no-op;
- actual count and active-group reporting;
- notification sent exactly once for a real change and never for no-op/error.

Integration coverage with an MCP test client:

1. Start in `lite`.
2. Call `tools/list` and prove a selected group tool is absent.
3. Call `enable_tool_group`.
4. Observe `tools/list_changed`.
5. Call `tools/list` again and prove the typed schema is present.
6. Successfully invoke one newly enabled read-only tool.

Use a read-only tool for the integration invocation to avoid filesystem side
effects.

## Manual compatibility matrix

Prepare, but do not claim, results for:

- Cursor with the target local model;
- OpenCode with the target local model;
- an MCP SDK/Inspector client.

For each client record:

- notification received;
- list refreshed without reconnect;
- newly enabled schema becomes callable by the model;
- prompt/catalog size before and after;
- startup-group fallback result.

Actual broad client validation belongs to Epoch 4.

## Acceptance criteria

- Lite startup remains compact.
- Enabling a group preserves individual typed tools and approvals.
- Notification behavior is correct and tested server-side.
- Unsupported clients can achieve the same initial set through startup groups.
- No dynamic removal or generic dispatcher is introduced.
- Build and tests pass.

## Out of scope

- Dynamic disable/removal.
- Model/context-size auto-detection.
- Pagination as a catalog-reduction mechanism.
- Aliases implemented as duplicate tools.
- Final docs, publish, and claims of Cursor/OpenCode compatibility.

## Versioning handoff

`enable_tool_group` is a new public capability. Follow the repository version
policy if this epoch ships independently. Otherwise record the integrated
release state and leave final publishing ownership to Epoch 4.
