# Epoch 1 — Tool catalog and startup profiles

## Mission

Introduce a single tool catalog and reliable startup selection for
`full`/`lite` profiles. This epoch must reduce the initial `tools/list` by
excluding tools before SDK registration. It must not implement runtime
`tools/list_changed`, bulk description rewriting, or Markdown help bodies.

## Prerequisites

- Read [README.md](README.md) in this directory.
- Treat existing tool methods and behavior as base truth.
- Inspect the current `ModelContextProtocol` 1.3.0 APIs before relying on
  programmatic registration.

## Required behavior

### Configuration

- Add a strongly typed Options model for:
  - `ROSLYN_MCP_TOOL_PROFILE=full|lite`
  - `ROSLYN_MCP_TOOL_GROUPS=<comma-separated groups>`
- Matching is case-insensitive and surrounding whitespace is ignored.
- Empty profile means `full`.
- Unknown profiles and groups fail startup with a concise actionable message.
- Duplicate startup groups are harmless.

### Catalog

Create one source of truth containing, at minimum:

- public MCP tool name;
- reflected host method or a factory capable of creating `McpServerTool`;
- host type used by DI;
- logical group;
- inclusion in the `lite` core;
- read-only versus mutating classification.

The catalog must drive:

- DI registration;
- MCP SDK tool registration;
- profile/group selection;
- registered-tool counts;
- tests and later help generation.

Do not maintain a second hardcoded `.WithTools<T>()` list beside the catalog.
Registration must work at method/tool-name granularity because
`Tools/UtilityTools.cs` contains unrelated operations.

### Profiles

`full`:

- Includes all 59 existing tools.
- Remains the default.
- Preserves existing names, schemas, activation, and behavior.

`lite` core should include only these existing capabilities:

- server information;
- workspace load/reset;
- disk/workspace skeletons;
- file diagnostics;
- symbol definition, usages, references, implementations, and call graph;
- build;
- full and specific test execution;
- changed-files summary.

Reserve names for the bootstrap tools introduced by later epochs, but do not
implement fake placeholders.

Suggested optional groups:

- `files`: disk read/range/search/tree/write/patch;
- `editing`: AST edits, code fixes, rename, formatting, refactorings;
- `decompile`: assembly exploration and decompilation;
- `nuget`: registry, package listing/audit and package-reference changes;
- `project`: project listing, graph and project rename;
- `runtime`: run and raw dotnet execution;
- `operations`: logs, scratchpad and server stop.

The exact mapping may be adjusted when a tool has a safety or dependency reason,
but every tool must belong to exactly one documented group or the lite core.

### Startup groups

`ROSLYN_MCP_TOOL_GROUPS=decompile,nuget` adds those groups before the first
`tools/list`. It must work in both profiles:

- in `lite`, it expands the selected surface;
- in `full`, it is an idempotent no-op.

This is the required fallback for clients without runtime list refresh.

### Observability

Extend server info and workspace health output with:

- active profile;
- startup groups;
- actual registered tool count.

Do not report the static total as the active count.

## Primary files

- `Program.cs`
- `Hosting/McpToolRegistry.cs`
- `Hosting/RoslynMcpServiceCollectionExtensions.cs`
- new catalog/options files under `Hosting/`
- `Services/WorkspaceHealthReporter.cs`
- `Tools/ServerLifecycleTools.cs`
- `RoslynMcpServer.Tests/McpToolActivationTests.cs`

Avoid splitting or rewriting existing large tool classes solely for grouping.

## Tests

Add automated coverage for:

- default profile is `full`;
- explicit `full` contains every pre-existing tool;
- `lite` contains only the agreed core;
- startup groups expand `lite`;
- invalid profile/group rejection;
- casing, whitespace and duplicate groups;
- unique public tool names;
- every catalog entry can be activated through DI;
- active counts in server info/health;
- existing tool activation tests continue to pass.

Add a deterministic catalog-size test that serializes the actual minified
`tools/list` representation and measures UTF-8 bytes. Initial budgets:

- `full <= 70 KB` before Epoch 2 compaction;
- `lite <= 20 KB`.

Do not assert tokenizer-specific token counts.

## Acceptance criteria

- Starting with no profile produces the same 59 existing tools.
- Starting with `lite` never registers excluded tools in the SDK collection.
- Startup groups are visible in the first list response without notification.
- No existing public tool name or invocation contract changes.
- Tests and build pass.
- The handoff reports full/lite counts and serialized sizes.

## Out of scope

- Shortening all descriptions.
- `get_tool_help` and `list_tool_groups`.
- `enable_tool_group`.
- Runtime add/remove or `tools/list_changed`.
- README/config sample updates beyond a temporary developer note needed to run
  tests.
- Publish/reload unless this epoch is intentionally released independently.

## Versioning handoff

If this epoch is shipped independently, profiles are an additive public
capability and require the repository's minor-version policy. If it remains
part of one unreleased multi-epoch feature, record that status clearly for the
release epoch and do not invent a conflicting final version.
