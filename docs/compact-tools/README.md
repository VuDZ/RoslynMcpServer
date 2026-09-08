# Compact MCP tools: implementation epochs

This directory contains handoff documents for implementing a smaller MCP tool
surface for local models with limited effective context.

The documents are intended to be given to separate implementation chats in
order. Each epoch must treat the repository state produced by previous epochs
as base truth and must not implement later epochs early.

Epoch 1 is implemented; see [epoch-1-handoff.md](epoch-1-handoff.md).
Epoch 2 is implemented; see [epoch-2-handoff.md](epoch-2-handoff.md).

## Fixed architectural decisions

- Configuration is named `tool_profile`, not `model_type`.
- Runtime value is supplied through `ROSLYN_MCP_TOOL_PROFILE=full|lite`.
- `full` is the backward-compatible default.
- `ROSLYN_MCP_TOOL_GROUPS` enables extra groups at process startup.
- MCP `tools/list` remains JSON with JSON Schema. Markdown never replaces the
  protocol schema.
- Markdown is used for just-in-time tool help, group listings, diagnostics, and
  workflow examples.
- Tools excluded from a profile are not registered through `WithTools<T>`.
  A custom list handler cannot hide tools already present in the SDK tool
  collection.
- Do not introduce a generic `dispatch(name, arguments)` facade in this work.
- Runtime dynamic expansion is an enhancement. Startup groups remain the
  compatibility fallback for clients that ignore `tools/list_changed`.
- Do not add runtime disable/removal in the first release.

## Epoch order

1. [Epoch 1 — Catalog and startup profiles](epoch-1-catalog-and-profiles.md)
2. [Epoch 2 — Compact descriptions and Markdown help](epoch-2-compact-descriptions-and-help.md)
3. [Epoch 3 — Dynamic tool groups](epoch-3-dynamic-tool-groups.md)
4. [Epoch 4 — Release and client validation](epoch-4-release-and-validation.md)

## Baseline

- Current server code registers 59 MCP tools across 16 host classes.
- The observed full tool catalog is approximately 68 KB of formatted JSON.
- Tool and parameter descriptions contain approximately 36,000 characters.
- Eight verbose tools account for roughly 31% of description text:
  `load_workspace`, `run_specific_test`, `find_symbol_definition`,
  `search_code`, `run_dotnet_test`, `run_dotnet_build`,
  `get_code_skeleton`, and `find_usages`.
- `UtilityTools` contains 14 unrelated tools, so class-level filtering is not
  sufficiently granular.
- `README.md` currently reports a stale tool count and must be corrected only
  in the release/documentation epoch unless an earlier epoch ships separately.

## Rules for every implementation chat

- Read this index and the assigned epoch document before editing.
- Inspect current code and git status; do not overwrite unrelated user changes.
- Keep `McpToolRegistry` or its replacement as the single source of truth.
- Use MCP build/test tools for verification as required by repository rules.
- Apply the repository version-bump rule whenever an epoch is shipped or the
  rebuilt server is published/reloaded. If all epochs remain one unreleased
  feature, coordinate one final minor release rather than guessing versions.
- Record actual tool counts and serialized UTF-8 sizes in the handoff.
- Stop after the assigned epoch and report prerequisites for the next one.
