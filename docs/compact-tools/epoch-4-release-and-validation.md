# Epoch 4 — Release and client validation

## Mission

Review the integrated implementation from Epochs 1–3, validate actual context
and routing improvements on target clients, synchronize documentation/version,
publish, reload, and produce the final evidence-based handoff. Do not redesign
the feature during release unless validation exposes a blocking defect.

## Prerequisites

- Epochs 1–3 are complete with passing automated tests.
- Read [README.md](README.md) in this directory and every epoch handoff.
- Inspect current git status and preserve unrelated user changes.
- Identify whether earlier epochs were already versioned or published; never
  overwrite their version history with a guessed number.

## Code review checklist

Verify before release:

- `full` remains the default and exposes every legacy tool;
- `lite` excludes non-core tools before SDK registration;
- startup groups are present in the first `tools/list`;
- dynamic enablement is thread-safe and idempotent;
- `tools/list_changed` is emitted only after a real addition;
- Markdown is used for JIT help/output, not instead of JSON Schema;
- compact descriptions retain prerequisites and mutation/process warnings;
- there is no generic dispatcher or runtime disable;
- catalog, registration, DI, counts, help, and tests use one source of truth;
- no secrets or machine-specific paths were introduced.

## Automated verification

Use this repository's MCP tools:

1. Load the appropriate solution/project.
2. Run a non-incremental build.
3. Run the complete test suite.
4. Run focused profile/catalog/dynamic-group tests where useful.
5. Capture actual minified `tools/list` UTF-8 sizes and counts for:
   - `full`;
   - `lite`;
   - `lite` with each startup group;
   - `lite` after one representative dynamic enable.

Expected targets:

- full catalog `<= 45 KB`;
- lite catalog `<= 20 KB`;
- description text reduced by at least 40% from baseline;
- full default retains all legacy names plus intentionally added bootstrap/help
  tools.

If targets differ, publish measured numbers and technical reasons. Do not alter
schemas solely to make a number green when it harms safety or routing.

## Client validation

Validate at least:

- Cursor with the target local Qwen model;
- OpenCode with the same or equivalent local runtime;
- MCP Inspector or SDK test client as protocol control.

For each client record:

- whether server instructions are passed to the model;
- initial visible tool count;
- measured prompt/context cost if the client exposes it;
- whether `tools/list_changed` triggers refresh without reconnect;
- whether the model can select and call a newly enabled typed tool;
- whether startup `ROSLYN_MCP_TOOL_GROUPS` works after restart;
- one semantic navigation task;
- one build/test task;
- one task requiring a dynamically enabled group;
- routing failures or hallucinated tool names.

Do not state that a client supports dynamic refresh without observed evidence.

## Documentation

Update `README.md`:

- correct the stale registered-tool count;
- explain `ROSLYN_MCP_TOOL_PROFILE=full|lite`;
- document `ROSLYN_MCP_TOOL_GROUPS`;
- list group names and lite-core intent without duplicating every schema;
- document `list_tool_groups`, `get_tool_help`, and `enable_tool_group`;
- explain that Markdown supplements but does not replace MCP JSON Schema;
- document `tools/list_changed` client dependency and startup fallback;
- add the new version to “Agent tools by version” and update the RU pointer.

Update `opencode.json.sample` with portable examples for:

- full;
- lite;
- lite plus startup groups.

Update `AGENTS.md.sample` only if session policy changes. Do not copy profile
parameter documentation into it.

Update tool-count assertions/hints in server info, workspace health, tests, and
docs from the catalog source of truth.

## Version and publish

- Determine the correct next version from repository state and prior epoch
  handoffs.
- Because the integrated feature adds public MCP capabilities, use a minor
  version when it has not already been versioned.
- Keep `Version`, `AssemblyVersion`, and `FileVersion` synchronized.
- Do not change `PackageVersion` unless packaging intentionally changes.
- After successful verification, publish with the repository script/process,
  reload MCP, and confirm `get_mcp_server_info` reports:
  - expected binary;
  - expected version;
  - expected profile;
  - expected active tool count.

Run final smoke checks against the newly loaded binary, not the stale server
process used to build it.

## Acceptance criteria

- Automated build and complete tests pass.
- Full and lite size/count evidence is recorded.
- Dynamic refresh is accurately classified per tested client.
- Startup fallback is verified.
- README, config sample, tool reference, version history and reported counts
  agree.
- Published MCP reports the new version.
- Final handoff lists remaining client limitations and no unsupported claims.

## Out of scope

- Adding more profiles or tool groups discovered during validation.
- Replacing typed tools with a dispatcher.
- Runtime group disable/removal.
- Client-specific protocol forks.
- Unrelated tool behavior changes or repository cleanup.

## Final report format

Provide:

1. shipped version and binary path;
2. full/lite counts and byte sizes;
3. description reduction percentage;
4. automated test summary;
5. client compatibility results;
6. known limitations and fallback;
7. changed documentation files;
8. any follow-up proposals, clearly outside the shipped scope.
