# Epoch 2 — Compact descriptions and Markdown help

## Mission

Reduce the permanent schema cost while preserving tool-selection accuracy.
Move long operational guidance to just-in-time Markdown help. This epoch must
not implement runtime group activation or client notifications.

## Prerequisites

- Epoch 1 is complete and its catalog is the source of truth.
- Read [README.md](README.md) in this directory and the Epoch 1 handoff.
- Capture the current minified full/lite catalog sizes before editing.

## Description policy

Tool-level `[Description]` should contain only:

- what the tool does;
- a non-obvious prerequisite such as `load_workspace`;
- whether it writes files or executes a process;
- one critical routing distinction when similarly named tools exist.

Parameter descriptions should contain only semantics not already expressed by:

- the parameter name;
- JSON type/nullability;
- required/default values;
- a short obvious example.

Remove from permanent descriptions:

- historical bug narratives;
- exhaustive MSBuild/VSTest failure taxonomy;
- repeated saved-file watcher explanations;
- repeated path-resolution rules;
- repeated `configuration`/`platform` inheritance prose;
- Markdown emphasis and backticks that do not improve tool selection;
- documentation already represented in the generated schema.

Prefer concise plain text. Keep critical safety language for mutation,
preview/dry-run defaults, process execution, and destructive operations.

## Distinctions that must remain discoverable

Do not merge or blur:

- `get_code_skeleton`: disk file or directory, no workspace;
- `get_class_skeleton`: a document in the loaded workspace;
- decompiled skeleton/body tools: external assemblies;
- `find_usages`: solution-wide search by declared name;
- `find_symbol_references`: references when a declaring file is known;
- `find_implementations`: interface/base hierarchy;
- specialized build/test/run tools versus raw `execute_dotnet_command`.

Prioritize the eight largest descriptions first:

- `load_workspace`;
- `run_specific_test`;
- `find_symbol_definition`;
- `search_code`;
- `run_dotnet_test`;
- `run_dotnet_build`;
- `get_code_skeleton`;
- `find_usages`.

## Just-in-time help

Add two read-only bootstrap tools:

### `list_tool_groups`

Return compact Markdown containing:

- group name;
- one-line purpose;
- whether it is active;
- tool names in the group;
- startup fallback syntax.

Do not include every parameter or full tool documentation.

### `get_tool_help`

Input: exact tool name.

Return Markdown containing:

- purpose and read/write/process classification;
- prerequisites;
- parameters generated from the actual registered schema/catalog;
- focused workflow guidance;
- relevant pitfalls only;
- related tools where confusion is likely.

Unknown names must return a concise error plus close valid names. Help must not
contain machine-specific paths, secrets, or a copy of the full README.

## Help source of truth

- Generate names, types, required/default data and group membership from the
  catalog/schema.
- Store only supplemental long-form guidance in one centralized help catalog.
- Do not duplicate complete parameter documentation in attributes and help.
- Do not move the full catalog into `ServerInstructions`; that would preserve
  the permanent token cost under another field.
- If server instructions are used, limit them to one short sentence explaining
  that lite groups can be discovered and enabled.

## Primary files

- all affected files under `Tools/`
- Epoch 1 catalog under `Hosting/`
- `Tools/ServerLifecycleTools.cs` or a dedicated small help host
- a new centralized help catalog/service
- relevant unit tests

Do not rewrite tool implementations while editing their attributes.

## Tests

Cover:

- every registered tool has a non-empty concise description;
- every parameter that needs help still has a valid schema;
- no public tool schema loses required/default/type information;
- help for a known tool reflects its actual parameters and group;
- unknown-tool suggestions;
- group Markdown in `full`, `lite`, and lite with startup groups;
- help output contains no absolute developer-machine paths;
- tool invocation behavior remains unchanged.

Update deterministic size budgets after measuring actual results. Targets:

- full minified catalog `<= 45 KB`;
- lite minified catalog `<= 20 KB`;
- description text reduced by at least 40% from the captured baseline.

If a target cannot be met without harming routing or safety, document the
measured exception instead of hiding essential information.

## Acceptance criteria

- Full and lite profiles still expose the expected tools.
- Existing method behavior is untouched.
- Long help is available on demand as Markdown.
- JSON Schema remains the protocol contract.
- Catalog size and description reductions are recorded.
- Build and tests pass.

## Out of scope

- `enable_tool_group`.
- Mutating SDK `ToolCollection` at runtime.
- `tools/list_changed`.
- Generic dispatch/facade tools.
- Client compatibility testing.
- Final README/version/publish work unless this epoch ships independently.

## Versioning handoff

The two new public help tools are additive MCP capabilities. Follow the
repository version policy if this epoch is shipped independently. If all epochs
form one unreleased feature, preserve the agreed release-version ownership for
Epoch 4 and state it in the handoff.
