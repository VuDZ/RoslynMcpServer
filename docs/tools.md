# MCP tool reference

[Русская версия](tools.ru.md) · [Back to README](../README.md) · [Installation and configuration](setup.md)

Every tool also describes itself: `get_tool_help` returns the live parameters of a registered tool, and `list_tool_groups` shows which groups are active.

**Parameter Naming Rules:**
- `filePath` — a single file (read/edit/diagnostics/logs).
- `directoryPath` — root folder (`list_directory_tree`, optional root for `search_code`).
- `includeExtensions` — optional extension filter for `search_code` (`.cs` by default; `*` = all files).
- `caseSensitive` — optional for `search_code` (default `false`; use `true` for leftover branding checks).
- `workspacePath` — `.sln` / `.slnx` / `.csproj`, and for some tools a directory. File only: `load_workspace`, `run_dotnet_build` (not a directory), `run_dotnet_run` (executable `.csproj`). Directory allowed: `run_dotnet_test`, `run_specific_test`, `run_test_by_filter`, `run_format`, `list_nuget_packages`, `list_outdated_packages`, `run_nuget_audit`, `get_changed_files`. Optional reload: `list_projects` / `get_project_graph`. Prefer `.sln`/`.slnx` for multi-config solutions.
- `symbolName` — C# identifier for `find_symbol_definition`, `find_symbol_references`, `find_usages`, and `find_implementations` (exact name; solution-wide definition/usages/implementations and `find_symbol_references` without `filePath` are case-insensitive; `filePath` without `line` on definition and references is ordinal case-sensitive).
- `diagnosticId` — compiler diagnostic ID from `get_diagnostics_for_file` (e.g. `CS0246`) for `get_code_fixes` / `apply_code_fix`.
- `fixIndex` — 0-based index from `get_code_fixes` for `apply_code_fix`.
- `path` — `.cs` file or directory for `get_code_skeleton` (absolute path; disk-based, no workspace required).

When a tool accepts `filePath`, relative values are resolved against the loaded workspace root after `load_workspace`; if no workspace is loaded, fallback is `Environment.CurrentDirectory`.

There are **55** registered tools in the default `full` profile (see list below). A `lite` profile starts with **16** core tools; extra groups use `ROSLYN_MCP_TOOL_GROUPS` or `enable_tool_group`. Nine attributed methods stay out of `tools/list` until v2 and are not callable: `remove_using`, `organize_usings`, `add_property_to_class`, `add_field_to_class`, `add_type_to_class_bases`, `remove_member`, `implement_interface`, `extract_interface`, `move_type_to_new_file`.

### Workspace / Roslyn

<details>
<summary><code>load_workspace</code> — Loads .sln/.slnx/.csproj into MSBuildWorkspace.</summary>

**Parameters:**
- `workspacePath: string` — `.sln`, `.slnx`, or `.csproj` file (not a directory). Prefer solution files for multi-config repos.
- `configuration: string?` — optional MSBuild `Configuration` global property (e.g. `Sit-Debug`, `kart`). Inherited by build/test when those tools omit `-c`.
- `platform: string?` — optional MSBuild `Platform` (`Any CPU` → `AnyCPU`). Inherited by build/test as `-p:Platform=`.
- `targetFramework: string?` — optional MSBuild `TargetFramework` (e.g. `net10.0`). Pass when the solution uses `TargetFrameworks` so design-time evaluation is an inner TFM with a `Compile` target. Not inherited by build/test.
- `buildArgs: string?` — optional extra arguments appended to later `dotnet build` (probe and pre-test build). Omitted or whitespace keeps a value already loaded; it does not clear it. `reset_workspace` clears the session, including `buildArgs`. Do not include `-c`, `-p:Platform`, `-v`, or `--no-incremental`.
- `briefOutput: bool = false` — when `true`, collapse successful-load MSBuild/NuGet warnings to category and code counts. Default `false` keeps full messages. Failures always print in full.
- `logProjectOutputDiagnostics: bool = false` — when `true`, logs one Information-level line per project (`OutputFilePath`, exists/last-write UTC, `CompilationOutputInfo.GeneratedFilesOutputDirectory`, exists) and one line per `AnalyzerReference` (`Display`, `FullPath`, exists/last-write UTC) to the MCP server log — not returned in this tool's response. Diagnostic-only aid for `Directory.Build.props` overriding `OutputPath` (e.g. into a shared `artifacts` folder) so an analyzer/generator project's `AnalyzerReference` ends up pointing at a stale or missing DLL. Read with `tail_tool_log` / `read_log_tail`.
- `shadowCopyInSolutionAnalyzers: bool = false` — when `true`, rewrites only `AnalyzerReference`s whose complete load-session provenance exact-joins them to one loaded in-solution project; filename/`AssemblyName` similarity is not evidence. Same-name external, unconfirmed, or ambiguous references remain unchanged. An inaccessible original path is skipped (U-ARB-01 skip; `access_failure`; not missing; not rewritten) — see [the decision](archive/analyzer-shadow-copy-improvements/v2/u-arb-01-inaccessible-skip.md); rewrite/fail-closed remain alternatives if field evidence warrants a change. A missing, Failed, Incomplete, or other-session capture does not publish a semantic snapshot (the MSBuild graph may still open); cached `true` reuses that capture — call `reset_workspace` then `load_workspace`. A failed opt-in stays fail-closed across edit/flush/reconciliation: raw workspace may keep original refs for persistence, but they are not published or executed. Activation is session-sticky: after enablement, a same-key cached `false`/omitted call preserves the active overlay without preparation/refresh. The selected project must already have its resolved output on disk. Prepared generations are content-hashed (`v2-main-only`) and reused on document edit without recopying analyzer files. After rebuilding a generator with the same assembly identity, restart the MCP process — `reset_workspace` does not unload CLR assemblies. Private helper DLLs are refused (main-only). This response includes a short summary (`N rewritten` plus execution status, or an explicit semantic-unavailable note); per-reference detail goes to the MCP server log.

**Analyzer shadow-copy activation and side effects:** this flag is opt-in; the MCP client and agent do not enable it automatically. Add a conditional rule to the target repository's `AGENTS.md` when that repository uses in-solution analyzer/generator projects with a custom `OutputPath`. Enabling it copies versioned analyzer DLL/PDB files under the OS temp `v2-main-only/` namespace and re-applies the in-memory overlay after workspace document updates, adding some disk I/O and CPU. Activation is session-sticky: cached `false`/omitted preserves an already-active overlay and does not refresh analyzer files; use `reset_workspace` then load without the flag to disable. Old generations (including leftover timestamp directories) are not cleaned automatically and can accumulate; `reset_workspace` does not delete them. The real `.csproj` and analyzer build output are not modified; on the supported overlay paths the MCP process locks the shadow copy, not the real output. Path resolution and this anti-lock workaround are independent. `find_symbol_definition` by a generator-emitted type name remains a Roslyn limitation, not a failed overlay.

**Atomic anti-lock boundary (U-ARB-04):** physical load/cache lookup, opt-in prepare, execution gate, and final snapshot publication are one serialized manager operation. Production semantic readers wait for this boundary (`GetPublishedSolutionAsync` / `FindDocumentAsync`) and never compile an intermediate raw analyzer reference. `GetCurrentSolution()` returns only the published snapshot. If opt-in entered first, a concurrent semantic request receives the shadow overlay; prepare failure/cancellation on a new session publishes only a fail-closed snapshot and does not later re-publish confirmed real refs. A semantic operation completed before later enablement remains an honest no-overlay operation and can make the identity gate require a process restart. See the [evidence](archive/analyzer-shadow-copy-improvements/v2/u-arb-04-load-boundary-evidence.md), [decision](archive/analyzer-shadow-copy-improvements/v2/u-arb-04-decision.md), and [implementation specification](archive/analyzer-shadow-copy-improvements/v2/u-arb-04-atomic-load-prepare.md).

**Behavior:** Host abort mid-load returns **Workspace Load Cancelled (client abort)** (raise MCP tool timeout; not an MSBuild failure). After a successful load, **saved** `.cs` files are watched and applied before symbol search (unsaved buffers ignored). A changed `.csproj`/`.sln`/`Directory.Build.props` skips the next `load_workspace` cache. NuGet restore warnings (`NU1701` TFM compat, audit, prune) and design-time MSBuild warnings (ASP.NET/SDK deprecation such as `IncludeOpenAPIAnalyzers`/`ASPDEPR007`, processor-architecture mismatch, analyzer project without metadata) are shown as warnings and do not fail load even when wrapped as `Msbuild failed when processing the file`; true MSBuild/SDK errors (`error NU|MSB|NETSDK`) still do. Empty `TargetFramework` (`ResolvePackageAssets`) is a dedicated failure — retry with the IDE solution config, or the `.sln` is Bazel-generated and not MSBuild-evaluable. Missing `Compile` target (CrossTargeting outer build) is a dedicated failure — retry with `targetFramework` from the report / `Directory.Build.props`; `dotnet build` can still succeed. VS 2026 / MSBuild 18 BuildHost crash (`XMakeElements`) is a dedicated failure — **not** `MCP_MSBUILD_SDK_MISMATCH`; use MCP 1.0.35+ or load a single SDK-style `.csproj`.
</details>

<details>
<summary><code>reset_workspace</code> — Clears the in-memory MSBuildWorkspace/solution cache. Call before <code>load_workspace</code> again after building the loaded solution on disk (generated files / refs). Saved <code>.cs</code> edits sync without reset (v1.1.0+). Does <strong>not</strong> unload CLR analyzer assemblies; after a same-identity generator rebuild, restart the MCP process.</summary>

**Parameters:** *(none)*
</details>

<details>
<summary><code>get_file_content</code> — Reads the entire file from disk (safe preview for large files).</summary>

**Parameters:**
- `filePath: string`
</details>

<details>
<summary><code>get_class_skeleton</code> — Returns the C# file structure without method bodies (workspace index after applying saved <code>.cs</code>).</summary>

**Parameters:**
- `filePath: string`
</details>

<details>
<summary><code>get_code_skeleton</code> — Parses `.cs` from disk and returns full-file syntax with bodies stripped (signatures + empty blocks); optional directory scan (max 20 files, skips <code>bin</code>/<code>obj</code>/<code>Test</code>/<code>Tests</code> path segments).</summary>

**Parameters:**
- `path: string` — absolute path to one `.cs` file or a folder to scan recursively.

**Note:** Does not require `load_workspace`. In `lite`, enable the `files` group (or use `full`) to see this tool. For a file already in the loaded solution, `get_class_skeleton` may still be preferable (workspace-consistent view).
**Important:** This tool is file/folder-only (`path`). Do **not** pass `assemblyName` / `typeName`; for external/NuGet assemblies use `decompile_type` / `get_decompiled_class_skeleton`.
</details>

<details>
<summary><code>get_method_body</code> — Returns the first matching method source from a disk <code>.cs</code> file. No overload selection.</summary>

**Parameters:**
- `filePath: string`
- `className: string`
- `methodName: string`

**Path resolution:** `filePath` may be absolute or workspace-relative. After `load_workspace`, `src/...` is resolved from the loaded `.sln`/`.slnx`/`.csproj` directory.
</details>

<details>
<summary><code>get_diagnostics_for_file</code> — Returns Roslyn compiler diagnostics for a single file (Warning and Error only; saved <code>.cs</code> applied first).</summary>

**Parameters:**
- `filePath: string`

Each compiler diagnostic includes severity, **line**, **column**, an ID such as `CS0246`, and a message. Use the ID and position with `get_code_fixes`.
</details>

<details>
<summary><code>get_code_fixes</code> — Lists Roslyn CodeAction fixes available for a compiler diagnostic at a specific location.</summary>

**Parameters:**
- `filePath: string`
- `diagnosticId: string` — from `get_diagnostics_for_file` (e.g. `CS0246`)
- `line: int` — 1-based line where the diagnostic starts
- `column: int = 1` — 1-based column (from diagnostics output)

**Returns:** numbered fixes (`fixIndex` 0-based) with titles such as “Add using …”, “Implement interface”, etc. Up to 20 fixes per call.

**Workflow:** `get_diagnostics_for_file` → `get_code_fixes` → `apply_code_fix`. Do not invent fix code when Roslyn provides a fix.
</details>

<details>
<summary><code>apply_code_fix</code> — Applies a Roslyn CodeAction selected from <code>get_code_fixes</code>.</summary>

**Parameters:**
- `filePath: string`
- `diagnosticId: string`
- `fixIndex: int` — index from `get_code_fixes` (0-based)
- `line: int` — same as in `get_code_fixes`
- `column: int = 1`
- `previewOnly: bool = false` — when `true`, returns a diff preview without writing files

**Behavior:** writes changed files through the workspace write boundary and updates the in-memory workspace. An unsupported or stale analyzer-reference diff is rejected before any server write. If only some files are saved, the reply is not full success: it includes Status, Reason, and the known saved paths. Re-run `get_diagnostics_for_file` to check for remaining compiler warnings and errors.

**Note:** `fixIndex` is valid only for the same `filePath` / `diagnosticId` / `line` / `column` pair in the same session (file must not change between `get_code_fixes` and `apply_code_fix`).
</details>

<details>
<summary><code>explore_assembly</code> — Decompiles a referenced external assembly (NuGet/third-party DLL) via ILSpy and returns namespaces with visible top-level classes/interfaces.</summary>

**Parameters:**
- `assemblyName: string?` — simple name without `.dll` (resolved via loaded workspace; call `load_workspace` first).
- `assemblyPath: string?` — absolute path to a `.dll` (e.g. NuGet cache). Provide **one** of the two.
</details>

<details>
<summary><code>decompile_type</code> — Decompiles one specific type from a referenced assembly and returns C# source code (circuit breaker: max 500 lines).</summary>

**Parameters:**
- `assemblyName: string?` / `assemblyPath: string?` — same as `explore_assembly` (one required).
- `fullTypeName: string` (e.g. `Microsoft.AspNetCore.Mvc.ControllerBase`)

**Behavior note:** if decompiled output exceeds 500 lines, the tool returns an error message instructing to use `get_decompiled_class_skeleton` and `get_decompiled_method_body`.
</details>

<details>
<summary><code>get_decompiled_class_skeleton</code> — Returns a signatures-only skeleton (public/protected fields/properties/methods) for one type from a referenced assembly.</summary>

**Parameters:**
- `assemblyName: string?` / `assemblyPath: string?` — same as `explore_assembly` (one required).
- `fullTypeName: string` (e.g. `Microsoft.AspNetCore.Mvc.ControllerBase`)
</details>

<details>
<summary><code>get_decompiled_method_body</code> — Decompiles only matching method overload(s) from one type in a referenced assembly.</summary>

**Parameters:**
- `assemblyName: string?` / `assemblyPath: string?` — same as `explore_assembly` (one required).
- `fullTypeName: string` (e.g. `Microsoft.AspNetCore.Mvc.ControllerBase`)
- `methodName: string`
</details>

<details>
<summary><code>find_symbol_references</code> — Finds usages of a type/member across the solution.</summary>

**Parameters:**
- `symbolName: string?` — simple name or exact FQN (`Namespace.Type` / `Namespace.Type.Member`; no `global::`, no `()`); optional with exact `filePath`+`line`+`column`. Solution-wide search is case-insensitive; with `filePath` and no `line`, match is ordinal case-sensitive; required for auto-column when `column` is omitted
- `filePath: string?` — optional `.cs` file; omit for solution-wide name/FQN search; required when `line` is set; without `line`, select the unique matching declaration in that file (error if several)
- `line: int?` — optional 1-based line; requires `filePath`; when set, resolve the symbol at that position (declaration or usage)
- `column: int?` — optional 1-based column; omit to auto-pick the unique matching identifier token on the line
- `maxResults: int?` — listing cap 1–500. Omit → 50, else `ROSLYN_MCP_MAX_RESULTS`, else `RoslynMcp.jsonc` `max-results`; an explicit argument wins
- `preview: bool? = null` — when true, append the source line (truncated at 400 chars). Omit → `RoslynMcp.jsonc` `preview`, else `path:line:col` only
- `overflowCursor: string?` — when set, return the next in-memory overflow chunk (does not start a new search)
- `directOnly: bool = false` — when true (requires `filePath`), for a class virtual/abstract/override method keep only direct-receiver calls; default false returns the same points and may add one virtual-dispatch note. Cross-project source/metadata types match by full name.

**Behavior:** File without line covers class/struct/record/interface/enum/method/ctor/dtor/property/event/field (not operator/indexer/local function). Several matches → error listing FQN and identifier `line:column` (no silent first). Locations beyond `maxResults` are stored in-process behind a cursor (not silent drop; not a host `%Temp%` path). Unknown/expired cursor → human error. Filter (when used) runs after dedupe and before the listing cap / overflow store.
</details>

<details>
<summary><code>find_symbol_definition</code> — Semantic lookup: where a type or member is declared (file, line, column, full name) in the loaded solution.</summary>

**Parameters:**
- `symbolName: string?` — class, interface, struct, enum, or member identifier (e.g. `IRunCommand`); optional with exact `filePath`+`line`+`column`; otherwise required. Solution-wide search is case-insensitive; file without line is ordinal case-sensitive.
- `filePath: string?` — optional; omit for solution-wide name search; without `line`, select the unique matching declaration in that file; required with `line` for positional go-to-definition
- `line: int?` — optional 1-based line in `filePath` (usage or declaration)
- `column: int?` — optional 1-based column; omit to auto-pick the unique matching identifier token on the line
- `maxResults: int?` — listing cap 1–500. Omit → 50, else `ROSLYN_MCP_MAX_RESULTS`, else `RoslynMcp.jsonc` `max-results`; an explicit argument wins
- `overflowCursor: string?` — next in-memory overflow chunk; does not start a new search

**Behavior:** Locations beyond `maxResults` are stored in-process behind a cursor (not silent drop; not a host `%Temp%` path). Unknown/expired cursor → human error. File+line mode uses the same cursor scheme.

**Model guidance:** after `load_workspace`, use this for “where is X **declared**?” — do **not** answer that with plain-text search or invent a generic tool named `search`. For free-text matches across files, use your client’s built-in **`grep`** tool (not `bash`/`PowerShell` grep). This tool avoids `bin/`/`obj/` and uses Roslyn. **Saved** `.cs` (IDE/git) are applied before search; unsaved buffers are ignored — no `reset_workspace` for ordinary saves. Omit `filePath`/`line` for solution-wide name search; pass `filePath` without `line` for the single declaration in that file (prints every in-source location, including partial parts, each with column and full name); several matches → error with FQN and identifier coordinates; pass `filePath`+`line` to go to the definition of the symbol under that position.

Optional `preview: bool?` appends each definition's source line (up to 400 characters), including overflow chunks. The explicit argument overrides `RoslynMcp.jsonc` `preview`; otherwise the default is false. With exact `filePath`+`line`+`column`, `symbolName` may be omitted.
</details>

<details>
<summary><code>find_usages</code> — Solution-wide references for a declared name (<code>path:line:col</code>; optional preview / overflow).</summary>

**Parameters:**
- `symbolName: string` — simple name or exact FQN (same rules as `find_symbol_references` without `filePath`).
- `maxResults: int?` — listing cap 1–500. Omit → 50, else `ROSLYN_MCP_MAX_RESULTS`, else `RoslynMcp.jsonc` `max-results`; an explicit argument wins
- `preview: bool? = null` — when true, append the source line (truncated at 400 chars). Omit → `RoslynMcp.jsonc` `preview`, else false
- `overflowCursor: string?` — next in-memory overflow chunk; does not start a new search

**Behavior:** Requires `load_workspace`. Name-based alias of `find_symbol_references` without `filePath`. Applies saved `.cs` from disk first. All matching declaration groups are returned (no primary pick). FQN miss lists candidates and does not fall back to simple name. Overloads share one FQN group. Excess locations use the same overflow store as `find_symbol_references`.
</details>

<details>
<summary><code>find_implementations</code> — Find classes implementing an interface or derived from a base type.</summary>

**Parameters:**
- `symbolName: string` — interface or base class name. A short name returns a section per matching base. Copy the qualified name from a section to select that base, for example `Demo.IProvider<T>` or `global::IProvider`
- `transitive: bool = true` — when `true`, includes indirect implementations / derived types in the hierarchy
- `maxResults: int?` — listing cap 1–500. Omit → 50, else `ROSLYN_MCP_MAX_RESULTS`, else `RoslynMcp.jsonc` `max-results`; an explicit argument wins
- `preview: bool? = null` — when true, append the source line (truncated at 400 chars). Omit → `RoslynMcp.jsonc` `preview`, else `path:line:col` only
- `overflowCursor: string?` — next in-memory overflow chunk; does not start a new search

**Behavior:** Requires `load_workspace`. Applies saved `.cs` from disk first. For **interfaces**, uses Roslyn `FindImplementationsAsync`; for **classes/structs**, uses `FindDerivedClassesAsync`. A qualified name without type parameters selects the non-generic declaration when both forms exist. It also matches a single generic declaration by its older name without type parameters. A qualified-name miss lists candidates. Empty sections are kept. Each section lists its own related types with `path:line:col`, and the summary counts distinct types across all sections. Excess lines use the same overflow store. Do not use text search or `find_usages` for “who implements X?” / “what inherits from Y?”.

**Model guidance:** after `load_workspace`, use this instead of grep or analyzing usages when you need the OOP hierarchy.
</details>

<details>
<summary><code>get_call_graph</code> — Callers and callees for a method (workspace; saved <code>.cs</code> applied first).</summary>

**Parameters:**
- `filePath: string` — `.cs` file containing the method
- `className: string`
- `methodName: string`
- `maxNodes: int = 25` — cap per callers/callees list
- `includeExternalCallees: bool = false` — include BCL / external calls
- `line: int?` — optional 1-based line; must be passed together with `column`
- `column: int?` — optional 1-based column; must be passed together with `line`

**Behavior:** Requires `load_workspace`. Uses `SymbolFinder.FindCallersAsync` and invocation analysis inside the method body. Without position, several same-named methods in the class is an error (FQN + line:column), not the first match. With `line`+`column`, resolves a declaration or call site. Use for bug investigation instead of loading many bodies via `get_method_body`.

</details>

### File Editing

<details>
<summary><code>update_file_content</code> — Completely overwrites a file. Creates missing directories automatically. A new <code>.cs</code> is written to disk and marks the project graph composition-stale; it is not added to the semantic snapshot until `load_workspace` (a stale graph skips the load cache) or `reset_workspace` then `load_workspace`.</summary>

**Parameters:**
- `filePath: string`
- `content: string`
</details>

<details>
<summary><code>add_using</code> — Add a using directive via Roslyn AST.</summary>

**Parameters:**
- `filePath: string`
- `namespaceName: string` — e.g. `System.Linq`

Inserts and formats the directive. Requires `load_workspace`. Prefer over `apply_patch` for imports.

</details>

<details>
<summary><code>add_method_to_class</code> — Insert a method into a class via Roslyn AST.</summary>

**Parameters:**
- `filePath: string`
- `className: string` — top-level class name
- `methodSource: string` — full method declaration (modifiers, signature, body)

Parses C# syntax, inserts with DocumentEditor, formats the file. Prefer over `apply_patch` for new methods.

</details>

<details>
<summary><code>update_method_body</code> — Replace a method body via Roslyn AST.</summary>

**Parameters:**
- `filePath: string` — absolute path to `.cs` file
- `className: string` — class containing the method (top-level or nested)
- `methodName: string`
- `newBody: string` — statements only, or a full `{ ... }` block
- `parameterTypes: string[]?` — the signature must match when supplied, e.g. `["string", "int"]`. Use `[]` for a parameterless method. Omit only when the method name is unique.

**Behavior:** Finds `MethodDeclarationSyntax`, parses `newBody` into a `BlockSyntax`, replaces block or expression-bodied body, validates syntax errors before write, formats the file. Use with `get_method_body` — prefer over `apply_patch` for body-only edits.

</details>

<details>
<summary><code>apply_patch</code> — Surgical find-and-replace tool.</summary>

**Parameters:**
- `filePath: string`
- `oldString: string`
- `newString: string`
- `replaceAll: bool = false`

**Behavior:** `replaceAll` continues the search after each inserted `newString` (does not rescan the replacement). Safe when `newString` contains `oldString`. Logs start/match/write with elapsed ms.
</details>

### Build / Test / CLI

<details>
<summary><code>estimate_affected_tests</code> — Approximate test selection since the solution was loaded.</summary>

If the source baseline is still being captured, the tool reports pending documents. Retry the call after capture completes. With `run: true`, the pending response starts no builds or tests. Documents with an unknown baseline remain eligible for conservative selection and appear separately under `Not covered`. The response is limited to 32 KiB of UTF-8 text.

Requires `load_workspace`. The tool synchronizes saved disk and MCP edits and reports changes since the session baseline. `depth` (case-insensitive) chooses a cumulative stage: `changed` (changed or new test classes), `direct` (also type references), `di` (also container and framework discovery), or `all` (also assembly loads by name, the default). For a narrowed project the answer shows class counts for all four stages and the VSTest filter at the selected depth. `includeDetails: true` lists the classes by stage and the changed compiled types. The answer also reports compiler errors in changed source documents and specific `Not covered` limitations. Use the build output for compilation-wide errors such as entry point conflicts.

Use `run_specific_test` when you know the class or method. When the impact of a saved edit is broad or unclear, call `estimate_affected_tests` with `run: true` to build and test the selection in one call. Use `run: false` to inspect the plan or reuse a fresh build. With fresh outputs, run the returned filters via `run_test_by_filter` and whole projects via `run_dotnet_test` with `noBuild: true`. On full Jellyfin, the automatic parallel run saved 13.85–18.10 seconds over `maxParallelProjects: 1` in two paired edits with identical tests and outcomes. [Full-cycle benchmark](rapid-tia-2/mvp/evidence/parallel-run-2026-11-19.md).

The initial selection uses saved inputs and project dependencies. Narrowing uses compiled type fingerprints and dependency graphs from before and after the edits. The baseline survives reload or reset of the same solution. A different solution or server restart takes a new baseline. When every stage is empty, the answer distinguishes unchanged compiled code from changed types that no test class reaches.

When the initial plan selects the whole project, the answer keeps that scope and explains why if the pre-edit snapshot is missing, an assembly was overwritten before its first read could finish, an assembly cannot be read, an output is no newer than the latest edit, or the test output is stale after a dependency changed shape. It also keeps the whole project for non-C# or project inputs, a stale project graph, observation gaps, unmappable VSTest class names, or a filter over the length limit. In estimate-only mode, stale outputs prompt a build and another estimate.

The graph can miss types named in strings or configuration and dependencies reached in another process started by a test. Narrowing only visits test projects already selected by the initial project-level plan. It reads the outputs for the target frameworks in the loaded workspace, so an edit that only affects another framework can appear as unchanged compiled code.

`run` defaults to `false`. With `run: true`, the tool builds the selected projects sequentially using configuration, platform and build arguments from `load_workspace`. It narrows against the fresh outputs and then starts VSTest without rebuilding. `maxParallelProjects: 0` (default) allows half the logical processors rounded down, with a minimum of one and a cap at the number of projects with tests to run. A value greater than one sets the project limit. Use `1` to build, narrow and test each project in turn if projects share a port, file or database. Each test process uses a separate temporary results directory that is removed after processing. The response lists projects in path order and reports the test concurrency.

An empty selection skips the test process and reports `not run`. If narrowing fails after a successful build, the project runs whole and the answer names the error. A comment-only edit is checked only with `run: true`: the estimate keeps the syntax comparison, while the run compares the compiled code. The changed-type count of a run covers the projects built in that run. `timeoutSeconds` (default 300, 0 disables it) applies separately to each build or test process. A failure or timeout leaves other projects eligible to run. MCP cancellation terminates active process trees, skips queued projects and logs the outcomes.

**Build outputs:** changes are detected in authored inputs (sources, project files, evaluation inputs, data files and assemblies kept in the repository), not in build products. Rebuilding a project, including the build inside `run`, `run_*` or the IDE, does not widen the next estimate, and reverting an edit can return an empty plan. A project that references another solution project's output as an assembly file (a metadata reference), outside the project graph, is still selected when that output changes; a project that loads it only as an analyzer is not.

Filters use name containment, so selecting `Check` may also run `CheckExtra` and all parameter rows. Nested/inherited/generic runtime names use the measured VSTest forms; unknown forms and NUnit fixture constructor/source arguments select the whole project. MTP projects are not recognized separately and follow the existing VSTest path; a dedicated MTP plan is deferred. Reflection, conventions, external data/processes, source generators and tests reading source files can escape this approximation; input watching and syntactic test-role recognition have limits. Run project-mandated checks (such as `SourceStructure`) independently. The full test suite and CI remain required before accepting or committing a change.
</details>

<details>
<summary><code>run_dotnet_build</code> — Runs dotnet build and returns a compact diagnostic summary.</summary>

**Parameters:**
- `workspacePath: string` — must be an existing **`.csproj`, `.sln`, or `.slnx` file** (not a directory).
- `configuration: string? = null` — optional `-p:Configuration=` (e.g. `Sit-Debug`, `Dit-Debug`). Omit to inherit `load_workspace` configuration.
- `noIncremental: bool = true` — pass `--no-incremental` on every build step (default). Set `false` only if you explicitly accept MSBuild up-to-date caching.
- `platform: string? = null` — optional `-p:Platform=` (e.g. `x64`). Omit to inherit `load_workspace` platform.
- `projectName: string? = null` — optional. When set, `workspacePath` must be a `.sln`/`.slnx`. Builds that project via its solution-folder MSBuild target (`-t:"Folder\Project"`). Match display name, file name, or virtual path.
- `timeoutSeconds: int? = null` — omit for 180 seconds per step and 300 seconds total. An explicit value from 1 to 4,294,967 sets one budget for all build and restore steps. `0` disables the tool timeout. Client cancellation and the client's `tools/call` timeout still apply.
- `reportCursor: string? = null` — next chunk of a retained full report. Does not start a new build.
- `includeBuildWarnings: bool = false` — include build warnings; default hides them. Errors are always shown.

**Behavior:** Uses the SDK selected by `dotnet` for the working directory, including `global.json` roll-forward. Clears inherited MSBuild SDK overrides and applies the verified SDK path when available. An observed MSBuild path that differs from the resolved SDK can produce `MCP_MSBUILD_SDK_MISMATCH` and a restore retry with the resolved `MSBuild.dll`. The diagnostic context shows both requested and resolved SDK versions.

Escalation: minimal build → resolved SDK restore → restore (detailed if empty) → build normal → build detailed. The effective exit code comes from the final build step. On failure, `Execution context` reports `Configuration`, `Platform`, `ProjectName`, `SolutionTarget`, `BuildArgs`, and `NoIncremental`. Session `buildArgs` from `load_workspace` apply to build steps. **Key lines** include task `-- FAILED` with project context. The server runs the build for each request. MCP progress heartbeats report the active step, elapsed time, and previous exit code. The client timeout still applies.

**Output:** Successful builds return only `Build succeeded` plus opted-in warnings. Test results and failed-test stdout/stderr are preserved. `Execution context` appears only on failure, timeout, or an unverified test result. Full build failure logs are available through `reportCursor`, including hidden warnings.

</details>

<details>
<summary><code>run_dotnet_test</code> — Runs dotnet test with condensed failure details.</summary>

**Parameters:**
- `workspacePath: string`
- `timeoutSeconds: int = 300` — process timeout; `0` disables (not recommended). Raise for long integration tests (e.g. 900/1800).
- `noBuild: bool? = null` — omit means false on the non-DLL route (`--no-build` only when true). With `binariesPath`, `false` means `always` and `true` means `never`; do not pass this together with `buildPolicy`.
- `buildPolicy: string? = null` — `auto`, `always`, or `never`. Requires `binariesPath`. Omit both this and `noBuild` for `auto`.
- `noRestore: bool = false` — pass `--no-restore`.
- `configuration: string? = null` — optional `dotnet test -c` (e.g. `Sit-Debug`). Omit to inherit `load_workspace` (use the same value as `run_dotnet_build` when `noBuild=true`).
- `platform: string? = null` — optional `-p:Platform=`. Omit to inherit `load_workspace`.
- `binariesPath: string? = null` — optional bin directory containing `{AssemblyName}.dll`. Requires a loaded `.sln`/`.slnx` and a `.csproj` `workspacePath`. When `noBuild=false`, builds that project via the loaded solution `-t` first, then `dotnet test` on the DLL. When `noBuild=true`, the DLL must already exist in that directory.
- `includeFullOutput: bool = false` — when `true`, include failed-test Standard Output/Error up to 100000 chars per stream. Default `false` uses `maxOutputChars`.
- `maxOutputChars: int = 0` — per-failure Standard Output budget. `0` = 2500 (head 1600 + tail 700), or 100000 when `includeFullOutput` is true. Capped at 100000. StdErr scales with this.
- `reportCursor: string? = null` — next chunk of a retained full report. Does not start a new build or test.
- `includeBuildWarnings: bool = false` — include build warnings; default hides them. Errors are always shown.

**Behavior:** When `noBuild=false` and `binariesPath` is omitted, runs incremental `dotnet build` first (same `-c` / platform / session `buildArgs` from `load_workspace`; not the `run_dotnet_build` probe), then `dotnet test --no-build --no-restore`. With `binariesPath`, the compile is `dotnet build <sln> -t` and the test target is the DLL. Parser sees only the test process. `--logger "console;verbosity=normal"`. Summary from `Passed!`, `Test Run Successful` + `Total tests`/`Passed:` (Failed defaults to 0), or `.slnx` fail-only `Total tests` + `Failed:` (Passed inferred as Total − Failed − Skipped), or per-test `  Passed FQN [ms]` / `[1 s]` / `[1 m 28 s]`. Failed-test `Error Message:` is reported multiline (head+tail); Standard Output/Error are separate `StdOut`/`StdErr` blocks (default 2500/1000 chars, head+tail; `includeFullOutput` / `maxOutputChars` raise the cap). The VSTest `Build FAILED` / `0 Error(s)` footer is ignored as a compile result. MSBuild/prune noise ignored; duplicate NU audit lines deduped. Exit 0 without any summary marker → **`Status: partial`** + last 2KB (footer stripped). `run_specific_test` checks the filter matched a test (FQN, method-only xUnit display names, Theory `FQN(args)` Passed/Failed lines). `timeoutSeconds` is the combined budget for build+test. A DLL test also needs `.runtimeconfig.json` / `.deps.json` beside the assembly. While a step runs, MCP progress heartbeats report `dotnet build` / `dotnet test` (no stdout); this is UX only and cannot extend the host `tools/call` timeout.

**Output:** Successful builds return only `Build succeeded` plus opted-in warnings. Test results and failed-test stdout/stderr are preserved. `Execution context` appears only on failure, timeout, or an unverified test result. Full build failure logs are available through `reportCursor`, including hidden warnings.

</details>

<details>
<summary><code>run_specific_test</code> — Run dotnet test filtered to one class and/or method.</summary>

**Parameters:**
- `workspacePath: string`
- `className: string?` — e.g. `UserServiceTests` (simple or fully qualified)
- `methodName: string?` — e.g. `CreateUser_WhenValid_ReturnsOk`
- `timeoutSeconds: int = 300` — same as `run_dotnet_test`.
- `noBuild: bool? = null` — omit means false. With `binariesPath`, `false` means `always` and `true` means `never`; do not pass this together with `buildPolicy`.
- `buildPolicy: string? = null` — same DLL-route policy as `run_dotnet_test`. Requires `binariesPath`.
- `noRestore: bool = false` — pass `--no-restore`.
- `configuration: string? = null` — optional `dotnet test -c` (same as `run_dotnet_test`).
- `platform: string? = null` — optional `-p:Platform=`. Omit to inherit `load_workspace`.
- `binariesPath: string? = null` — same as `run_dotnet_test`: bin directory with `{AssemblyName}.dll`; loaded `.sln`/`.slnx` plus `.csproj` `workspacePath`. `noBuild=false` builds via the solution `-t` first.
- `includeFullOutput: bool = false` — same as `run_dotnet_test`.
- `maxOutputChars: int = 0` — same as `run_dotnet_test`.
- `reportCursor: string? = null` — next chunk of a retained full report. Does not start a new build or test.
- `includeBuildWarnings: bool = false` — include build warnings; default hides them. Errors are always shown.

At least one of `className` or `methodName` is required. The tool builds a VSTest-safe `--filter` internally (`FullyQualifiedName~…`, no method `()`, no extra leading `.` on dotted names). After `load_workspace`, Roslyn resolves the type/method FQN when possible. A pass is recognized when VSTest prints only the method name or `Method(args)` — that is not **no matching tests**.

**Model guidance:** use this for TDD red/green loops — do not run the full suite. For a raw VSTest `--filter` (`TestCategory=Smoke`, `FullyQualifiedName~…`) use `run_test_by_filter`. Prefer `className` + short `methodName`. After build, prefer `noBuild=true` for faster filtered re-runs. Same pre-test build split as `run_dotnet_test` when `noBuild=false`. Projects that must be built inside their `.sln` should pass `binariesPath` and a `.csproj` `workspacePath`.

**Output:** Successful builds return only `Build succeeded` plus opted-in warnings. Test results and failed-test stdout/stderr are preserved. `Execution context` appears only on failure, timeout, or an unverified test result. Full build failure logs are available through `reportCursor`, including hidden warnings.

</details>

<details>
<summary><code>run_test_by_filter</code> — Run dotnet test with a raw VSTest <code>--filter</code>.</summary>

**Parameters:**
- `workspacePath: string` — `.csproj`, `.sln`, `.slnx`, or test project directory (same as `run_dotnet_test`).
- `filter: string` — passed through as `--filter` (e.g. `FullyQualifiedName~MyClass`, `FullyQualifiedName~CreateUser`, `TestCategory=Smoke`). Empty is an error. Do not put method `()`.
- `timeoutSeconds: int = 300` — same as `run_dotnet_test`.
- `noBuild: bool? = null` — omit means true on the non-DLL route (default **true**, unlike the other test tools). With `binariesPath`, `false` means `always` and `true` means `never`; do not pass this together with `buildPolicy`.
- `buildPolicy: string? = null` — same DLL-route policy as `run_dotnet_test`. Requires `binariesPath`.
- `noRestore: bool = false` — `--no-restore`.
- `configuration: string? = null` — optional `dotnet test -c`. Omit to inherit `load_workspace`.
- `platform: string? = null` — optional `-p:Platform=`. Omit to inherit `load_workspace`.
- `binariesPath: string? = null` — optional bin directory containing the test DLL. Requires a loaded `.sln`/`.slnx` and a `.csproj` `workspacePath`. Runs `{AssemblyName}.dll` from that directory. When `noBuild=false`, builds the project via the loaded solution `-t` first; when `noBuild=true`, the DLL must already exist.
- `includeFullOutput: bool = false` — same as `run_dotnet_test`.
- `maxOutputChars: int = 0` — same as `run_dotnet_test`.
- `reportCursor: string? = null` — next chunk of a retained full report. Does not start a new build or test.
- `includeBuildWarnings: bool = false` — include build warnings; default hides them. Errors are always shown.

**Behavior:** Same VSTest parser and pre-test build split as `run_dotnet_test` when `binariesPath` is omitted and `noBuild=false`. Does **not** check that the filter needle appears in a test FQN (category filters would false-positive). Prefer `run_specific_test` for one class or method.

**Output:** Successful builds return only `Build succeeded` plus opted-in warnings. Test results and failed-test stdout/stderr are preserved. `Execution context` appears only on failure, timeout, or an unverified test result. Full build failure logs are available through `reportCursor`, including hidden warnings.

</details>

<details>
<summary><code>get_test_list</code> — List test methods in loaded solution (JSON; saved <code>.cs</code> applied first).</summary>

**Parameters:**
- `maxResults: int = 200` (clamped 1–500)
- `projectName: string? = null` — optional. Limit discovery to one loaded Roslyn project (case-insensitive exact match on display name, file name without extension, or assembly name). Missing or ambiguous names return the project list, not empty JSON.
- `nameContains: string? = null` — optional. Case-insensitive substring of the VSTest FQN (`Namespace.Class.Method`), class name, or method name.

**Behavior:** Detects Fact/Theory/TestMethod/etc. Requires `load_workspace`. Applies saved `.cs` from disk first. Filters run **before** `maxResults`. Each item includes `projectName`. Unfiltered `count: 0` includes agent guidance when the wrong `.csproj` is loaded; a filtered `count: 0` means no match (drop or relax filters first).

</details>

<details>
<summary><code>generate_test_method_stub</code> — Insert a test method stub into a test class.</summary>

**Parameters:** `filePath`, `className`, `methodName`, `testFramework: string?` — `xunit` (default), `nunit`, `mstest`

</details>

<details>
<summary><code>list_nuget_packages</code> — Installed NuGet packages as JSON per project.</summary>

**Parameters:**
- `workspacePath: string` — `.sln`, `.slnx`, `.csproj`, or a directory
- `includeTransitive: bool` — default `true`
- `includeOutdated: bool` — default `false` (adds `--outdated`)
- `includeVulnerable: bool` — default `false` (adds `--vulnerable`)

**Model guidance:** inspect dependency tree before editing `.csproj`; do not use `execute_dotnet_command` for package listing.

</details>

<details>
<summary><code>list_outdated_packages</code> — Outdated NuGet packages as JSON.</summary>

**Parameters:** `workspacePath: string` — shortcut for `list_nuget_packages` with `includeOutdated=true`, no transitive.

</details>

<details>
<summary><code>run_nuget_audit</code> — NuGet vulnerability audit (<code>dotnet list package --vulnerable --include-transitive</code>). Separate from compile errors in <code>run_dotnet_build</code>.</summary>

**Parameters:**
- `workspacePath: string` — `.sln`, `.slnx`, `.csproj`, or a directory
- `maxEntries: int = 40` — maximum vulnerable rows in the table

</details>

<details>
<summary><code>add_package_reference</code> — Add PackageReference to .csproj.</summary>

**Parameters:** `projectPath: string`, `packageId: string`, `version: string?`

Verify id/version with `search_nuget_registry` first. Clears workspace cache — call `load_workspace` after.

</details>

<details>
<summary><code>remove_package_reference</code> — Remove PackageReference from .csproj.</summary>

**Parameters:** `projectPath: string`, `packageId: string`

</details>

<details>
<summary><code>search_nuget_registry</code> — Search NuGet feeds for package id and latest stable version.</summary>

**Parameters:**
- `query: string` — package id or search term
- `exactMatch: bool` — default `true` (exact id lookup)
- `maxResults: int` — when `exactMatch=false`, default 10

**Model guidance:** verify package exists before `dotnet add package`; never hallucinate package names or versions.

</details>

<details>
<summary><code>run_dotnet_run</code> — Build and run an executable project.</summary>

**Parameters:** `workspacePath` (.csproj), `arguments`, `workingDirectory`, `timeoutSeconds=120`, `maxStdoutChars=8000`, `maxStderrChars=2000`, `reportCursor`, `includeBuildWarnings=false`.

**Behavior:** Builds first, then runs `dotnet run --no-build --project` within one timeout. Build warnings are hidden by default; opted-in warnings are deduplicated and capped. Application stdout/stderr are preserved under their existing excerpt limits. Errors, timeouts, and process failures include `Execution context`; successful runs omit it. A failed build prevents application startup. `reportCursor` reads the retained full log without restarting either process.

</details>

<details>
<summary><code>execute_dotnet_command</code> — Runs dotnet {command} in the target directory.</summary>

**Parameters:**
- `command: string` (e.g., `test`, `add package Moq`)
- `workingDirectory: string?` (defaults to current directory)
- `timeoutSeconds: int = 300` — process timeout; `0` disables
- `includeBuildWarnings: bool = false` — include build warnings; default hides them. Errors are always shown.
- `reportCursor: string? = null` — next chunk of a retained build report. Does not rerun the command.

**Build output:** For an explicit `build` command, the same silent mode and failure-only execution context apply. Other commands preserve raw stdout/stderr.

</details>

<details>
<summary><code>get_changed_files</code> — Lists git changed and untracked paths. Does not return a diff.</summary>

**Parameters:**
- `workspacePath: string? = null` — `.sln`, `.slnx`, `.csproj`, or a repo directory. Omit to use the loaded workspace, then the process directory.

Runs `git` with a 180s deadline and up to 3s to stop the process. Use host `git diff` for the patch body.

</details>

<details>
<summary><code>run_format</code> — Runs dotnet format (apply or verify-only). Applied <code>.cs</code> are picked up by the next symbol search without reset.</summary>

**Parameters:**
- `workspacePath: string`
- `verifyOnly: bool = false`
</details>

### Filesystem Utility & Search

<details>
<summary><code>list_directory_tree</code> — Builds a directory tree visualization (ignores bin, obj, .git, .vs).</summary>

**Parameters:**
- `directoryPath: string`
- `maxDepth: int = 2`
</details>

<details>
<summary><code>search_code</code> — Text search (managed file walk by default; optional ripgrep).</summary>

**Parameters:**
- `pattern: string`
- `directoryPath: string? = null`
- `includeExtensions: string? = ".cs"` — comma/semicolon list (`.cs,.csproj,.json`), or `*` for all files.
- `useRegex: bool = false` — .NET regex for the managed walk; ripgrep (Rust) syntax when `useRipgrep` is true.
- `caseSensitive: bool = false` — default case-insensitive; for leftover branding checks set `true`.
- `maxResults: int? = null` — omit → 50, else `ROSLYN_MCP_MAX_RESULTS`, else `RoslynMcp.jsonc` `max-results`; an explicit argument wins
- `maxScanSeconds: int = 20`
- `useRipgrep: bool = false` — when true, runs `rg` only (no managed-walk fallback). Missing `rg` is an error only with this flag.
- `ripgrepPath: string? = null` — full path to `rg`/`rg.exe`; used only with `useRipgrep=true` (else ignored). Omit to use `RoslynMcp.jsonc` `ripgrep-path` or PATH.

**Agent note:** if your client exposes a built-in **`grep`** tool, prefer that for ad-hoc text search (never shell-driven grep). Use this MCP tool when you need search inside the workspace from the Roslyn MCP process. Do not assume ripgrep is installed.
</details>

<details>
<summary><code>read_file_range</code> — Reads specific lines and prefixes them with original line numbers.</summary>

**Parameters:**
- `filePath: string`
- `startLine: int` (1-based)
- `lineCount: int`
</details>

<details>
<summary><code>read_log_tail</code> — Context-safe log reader with optional keyword filtering.</summary>

**Parameters:**
- `filePath: string? = null` — omit to read the latest MCP server log (`logs/mcp-*.log`, same as `tail_tool_log`).
- `lastNLines: int = 200`
- `filterKeyword: string? = null` — e.g. `Failure`, `ERR`, `NU1903`.

**Tip:** Prefer `tail_tool_log` for MCP diagnostics; use `filterKeyword` to avoid dumping entire logs.
</details>

<details>
<summary><code>tail_tool_log</code> — Reads the latest server log `logs/mcp-*.log`.</summary>

**Parameters:**
- `lastNLines: int = 200`
- `filterKeyword: string? = null`
</details>

<details>
<summary><code>manage_agent_scratchpad</code> — Manages the agent's long-term memory across sessions.</summary>

**Parameters:**
- `action: string` (`read` | `write` | `append` | `clear`)
- `content: string? = null`
</details>

### Roslyn Refactoring / Solution Insights

<details>
<summary><code>rename_symbol</code> — Semantic C# symbol rename via Roslyn with preview capability.</summary>

**Parameters:**
- `filePath: string`
- `symbolName: string`
- `newName: string`
- `scope: string = "project"` — allowed: `project` | `solution`
- `previewOnly: bool = true`
- `line: int?` — optional 1-based line; must be passed together with `column`
- `column: int?` — optional 1-based column; must be passed together with `line`

**Scope:** C# symbols only (types/members/namespaces as symbols). For project folder / `.csproj` / solution graph use `rename_project`. For README/rules/URLs use host Grep/edit.

**Behavior:** Without position, several same-named declarations in the file is an error (FQN + line:column) and nothing is written. With `line`+`column`, resolves a declaration or usage in that file. Persist goes through the same write boundary as `apply_code_fix`. Unsupported analyzer-reference diffs are rejected before writes. Partial save reports Status, Reason, and known saved paths — not full success of the rename. Preview remains the default.
</details>

<details>
<summary><code>rename_project</code> — Rename SDK-style project directory + `.csproj` and fix MSBuild graph.</summary>

**Parameters:**
- `projectPath: string` — path to `.csproj`
- `newProjectName: string` — single path segment (e.g. `DupFinder.Core`)
- `dryRun: bool = true`
- `searchRoot: string? = null` — where to find sibling projects / `.sln` / `.slnx`

**Behavior:** Moves identically named project folder + renames `.csproj`; updates `AssemblyName`/`RootNamespace` only when they equal the old project name; rewrites `ProjectReference` paths; updates `.sln` and `.slnx` entries. **SDK-style only.** Unsupported layouts hard-fail (no silent partial write). Does **not** rename C# namespaces/types, Docker, launchSettings, CI, or docs. After apply: `load_workspace` → `run_dotnet_build`.
</details>

<details>
<summary><code>list_projects</code> — Shows projects in the workspace (Name, TFM, Output, Refs).</summary>

**Behavior:** Reports the evaluated target framework and output type of each loaded project instance, including imported and conditional settings. An explicit `workspacePath` returns the projects from that load. A property whose evaluated value is unavailable is shown as `(unknown)`, including output type when the workspace uses a legacy BuildHost that does not report it.

**Parameters:**
- `workspacePath: string? = null`
</details>

<details>
<summary><code>get_project_graph</code> — Builds a project-to-project dependency graph.</summary>

**Parameters:**
- `workspacePath: string? = null`
</details>

### Server lifecycle

<details>
<summary><code>get_mcp_server_info</code> — Binary path, tool count, logs, workspace state.</summary>

**Parameters:** *(none)*

Use after `dotnet publish` to verify the MCP host picked up the new binary. Expect the `Version` from `RoslynMcpServer.csproj`, **55** tools on `full`, or **16** on `lite`.
The report includes the server process working set and the number of successful physical workspace loads since startup. The count persists across cache hits and `reset_workspace`.

</details>

<details>
<summary><code>list_tool_groups</code> — Lists groups with purpose, active state, and member names.</summary>

**Parameters:** *(none)*

Does not enable groups. Use `enable_tool_group` or restart with `ROSLYN_MCP_TOOL_GROUPS`. Markdown output; `tools/list` remains JSON Schema.

</details>

<details>
<summary><code>get_tool_help</code> — Markdown help for one public tool (kind, group, live parameters).</summary>

**Parameters:**
- `toolName: string` — exact public tool name (e.g. `find_usages`). Unknown names return close matches.

</details>

<details>
<summary><code>enable_tool_group</code> — Adds every missing tool in one catalog group to this session.</summary>

**Parameters:**
- `group: string` — exact group name, case-insensitive (`core`, `files`, `navigation`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations`).

Idempotent. Profile `full` is a no-op. Sends `tools/list_changed` only when tools were added. Clients that ignore the notification: restart with `ROSLYN_MCP_TOOL_GROUPS=<group>`. No disable in this release.

</details>

<details>
<summary><code>stop_mcp_server</code> — Stops this MCP host process after returning (for rebuilding the server binary; restart MCP in the IDE). Not for refreshing saved source — that syncs automatically.</summary>

**Parameters:** *(none)*

</details>
