# Changelog

Tracks MCP tools relevant to [`AGENTS.md.sample`](AGENTS.md.sample) (copy into app repos as `AGENTS.md`). Current server version: see `RoslynMcpServer.csproj`.

### v1.5.5

- Build/test/run tools accept `includeBuildWarnings=false`: successful builds return a short status, errors remain visible, and warnings are opt-in. Build failure reports keep full logs accessible via `reportCursor`. The raw command tool applies this policy only to an explicit `build` command.
- SDK/environment metadata is shown under `Execution context` only on failure, timeout, or an unverified test result. `run_dotnet_run` separates build from application startup (`--no-build`) within one timeout so application stdout/stderr are preserved.

### v1.5.4

- **Saved additional files and analyzer configs reach the semantic snapshot** — a path that is already an `AdditionalDocument` or `AnalyzerConfigDocument`, and is not also an evaluation input, is read once on the same flush as `.cs` and applied with `WithAdditionalDocumentText` or `WithAnalyzerConfigDocumentText`. The text is reapplied onto the published snapshot. `TryApplyChanges` is not used: it rewrites an additional file and throws for an analyzer config, and neither result updates the published snapshot.
- One read is applied to every project document of that path. A later `.cs` edit keeps the new text. An own write of the same path updates the snapshot as well. XAML, resx, and Razor stay recorded by role and are not text-synced. A new or deleted non-C# file does not add or remove a document. Standing coverage gaps do not block a document the graph already contains, and this replace does not clear them.

### v1.5.3

- **DLL tests share one solution-target check** — `run_dotnet_test`, `run_specific_test`, and `run_test_by_filter` take `buildPolicy` (`auto`, `always`, `never`) when `binariesPath` is set. Omit both `buildPolicy` and `noBuild` and the DLL route uses `auto`. `noBuild` is optional: on that route `false` means `always` and `true` means `never`; passing both parameters is an error. `buildPolicy` without `binariesPath` is an error. Without `binariesPath`, omitting `noBuild` still means rebuild for the first two tools and skip for `run_test_by_filter`.
- `auto` and `always` compile with `dotnet build <loaded.sln> -t`, then `dotnet test` the DLL with `--no-build`. `never` starts tests only when the session can prove that DLL is current; otherwise it returns an error and does not start them. The pull does not carry input content hashes yet, so `auto` still builds on a second call. If inputs change after the build and before tests start, one rebuild runs inside the same `timeoutSeconds`. A second miss does not start tests. Two DLL calls with the same project and build context wait on each other inside this process; that wait does not stop a build outside it.

### v1.5.2

- **Project inputs outside the solution directory** — the disk watcher follows the loaded graph. Project directories stay recursive. A linked file, and a walk-up `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, or `global.json` outside those directories, is a non-recursive watch on that file's own directory. `search_code` roots stay the loaded workspace directory plus project directories.
- A saved user `.cs` is applied to every project document of that path. A new `.cs` under a project directory still marks the graph composition-stale. The watcher does not add a document and does not write `<Compile Include>` into the project file. Additional files, analyzer configs, and other non-C# inputs are recorded by role and are not copied into the semantic snapshot.
- Generated outputs of the build that produced them are not input edits. An own write is recognized by the bytes that were saved, including the BOM. A matching echo does not add another revision. An external edit that does not match those bytes stays dirty, including one that arrives immediately. A semantic flush does not clear these revisions. Watcher overflow, a failed watch, a directory rename inside a project directory, or an unconfirmed other target framework leaves coverage unknown until the graph is evaluated again. No callback is not proof that nothing changed.

### v1.5.1

- **WPF temporary projects do not drop the workspace cache** — a markup compile creates and deletes `{ProjectName}_{random}_wpftmp.csproj` next to the real project. The file is still a project graph file, and the watcher ignores it only when that path is not one of the loaded projects. A real `.csproj`, solution, or `Directory.Build.*` change during the build still marks the graph stale. Watcher overflow is unchanged: it asks for a full document re-read and can mark composition stale on the next flush.

### v1.5.0

- **MCP C# SDK 2.2.0** — the host package moved from 1.3.0. `enable_tool_group` still sends one `tools/list_changed` when the set grows; the batch uses the SDK `DeferChangedEvents` scope. Clients on the `2025-11-25` initialize handshake receive that notification on the session. Clients on `2026-07-28` (the SDK 2.2 default, which this server accepts) receive it only after `subscriptions/listen` with `toolsListChanged`. No separate protocol feature is added on top of the SDK.
- **Plugins** — a plugin's `ModelContextProtocol` reference must be `Major.Minor` 2.2. A plugin built against 1.3 is skipped at discovery. Patch equality is still not required.
- **Catalog size** — full 54 tools / 43,029 bytes; lite 15 / 16,906. Counts are unchanged. Each tool's JSON is 39 bytes shorter because SDK 2.2 no longer writes the core `execution.taskSupport` field (Tasks left the core package; this server does not reference that extension).

### v1.4.19

- **Disk watcher** — an error or a directory rename from `FileSystemWatcher` still asks the next semantic call to re-read known documents when the logger throws. `Logger.Log` rethrows a provider failure (Windows Event Log does this after its handle is disposed), and that callback runs on a thread-pool thread, so the exception used to kill the process. The refresh is requested before the log write.

### v1.4.18

- **Plugins** — the server loads plugin assemblies before the host is built, so the first `tools/list` already contains their tools. Sources: `{BaseDirectory}/plugins/<id>/` drop-in directories, the `plugins` key of `RoslynMcp.jsonc`, and `ROSLYN_MCP_PLUGINS` (a directory or an entry DLL path; a development directory is copied to a temporary directory of this run before it is loaded). The manifest `id` must equal `IRoslynMcpPlugin.Name`, every tool name must start with the manifest `toolPrefix`, and a plugin compiled against another `ModelContextProtocol` Minor, a newer `RoslynMcpServer`, or another Roslyn major is refused before its DLL is opened. A plugin that cannot be loaded is skipped with a `[RoslynMcp] plugin skipped (<id or path>): <reason>` line on stderr at the moment of the skip and in `logs/mcp-*.log` after a successful start; the process starts with the remaining plugins and the built-in tools. `get_mcp_server_info` adds the loaded plugins (id, entry file that was opened, tool names) and one line per skipped source. `get_tool_help` for a plugin tool returns the `[Description]` and parameters of its accepted method and reports `Kind: plugin` / `Group: plugin`. Plugin tools are counted in `Registered MCP tools`, are not filtered by `lite`/`full`, and never enter `McpToolCatalog` or `list_tool_groups`.
- **Catalog size** — unchanged: full 54 tools / 45,135 bytes; lite 15 / 17,491. Plugin tools are not catalog entries.

### v1.4.17

- **`get_changed_files`** — `git status` reads stdout and stderr together and stops the process if that does not finish. The budget is 180s for the run plus up to 3s to stop it, on Windows, Linux, and macOS. A client that cancels sooner still cancels; the server limit is the backstop when the client waits longer.
- **Catalog size** — full 54 tools / 45,135 bytes; lite 15 / 17,491. The increase is the longer `get_changed_files` description.

### v1.4.16

- **Log lines name the calling harness** — every Serilog line carries a short session token after the level (`[cursor-k7m2]`, `[dsh-p9qx]`, `[opencode-4mht]`). The name is taken from the immediate parent process (`Cursor.exe`, `claude`, `codex`, `opencode`). When that parent is `node` or `nodejs`, the name comes from a package or directory in its command line (`@deepseek-ai/dsh`, Claude Code, Codex, OpenCode, `cursor-agent`). The command line is not written to the log. A 4-character suffix separates two processes of the same harness. An unrecognized parent is `cli`. Stderr startup line includes `session=`.

### v1.4.15

- **Edits keep the file's BOM state** — `update_file_content`, `apply_patch`, AST edits, and the disk-sync/watcher write path no longer add a UTF-8 BOM to files that had none, and no longer strip a BOM that existed. `MSBuildWorkspace` rewrites a changed document during `TryApplyChanges` with `SourceText.Encoding`, and `Encoding.UTF8` carries a BOM preamble; the complementary `File.WriteAllText` without an encoding stripped one. Both writes of one operation now resolve the encoding from the file (existing document → candidate text → on-disk BOM), falling back to BOM-free UTF-8 only for a new file. No `RoslynMcp.jsonc` key; the previous behavior was a bug, not a setting.
- **Catalog size** — full 54 tools / 45,093 bytes; lite 15 / 17,449.

### v1.4.14

- **`load_workspace` health** — a missing `obj` next to the project no longer fails the tool. Restore assets are also accepted from `artifacts/obj/<ProjectName>/project.assets.json` and from the `obj` folder beside the output `bin` directory.

### v1.4.13

- **Concurrent `load_workspace`** — a caller cancelled while waiting for the in-progress load still throws `OperationCanceledException` when `SemaphoreSlim` grants the lock in the same race as `Cancel`. The load already inside the lock is not cancelled.

### v1.4.12

- **Lite demotion (membership only)** — `find_symbol_references`, `find_implementations`, and `get_call_graph` moved from `core` to the new `navigation` group; `get_code_skeleton` moved from `core` to `files`. All four remain in `full`. Bare `lite` no longer lists them; enable `navigation` or `files` (or set `ROSLYN_MCP_TOOL_GROUPS`) to get them. `find_symbol_definition` and `find_usages` stay in `core`. Default profile is still `full`.
- **Catalog size** — full unchanged 54 tools / 45,093 bytes; lite 15 / 17,449.

### v1.4.11

- **Nine tools withheld from `tools/list` until v2.0** — `remove_using`, `organize_usings`, `add_property_to_class`, `add_field_to_class`, `add_type_to_class_bases`, `remove_member`, `implement_interface`, `extract_interface`, `move_type_to_new_file`. Names stay in source with `[McpServerTool]`; delete the methods in v2.0 if this decision does not change. Neither `full` nor `lite` registers them (`McpToolCatalog.WithheldUntilV2`).
- **Catalog size** — full 54 tools / 45,093 bytes; lite unchanged at 19 / 22,087.

### v1.4.10

- **`search_code` ripgrep is opt-in** — `useRipgrep` defaults to false and keeps the managed file walk (PATH is not probed, `rg` is not started). `true` uses ripgrep only: missing `rg` is an error and does not fall back to the managed walk. Optional `ripgrepPath` is used only with the flag; otherwise it is ignored and the response says so. If the argument is omitted, `ripgrep-path` from `RoslynMcp.jsonc` is used, then `rg` on PATH. The config key alone does not switch the engine. Roots, extensions, case, `maxResults`, and `maxScanSeconds` stay the same. Cancellation and timeout kill the `rg` process tree. Output stays `file:line | text`. The header notes that hidden and gitignored files may be skipped. With the flag, `useRegex` is ripgrep syntax.
- **Catalog size** — full 63 tools / 50,474 bytes; lite 19 / 22,087.

### v1.4.9

- **Optional `RoslynMcp.jsonc` and passed-only `load_workspace` compare** — the server reads `RoslynMcp.jsonc` from the executable directory, then from the process working directory (cwd wins; comments allowed). Relative `workspace-path` resolves against cwd. `ROSLYN_MCP_WORKSPACE` still sets that directory at startup. No file means call `load_workspace` yourself. If `workspace-path` is set, the first semantic tool loads through `LoadAndPrepareAsync` and waits; it does not cancel a load already in progress. A broken file disables lazy load; `load_workspace` still works and the parse failure stays visible. `load_workspace` compares only arguments you passed: an omitted configuration, platform, or target framework does not reopen and does not clear the value already loaded. A different configuration reloads and keeps the platform and TFM you did not mention. `max-results` and `preview` in the file apply only when the tool argument is omitted (`ROSLYN_MCP_MAX_RESULTS` and an explicit arg still win). `ripgrep-path` is stored for a later opt-in and does not switch search. `shadowCopyInSolutionAnalyzers` is not a config key. `get_mcp_server_info` shows which files were read, the merged keys, and whether the solution came from the file or from `load_workspace`.
- **Catalog size** — full 63 tools / 50,074 bytes; lite 19 / 22,087.

### v1.4.8

- **`find_symbol_definition` pages the tail** — optional `maxResults` and `overflowCursor`, same rules as references (explicit arg, else `ROSLYN_MCP_MAX_RESULTS`, else 50, clamp 1–500). Every in-source place is collected first (column and full name unchanged), then the already-built list is capped. The next chunk is the same tool with `overflowCursor` and does not search again. The old hard stop at 200 places is gone. A call that used to return up to 200 places now returns the first page plus a cursor when more remain. No `%TEMP%` file.
- **Catalog size** — full 63 tools / 50,153 bytes; lite 19 / 22,246.

### v1.4.7

- **`rename_symbol` and `get_call_graph` no longer pick the first overload** — optional `line` and `column` must be passed together or both omitted. With no position, several stage-1 declarations of that name (for the graph: ordinary methods of the named class) return an error listing FQN and identifier line:column, and rename writes nothing. With both coordinates, `SourcePositionHelper` selects that declaration or call site. A call-graph position must be a method. `previewOnly` stays the default. `maxNodes` truncation is unchanged. No `%TEMP%` dump.
- **Catalog size** — full 63 tools / 50,028 bytes; lite 19 / 22,121.

### v1.4.6

- **`find_symbol_references` `directOnly`** — optional, default false, and only valid with `filePath`. For a class virtual, abstract, or override method, `true` keeps calls whose static receiver is the declaring type or a type derived from it (`this`, the type itself, a derived type, `?.`). A base-type receiver or a sibling branch is virtual dispatch. If the receiver cannot be resolved, the location is kept. Source and metadata forms of the same type match by full name; closed `T<int>` matches open `T<>` via `ConstructedFrom`. Default false returns the same points and adds one note when some of them are virtual dispatch. The base virtual method is not filtered and gets no note. Interface methods and ordinary methods are unchanged. `directOnly: true` without `filePath` is an error. The filter runs after dedup and before the overflow cap.
- **Catalog size** — full 63 tools / 49,851 bytes; lite 19 / 22,031.

### v1.4.5

- **`find_implementations` lists every same-named base** — a short name that matches several interfaces or base classes returns a section per base (exact FQN still selects one type). Each base is remapped onto the sanitized snapshot before `FindImplementationsAsync` / `FindDerivedClassesAsync`. An empty section is kept. FQN miss returns the resolver candidate text and does not fall back to the short name. A unique name keeps the previous single-type header. No new parameters.
- **Catalog size** — full 63 tools / 49,763 bytes; lite 19 / 21,943.

### v1.4.4

- **File declaration pick and definition coordinates** — `find_symbol_references` and `find_symbol_definition` with `filePath` and no `line` select the single matching declaration in that file (ordinal, case-sensitive). Kinds: class, struct, record, interface, enum, method, constructor, destructor, property, event, field, event field. Several matches is an error listing FQN and identifier `line:column` (no silent first). Operator, indexer, and local function stay line/column only. A unique file match on `find_symbol_definition` prints every in-source location of that symbol (including all `partial` parts), each with column and full name (`SymbolDeclarationResolver.GetSymbolFqn`, no `global::`). Solution-wide name search stays case-insensitive. Positional `filePath`+`line` is unchanged. No `directOnly`.
- **Catalog size** — full 63 tools / 49,675 bytes; lite 19 / 21,855.

### v1.4.3

- **Lossless diagnostic report (hybrid)** — when `run_dotnet_build` / `run_dotnet_test` / `run_specific_test` / `run_test_by_filter` / `run_dotnet_run` return a truncated excerpt (head/tail or raw tail) or a partial/unparsed test/build failure, the redacted full combined log is kept in-process (max 4 reports, ≤1 000 000 chars each, 15 min TTL, 16 000-char chunks). Pass optional `reportCursor` to the same tool for the next chunk (no new process). Successful short all-pass reports do not store or mention a cursor. Secrets (`password`/`token`/`Bearer`/URL userinfo, etc.) are redacted to `[redacted]` before store. Not a `%Temp%` path. Inline excerpt and StdOut/StdErr budgets from 1.3.24 unchanged; VSTest aggregation (item 2) and nonzero-exit gated success (item 5) unchanged.
- **Catalog size** — full 63 tools / 49,682 bytes; lite 19 / 21,862.

### v1.4.2

- **Navigation S3 (maxResults / preview / overflow)** — `find_symbol_references`, `find_usages`, and `find_implementations` accept optional `maxResults` (default 50, or env `ROSLYN_MCP_MAX_RESULTS` when a positive int; explicit arg wins; clamp 1–500), `preview` (default false: `path:line:col` only; true adds the source line, truncated at 400 chars), and `overflowCursor` (fetch the next in-memory overflow chunk; does not start a new search). Excess locations are stored in-process (max 8 entries, ≤2 000 000 chars total, 30 min TTL, 16 000-char chunks) — not silent drop and not `%Temp%` files. S1 position and S2 FQN resolver unchanged. `find_usages` kept. No S4/S5.
- **Catalog size** — full 63 tools / 49,117 bytes; lite 19 / 21,410.

### v1.4.1

- **Navigation S2 (name/FQN, optional filePath)** — `find_symbol_references` `filePath` is optional. Omit it for solution-wide simple name (all declaration groups) or exact FQN (`Namespace.Type` / `Namespace.Type.Member`; no `global::`, no `()`). FQN miss lists candidates and does **not** fall back to simple name. Overloads share one FQN group. `find_usages` kept as the name-based alias (same resolver; no `PickPrimarySymbol`). S1 positional path unchanged. No S3/S4/S5.
- **Catalog size** — full 63 tools / 47,707 bytes; lite 19 / 20,000.

### v1.4.0

- **Navigation S1 (positional)** — optional `line`/`column` (1-based) on `find_symbol_references` and optional `filePath`/`line`/`column` on `find_symbol_definition`. With `line` set, resolves the symbol at that position (declaration or usage) via `SourcePositionHelper`; auto-column picks the unique identifier token matching `symbolName` on the line (never `string.IndexOf`); ambiguity lists columns. Calls without `line` keep prior behavior. `find_usages` unchanged. No FQN-without-file (S2), maxResults/overflow (S3), enclosing-member fallback (S4), or `directOnly` (S5).
- **Catalog size** — full 63 tools / 47,348 bytes; lite 19 / 19,641.

### v1.3.41

- **`search_code` multi-root scope** — when `directoryPath` is omitted, scans the loaded `.sln`/`.csproj` directory union each project's directory (`ComputeWatchRoots`; nested duplicates dropped), so external projects are covered and loose `.cs` at the solution root still matches. Explicit `directoryPath` stays a single root. Shared `maxResults` / `maxScanSeconds` across roots; missing roots are skipped (error only if every root is missing).
- **Catalog size** — full 63 tools / 45,989 bytes (search_code description); lite 19 / 18,282.

### v1.3.40

- **Mixed non-C# projects no longer fail `load_workspace`** — diagnostics like `file extension '.vcxproj' is not associated with a language` (or a quoted non-C# `*.proj` extension) are soft advisories; loaded C# projects stay usable and the message remains visible. `Project file not found` on a C# `.csproj` and any `: error NU/MSB/NETSDK` / hard MSBuild load failure stay blocking.
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.39

- **CLI `Platform` is target-aware** — `.sln`/`.slnx` keep verbatim `Any CPU` (`-p:Platform="Any CPU"`) so MSBuild solution configs do not fail with MSB4126; `.csproj` still gets canonical `AnyCPU`. `load_workspace` continues to set the MSBuildWorkspace global property to `AnyCPU`. Session inherit stores both `LoadedPlatform` (canonical) and `LoadedPlatformRaw` (trimmed); omitted `platform` on build/test picks by the file being built. `FormatConfigurationProperty` (`-p:Configuration=`) is unchanged.
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.38

- **Disk watcher covers projects outside the `.sln` folder** — `ComputeWatchRoots` unions the loaded `.sln`/`.csproj` directory with each project's directory and drops nested duplicates (if A contains B, keep A). One recursive `FileSystemWatcher` per remaining root (same `*.*` / NotifyFilter / 64KB Windows buffer). A root that fails to start is logged and skipped; Linux inotify errors still degrade without crashing. Disk-sync is unchanged: known documents only (`WithDocumentText`), no `AddDocument`/`RemoveDocument`.
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.37

- **Filtered `dotnet test` on a `.sln` is success when a sibling assembly prints `No test matches`** — xUnit often exits **1** even though another assembly `Passed: 1`. The report uses a gated success (`parsed TestSummary`, `Failed == 0`, `Total > 0`, not silent); `exitCode` is not in that predicate. Non-zero exit adds one italic note; metadata still has the real exit. `## Filtered test run — no matching tests` only when no tests ran and the log has that VSTest line. Hung restore / `Build FAILED` without a summary stays not-success.
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.36

- **`run_dotnet_test` / `run_specific_test` sum VSTest counts across test assemblies** — on a `.sln` each project prints `Total tests:` / `Passed!`; the parser no longer keeps the last end-summary or the first totals block plus leaked `Skipped:` from another assembly. Blocks are aggregated; a `Total > 0` block without count lines fail-closes the whole summary (`null` / partial). `Total tests: 0` without counts is a zero block. `.slnx` fail-only `Passed` inference and StdOut/StdErr budgets are unchanged.
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.35

- **Solution-wide SymbolFinder no longer dies on a missing analyzer DLL** — Roslyn 5.9.0 cannot checksum a project that still has an `UnresolvedAnalyzerReference` stub, so one missing analyzer DLL used to break `find_usages` / `find_symbol_references` / `find_implementations` / `get_call_graph` / `rename_symbol` and `run_specific_test` filter resolution for the whole solution. Search now uses a **cached sanitized snapshot** of the published graph (stubs stripped, one retry). Raw published solution is unchanged, so health/load diagnostics and overlay admission still see the missing path. The sanitized graph is never `TryApplyChanges`'d into `MSBuildWorkspace` and never `SetPublishedSolution`.
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.34

- **Child `dotnet` processes always use English CLI UI** — `DotNetCliRunner.CreateProcessStartInfo` sets `DOTNET_CLI_UI_LANGUAGE=en-US` (kept next to `MSBUILDDISABLENODEREUSE=1`) so MSBuild/VSTest print `Passed!`, not `Пройдено!`, on ru-RU machines. English-only parsers are unchanged. Run metadata surfaces `DOTNET_CLI_UI_LANGUAGE: en-US`.
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.33

- **`get_test_list` `fullyQualifiedName` is a VSTest FQN** — `Ns.Type.Method` with no `global::` and no `()`, via `TestFilterHelper.FormatVstestFullyQualifiedName` (same helper as the `nameContains` filter). Previously `SymbolDisplayFormat.FullyQualifiedFormat` produced `global::Ns.Type.Method()`, which breaks VSTest `--filter` (parentheses are expression grouping, pitfall 11). Nested/generic adapter FQN (`+` vs `.`) is still a follow-up.
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.32

- **`dotnet` CLI timeout no longer hangs the MCP process** — `DotNetCliRunner` waits with `WhenAny`+`Delay` (not `WaitForExitAsync(token)`), kills the process tree, and drains pipes with a 3 s cap. Nested `dotnet build` / MSBuild often ignore cancel on redirected stdout, which left `run_dotnet_build` / `run_dotnet_test` / `run_dotnet_run` (and the lifecycle host `build` op) stuck until the client RPC limit. Child processes also set `MSBUILDDISABLENODEREUSE=1`.
- **Lifecycle host `build` honors `TimeoutMs`** — was hardcoded to 45 s and ignored the command budget (now 120 s for epoch-1 matrix builds).
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.31

- **`update_file_content` no longer recreates a deleted `.cs`** — `Skipped("missing-on-disk")` is not treated as «not in workspace, write the file». Disk write on skip is only `no-workspace` / `not-in-workspace`. Same for `apply_patch`. T-7 now also drives the MCP `WriteFile` path via the lifecycle host.
- **Disk-sync does not `WithDocumentText` a missing file** — a cached `SourceText` after `find_symbol_*` / `rename_symbol` no longer forces a new `Solution` instance (and `TryApplyChanges`) when the path is `Unrepresentable`.
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.30

- **Disk-sync no longer mutates `.csproj`** — `WorkspaceDocumentDiskSync` updates texts of **known** documents only. A new or deleted `.cs` is `Unrepresentable`: no `Solution.AddDocument` / `RemoveDocument`, so `MSBuildWorkspace.TryApplyChanges` does not write a duplicate `<Compile Include>` (NETSDK1022). Composition sets `_projectGraphStale` with a dedicated hint (not «a `.csproj` changed») and skips the next `load_workspace` cache. `dotnet build` of an SDK-style project still compiles a new file via glob **before** reload — expected. Missing-on-disk paths are not recreated by `update_file_content` / persist until reload. `update_file_content` skip (`not-in-workspace`) for `.cs` marks composition-stale **before** the 1 s watcher suppress.
- **Agent hint surfaces** — `get_test_list`, `run_specific_test`, successful `load_workspace`, `update_file_content` skip, and existing `WithDiskSyncNotes` navigation tools. Not `run_dotnet_build`. Intentional `AddDocument` in `extract_interface` / `move_type_to_new_file` is unchanged (follow-up).
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.29

- **Custom attributes derived from a framework test attribute are now discovered and filterable** — a single `TestAttributeMatcher` walks the attribute base chain against `FactAttribute` / `TheoryAttribute` (xUnit), `TestAttribute` / `TestCaseAttribute` / `TestCaseSourceAttribute` (NUnit), `TestMethodAttribute` (MSTest). So `[WpfFact]` and this repo's own `AnalyzerLifecycleFactAttribute : FactAttribute` (83 usages in `AnalyzerLifecycle/`) are reported by `get_test_list` and resolve in `run_specific_test` / `run_test_by_filter`, matching what VSTest already ran. MSTest `DataTestMethodAttribute` follows from `TestMethodAttribute`; `DataRowAttribute` is no longer treated as a test marker on its own (it does not create a test without `TestMethod`/`DataTestMethod`).
- **One matcher instead of two** — `TestDiscoveryHelper` and `TestFilterHelper` previously kept separate test-attribute lists that had already drifted (`DataRow` recognized by the filter but not by discovery). Both now call `TestAttributeMatcher`.
- **`get_test_list` payload** — new `totalTestMethodsFound` field (test methods matched **before** `projectName` / `nameContains`). When the scan is truncated at `maxResults` it is a lower bound.
- **Empty-result guidance no longer misleads** — filters are blamed for an empty list only when the workspace actually found test methods. With `totalTestMethodsFound == 0` the message is the workspace-scope one and states that relaxing the filters cannot change the result; the filtered message now reports how many methods were found.
- **Tests** — regression coverage for a custom `FactAttribute`-derived attribute (real xUnit hierarchy), in-source NUnit/MSTest hierarchies, and an unbound-compilation fixture for the syntactic fallback. `ListTests_does_not_match_lookalike_non_test_attributes` is renamed and documented as an over-broad-matcher guard (it cannot detect a regression back to `.ctor`; the positive tests do).
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.28

- **`get_test_list` returned 0 tests on every real workspace** — `HasTestAttribute` called `GetSymbolInfo(AttributeSyntax)`, which returns the attribute **constructor** (`IMethodSymbol`, `Name == ".ctor"`), never the attribute type; the fallback also compared against `Fact`/`Theory` while the real types are `FactAttribute`/`TheoryAttribute`. Result: `count: 0` for any loaded `.sln`/`.slnx` where the attributes actually bind (all real xUnit/NUnit/MSTest projects), while `run_test_by_filter` / `run_specific_test` still found and ran the same tests (they use `TestFilterHelper`, which checks `AttributeClass.Name`). Fixed by resolving `IMethodSymbol.ContainingType` (or `GetTypeInfo` type) and matching the type name with the `Attribute` suffix stripped and any qualifier removed; the syntactic name is still used as a fallback only when the attribute does not bind at all.
- **`get_test_list` empty-result guidance** — no longer the only explanation for `count: 0`: with semantic binding fixed, an unfiltered empty list means the loaded workspace really has no test methods (tests live in another `.sln`), and a filtered empty list means the filters matched nothing.
- **`TestDiscoveryHelperTests` were a false green** — the `AdhocWorkspace` fixtures had no metadata references (and no `System.Runtime`, so `FactAttribute`'s base `Attribute` was unresolved with `CS0012`), so the attribute never bound, the syntactic fallback matched, and the suite passed against the broken code. Fixtures now reference corelib + `System.Runtime` + `xunit.core` and use `using Xunit;`, plus regression tests for bound attribute spellings (`[Fact]`, `[FactAttribute]`, `[Xunit.Fact]`) and a look-alike `[NotATest]` that must stay ignored. Reverting the fix now fails 8 of these tests.
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.27

- **`run_dotnet_test` / `run_specific_test` / `run_test_by_filter` / `run_dotnet_run` protocol progress** — the same MCP `notifications/progress` heartbeat as `run_dotnet_build` (step label, current-step elapsed, previous exit; default **5 s**; no stdout). Test tools report `dotnet build` then `dotnet test` when `noBuild=false`, or only `dotnet test` when `noBuild=true`; `run_dotnet_run` reports `dotnet run`. Pre-test compile is still one incremental `dotnet build`, not the build probe. **Not** a replacement for `timeoutSeconds` and **not** a fix for host `tools/call` limits (`-32001`, Cursor ACP ~60 s). No new tool parameters and no catalog change (`IProgress` stays schema-excluded).
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.26

- **`run_dotnet_build` progress precision** — the reported `elapsed` is always the **current step** (a step boundary reports `starting`, not the whole-probe clock), and the numeric `Progress` is whole elapsed seconds with a strictly increasing value instead of a 1, 2, 3 counter that a progress bar renders as "1%". No `Total` is sent, so it is **not** a completion percentage — hosts should render `Message`. The adapter now honors the `ICliProgressReporter` contract itself (transport failures are swallowed there, not only at probe/runner call sites).
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.25

- **`run_dotnet_build` protocol progress** — while a live `dotnet build` / restore step runs, the tool emits MCP `notifications/progress` heartbeats (default every **5 s**) plus one report at each probe step boundary: step label, elapsed seconds, and the previous step's exit code. No stdout and no machine paths are sent, and the text is a heartbeat for a live agent — **not** a result, **not** a replacement for `timeoutSeconds`, and **not** a fix for host `tools/call` limits (`-32001`, Cursor ACP ~60 s). No new tool parameters and no catalog change: the SDK-supplied `IProgress<ProgressNotificationValue>` parameter is excluded from the JSON schema, and a host that sends no progress token gets the SDK's no-op instance, so no heartbeat timer is started at all and the build result is unchanged (`ModelContextProtocol.NullProgress` is detected by type; if a future SDK renames it, the only cost is a harmless timer). `run_dotnet_test` / `run_specific_test` / `run_dotnet_run` do not report progress yet.
- **Catalog size** — unchanged: full 63 tools / 45,868 bytes; lite 19 / 18,282.

### v1.3.24

- **Failed-test Standard Output in `run_specific_test` / `run_dotnet_test` / `run_test_by_filter`** — VSTest `Standard Output Messages:` / `Standard Error Messages:` (ConversationId, `TestContext`, `ITestOutputHelper`) are included in the markdown report instead of being used only as a parser terminator. Default per-failure budget is **2500** chars of StdOut (head **1600** + tail **700**) and **1000** of StdErr so assertion text stays intact. Optional `includeFullOutput=true` raises the cap to **100000** per stream; optional `maxOutputChars` sets an explicit StdOut budget (StdErr scales; both clamped at 100000). Truncated streams tell the agent to pass those knobs. Do not shell-out `dotnet test` to recover ConversationId.
- **Catalog size** — full 63 tools / 45,868 bytes; lite 19 / 18,282 (two optional parameters on the three test tools).

### v1.3.23

- **`run_specific_test` filter vs xUnit display names** — a passing filtered run is no longer reported as **no matching tests** when VSTest prints a method-only name (`methodDisplay=method`) or Theory `FQN(args)` while the filter needle is `Ns.Class.Method`. Parser matches a display-name suffix (dot-bounded) and still rejects a different method that only shares a tail (`OtherMethod` ≠ `Method`). Slow durations such as `[3 m 8 s]` stay valid.
- **Catalog size** — unchanged: full 63 tools / 44,503 bytes; lite 19 / 16,917.

### v1.3.22

- **`run_dotnet_test` / `run_specific_test` failure text** — VSTest `Error Message:` stays multiline (head+tail of the assertion, not a 512-character one-liner). The MSBuild footer `Build FAILED` / `0 Error(s)` that follows a failed test is not treated as a compile failure, not a silent/unparsed failure, and is stripped from raw-tail fallbacks so agents see the assertion (FluentAssertions diffs included) instead of empty compiler counters. Theory `Failed FQN(args)` lines match the filter. MCP log highlights skip those outcome lines.
- **Catalog size** — unchanged: full 63 tools / 44,503 bytes; lite 19 / 16,917.

### v1.3.21

- **U-ARB-01 inaccessible original is skip.** An original `AnalyzerReference` path that cannot be probed (`access_failure`) is not rewritten from a confirmed project output and is not treated as missing. The load summary reports the skip (and the unused confirmed project, if known). Source-output access failure is unchanged. Alternatives (rewrite / fail-closed) remain documented and can change after real-repo evidence. Public MCP schema is unchanged.
- **Catalog size** — unchanged: full 63 tools / 44,503 bytes; lite 19 / 16,917.

### v1.3.20

- **Preserve DependencyUnsupported / main-only through a full publication ban (v3 S8).** When every confirmed overlay reference is blocked and none is applied, the publication plan keeps the first non-restart `AnalyzerExecutionGate` observation (`Status`, `Reason`, `Action`, `DependencyName`) instead of replacing it with `LoadFailed` / `opt-in-prepare-not-enabled`. Restart-required still uses the existing restart ban. Overlay stays unpublished (`Applied=false`). Public MCP schema is unchanged.
- **Catalog size** — unchanged: full 63 tools / 44,503 bytes; lite 19 / 16,917.

### v1.3.19

- **Safe publication for a mixed analyzer prepare (v3 S5).** Opt-in overlay admission is no longer `HasAnyApplied`. Every confirmed in-solution reference is either applied (fresh or allowed stale), excluded from the semantic snapshot, or the whole snapshot is banned/unavailable. A failed confirmed reference is never published as real build output because another generator prepared. Load text reports `prepared` / `applied` / `stale` / `blocked`; a blocked publication does not claim `Applied=true` from leftover files. Exact inverse restores intentional exclusions and still rejects unknown analyzer diffs. Public MCP schema is unchanged.
- **Catalog size** — unchanged: full 63 tools / 44,503 bytes; lite 19 / 16,917.

### v1.3.18

- **Withhold opt-in semantics when provenance capture is unsuitable (V3-R3).** For `shadowCopyInSolutionAnalyzers=true`, a missing, Failed, Incomplete, or other-session capture no longer publishes a raw semantic snapshot. The MSBuild graph can still open; the load text reports capture status, the refusal reason, and that overlay is not allowed. Cached `true` reuses the failed snapshot and does not restore semantics — call `reset_workspace` then `load_workspace` (or reopen another load key). Complete capture still keeps the confirmed-only matcher (missing-path / foreign fixtures). Honest no-overlay load is unchanged. Public MCP schema is unchanged.
- **Catalog size** — unchanged: full 63 tools / 44,503 bytes; lite 19 / 16,917.

### v1.3.17

- **Persistent fail-closed publication after a failed opt-in (V3-R2).** Semantic publication admission is now a load-session policy, not the last prepare/execution observation. A failed opt-in stays banned across text edit, watcher flush, reconciliation, load-boundary cancellation, and cached `false`/omitted. Ordinary publication reapplies a predetermined excluded-reference set and does not inspect or probe analyzer files. Cached `true` may restore only after a successful allowed prepare (identity gate still applies). Honest no-overlay still publishes raw references; successful opt-in still executes the exact marker from the shadow path. Public MCP schema is unchanged.
- **Catalog size** — unchanged: full 63 tools / 44,503 bytes; lite 19 / 16,917.

### v1.3.16

- **Reject stale write bases before any disk or workspace mutation (V3-R1 / A4-09).** A candidate built on an older published snapshot is `PreflightRejected` (`stale-base`, `stale-publication`, or `unknown-operation-context`) before document persist, project-file write, `TryApplyChanges`, or reconciliation. Same-session intervening edits, watcher flush, reset/reload, and unverifiable external candidates cannot overwrite newer text. Fresh under-lock update/flush still apply. Public MCP schema is unchanged.
- **Catalog size** — unchanged: full 63 tools / 44,503 bytes; lite 19 / 16,917.

### v1.3.15

- **No implicit semantic workspace load (U-ARB-04-IMPL-1).** `FindDocumentAsync` no longer walks parent directories and publishes a raw workspace when semantic tools are called before `load_workspace`. It returns no document, matching the existing tool contract and no-workspace guidance; clients must load a workspace explicitly.
- **Catalog size** — unchanged: full 63 tools / 44,503 bytes; lite 19 / 16,917.

### v1.3.14

- **Atomic analyzer load/prepare boundary (U-ARB-04).** Opt-in `load_workspace(..., shadowCopyInSolutionAnalyzers=true)` now performs physical load/cache lookup, shadow preparation, execution gate, and final snapshot publication under one manager lock. Production semantic readers wait for that boundary and consume only the published snapshot; they cannot compile an intermediate raw analyzer path. A new-session prepare failure or cancellation is fail-closed, while an existing applied mapping remains available as stale where the prior contract permits it. The public MCP schema is unchanged.
- **Catalog size** — unchanged: full 63 tools / 44,503 bytes; lite 19 / 16,917.

### v1.3.13

- **Repeated analyzer-shadow flag contract (U-ARB-05).** `shadowCopyInSolutionAnalyzers` activation is session-sticky. After successful enablement, a same-key cached `false`/omitted call preserves the active mapping/generation and performs no analyzer preparation or refresh. Disable with `reset_workspace` followed by a load without the flag; another load key or graph reopen also starts a new session without the old overlay. This preserves v1.3.5+ compatibility because the optional non-nullable boolean cannot distinguish omission by an older client from explicit `false`. Cached loads now explicitly report when an active overlay was preserved. No new MCP parameter or schema shape.
- **Catalog size** — full 63 tools / 44,503 bytes; lite 19 / 16,917.

### v1.3.12

- **Analyzer provenance acceptance (E5-S4).** The original redirected missing-path repro now rewrites from confirmed load-session provenance and executes exact `V1`. Same-name foreign analyzers keep their path and execute exact `FOREIGN` (missing foreign is not replaced by the in-solution candidate). Lifecycle diagnostics distinguish `source_output_missing`, `provenance_unconfirmed`, and `access_failure` on those fixtures. The public `load_workspace` schema is unchanged.
- **Catalog size** — unchanged: full 63 tools / 44,165 bytes; lite 19 / 16,579.

### v1.3.11

- **Analyzer provenance diagnostics (E5-S3).** Shadow-copy decisions now carry stable internal reason codes and independent original/source path states, plus selected project, selection basis, source path, and generation. Concrete missing-output, ambiguity, proven-foreign, access, and preparation failures are no longer collapsed into generic unconfirmed provenance. The `load_workspace` response remains a compact text summary; detailed fields are logged without adding a public structured MCP schema.
- **Catalog size** — unchanged: full 63 tools / 44,165 bytes; lite 19 / 16,579.

### v1.3.10

- **Analyzer provenance rollout (E5-S2).** `shadowCopyInSolutionAnalyzers` now rewrites only analyzer references that the complete load-session F-09 snapshot exact-joins to one loaded source project. Filename/`AssemblyName`, first-candidate, and incomplete-capture fallbacks are removed. Same-name external or unconfirmed missing references remain unchanged; ambiguous source projects are skipped. Source binding requires an exact resolved output and rejects contradictory effective TFM metadata.
- **Catalog size** — unchanged: full 63 tools / 44,165 bytes; lite 19 / 16,579.

### v1.3.9

- **Analyzer provenance capture (F-09 production snapshot).** Every physical `MSBuildWorkspace` open now records the same design-time build into process-private binlogs, replays them into an immutable load-session snapshot, and exact-joins captured analyzer items to loaded consumer/source `ProjectId`s. Replay is fail-closed and temporary data is deleted before publication. Cached loads, source edits, semantic queries, and analyzer artifact refreshes do not recapture; graph-stale, changed load globals, and reset+load do. This release does not change the analyzer matcher or enable E5-S2 rollout.
- **Catalog size** — unchanged: full 63 tools / 44,165 bytes; lite 19 / 16,579.

### v1.3.8

- **Workspace write boundary (epoch 4).** All production `TryApplyChanges` and server document writes go through one preflight → exact original↔shadow inverse → persist → apply/reconciliation → overlay publish workflow. Unsupported or stale analyzer-reference diffs (including CodeAction / `rename_symbol`) are rejected before any server write instead of silently wiping the analyzer list. Partial persistence is not full success: the reply includes Status, Reason, and the known saved paths (existing tool text, no new MCP schema). Post-apply reuses the prepared mapping with no analyzer file I/O. Upgrade from v1.3.5: consumer edit no longer recopies analyzer files and will not pick up rebuilt generator bytes — after a same-identity rebuild, restart the MCP process.
- **Catalog size** — unchanged: full 63 tools / 44,165 bytes; lite 19 / 16,579.

### v1.3.7

- **`shadowCopyInSolutionAnalyzers` loader contract (epoch 3).** Chosen mode is **restart-required**: a rebuilt generator with the same assembly name/version cannot execute in the current MCP process (cached load and `reset_workspace`+load refuse with an explicit “restart the MCP server process” action). A new process runs the new bytes. Supported dependency policy is **main-only**; private helper DLLs are refused rather than guessed from the output directory. Rewrite count still means reference rewrite only; execution status is a separate line in the load summary.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 44,165 bytes; lite 19 / 16,579.

### v1.3.6

- **`shadowCopyInSolutionAnalyzers` immutable mapping (epoch 2).** Analyzer shadow copies are content-hashed main-only generations under `v2-main-only/` (manifest-last, same-volume no-replace publish). Preparation runs at load/enable and explicit refresh only; document edit, watcher flush, and post-apply reapply the in-memory mapping with **zero analyzer file I/O**, so a missing original output path no longer drops the overlay. Failed refresh keeps a stale mapping instead of reverting to broken originals. Timestamp directories are not migrated. CLR reload of a rebuilt generator remains an epoch-3 / U-ARB-02 gate.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 43,895 bytes; lite 19 / 16,309.

### v1.3.5

- **Fix: `shadowCopyInSolutionAnalyzers` (v1.3.4) no longer touches the real `.csproj` on disk.** The v1.3.4 implementation applied the rewritten `Solution` via `Workspace.TryApplyChanges`, which for `MSBuildWorkspace` persists `AddAnalyzerReference`/`RemoveAnalyzerReference` back into the backing project file. Confirmed against the `C:\Scratch\GenRepro` repro: this injected a machine-/session-specific temp shadow path as a literal `<Analyzer Include=...>` MSBuild item and could not remove the original `ProjectReference OutputItemType="Analyzer"` reference (it is synthesized by MSBuild, not a literal item), leaving both active — the generator then ran twice and the next real `dotnet build` failed with `CS0102`/`CS0111` duplicate-member errors. Fix: `SolutionManager` now keeps the rewrite purely in-memory instead of ever pushing it back via `TryApplyChanges`. *(Historical wording said `GetCurrentSolution()` re-derived the overlay on every read. The getter returned a stored `_solution` even then; v1.3.6+ documents that explicitly and reapplies mapping only at mutation points.)* `FindDocumentAsync` (backs `get_diagnostics_for_file`, `find_symbol_references`, AST/code-fix tools) now also reads through `GetCurrentSolution()` instead of `workspace.CurrentSolution` directly, so the fixed semantic model (no more `CS0103` on generator-produced types) reaches those tools too. Any tool that edits a document and calls back into `ApplySolutionChangesToDiskAsync` (rename, code fix, generated test stub) is defended by a new guard that strips any accidental `AnalyzerReference` diff before it reaches `TryApplyChanges`, so the same corruption cannot resurface via a different call path. No tool parameters changed; `shadowCopyInSolutionAnalyzers` behaves the same from the caller's side, just without the disk side effect. **Known separate limitation (not something this fixes):** `find_symbol_definition` still cannot locate a generator-emitted type by name — `SymbolFinder` [never searches source-generated documents, by Roslyn design](https://github.com/dotnet/roslyn/issues/63375). `get_diagnostics_for_file` / `find_symbol_references` from a real usage site are unaffected.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / (see `get_mcp_server_info`); lite 19 / (unchanged — no tool surface change).

### v1.3.4

- **`load_workspace` `shadowCopyInSolutionAnalyzers`** — Optional bool (default `false`). Fixes the case `logProjectOutputDiagnostics` (v1.3.3) diagnoses: rewrites any `AnalyzerReference` whose file name matches another (unambiguous) in-solution project's `AssemblyName` to a private shadow copy of that project's own resolved output (`CompilationOutputInfo.AssemblyPath` / `OutputFilePath`, not the possibly-broken original reference path). Confirmed against an external repro: without the fix, `Directory.Build.props` overriding `OutputPath` left an `OutputItemType="Analyzer"` `ProjectReference`'s resolved path missing on disk, so the generator never ran in the Roslyn semantic model even though `dotnet build` succeeded (`CS0103` on a generator-produced type). Also avoids locking the analyzer project's real build output (a prior `dotnet build` of it would otherwise fail with `MSB3027`) — shadow copies are namespaced by the source file's last-write time, so a rebuilt analyzer is picked up fresh on the next `load_workspace`. Requires the referenced project to already have a build output on disk (build it once first). Returns a one-line summary; details go to the MCP server log via `tail_tool_log` / `read_log_tail`. New: `Services/Analyzers/AnalyzerReferenceShadowCopier.cs` (pure rewrite + `Solution.WithProjectAnalyzerReferences`), `Services/Analyzers/InProcessAnalyzerAssemblyLoader.cs` (public-API-only `IAnalyzerAssemblyLoader`, since Roslyn's own non-locking loader is `internal`). **Superseded by v1.3.5**: the application mechanism described here (`Workspace.TryApplyChanges`) corrupted the real `.csproj` — see the v1.3.5 entry above.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 43,619 bytes; lite 19 / 16,033.

### v1.3.3

- **`load_workspace` `logProjectOutputDiagnostics`** — Optional bool (default `false`). When `true`, logs one Information-level line per project (`OutputFilePath`, exists/last-write, `CompilationOutputInfo.GeneratedFilesOutputDirectory`, exists) plus one line per `AnalyzerReference` (`Display`, `FullPath`, exists/last-write) to the MCP server log — not the tool's return value. Diagnostic-only aid for the known pitfall where a repo-wide `Directory.Build.props` overrides `OutputPath` (e.g. into a shared `artifacts` folder) and MSBuildWorkspace design-time evaluation ends up pointing an analyzer/generator project's `AnalyzerReference` at a stale or missing DLL, silently disabling source generation. Read the result with `tail_tool_log` / `read_log_tail`. New helper: `Services/Workspace/ProjectOutputDiagnosticsLogger.cs` (`Collect` for pure data, `Log` to write it).
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 42,632 bytes; lite 19 / 15,046.

### v1.3.2

- **`binariesPath` on `run_specific_test` / `run_dotnet_test`** — Same bin-directory mode as `run_test_by_filter`: loaded `.sln`/`.slnx` plus a `.csproj` `workspacePath`; runs `{AssemblyName}.dll` from that directory (the DLL sits directly in `binariesPath`, no `OutputPath` join). When `noBuild=false`, pre-test compile is `dotnet build <sln> -t:"Folder\Project"` (configuration/platform/`buildArgs` from `load_workspace`), then `dotnet test <dll> --no-build`. When `noBuild=true`, the DLL must already exist. After a successful sln-target build, a missing DLL reports the expected path plus a Configuration / `.runtimeconfig.json` hint.
- **`run_test_by_filter` `noBuild=false`** — Honor `noBuild` with the same sln-target pre-build; previously any `binariesPath` skipped compile.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 41,958 bytes; lite 19 / 14,372.

### v1.3.1

- **`get_test_list` filters** — Optional `projectName` (exact match on loaded Roslyn project name, file name, or assembly) and `nameContains` (case-insensitive substring of the VSTest FQN, class, or method). Both apply **before** `maxResults`. Unknown or ambiguous `projectName` returns the project list, not empty JSON. Filtered `count: 0` is a no-match signal (not “wrong `.csproj`”). Each test item includes `projectName`.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 41,490 bytes; lite 19 / 13,904.

### v1.3.0

- **`run_test_by_filter`** — New tool. Passes a raw VSTest `--filter` to `dotnet test` (`FullyQualifiedName~MyClass`, `TestCategory=Smoke`, …). Default `noBuild=true`. Optional `binariesPath` is a bin directory: requires a loaded `.sln`/`.slnx` and a `.csproj` `workspacePath`; runs `{AssemblyName}.dll` from that directory (no pre-test build). Prefer `run_specific_test` for one class or method.
- **Catalog size** — minified `tools/list` UTF-8: full 63 tools / 41,139 bytes; lite 19 / 13,904.

### v1.2.3

- **`run_dotnet_build` `projectName`** — Optional. When set, `workspacePath` must be a `.sln`/`.slnx`. Resolves the project's Solution Explorer virtual path (solution folders, not the filesystem path) and runs `dotnet build <sln> -t:"Folder\Project"` so solution Configuration/Platform mappings apply. Match is case-insensitive on display name, file name without extension, or virtual path (`src\App`). Missing or ambiguous names return the `Name → path` list. `.csproj` + `projectName` is an error.
- **`dotnet build` `-p:Configuration`** — Probe steps and the pre-test compile now pass `-p:Configuration=` (same as existing `-p:Platform`). `dotnet test` still uses `-c` so output is found under `bin/{configuration}`.
- Metadata reports `ProjectName` and `SolutionTarget` (`-t`) to diagnose MSB4057.
- **Catalog size** — minified `tools/list` UTF-8: full 62 tools / 39,738 bytes; lite 18 / 12,503.

### v1.2.2

- **`load_workspace` `briefOutput`** — Optional. Default `false` keeps the full MSBuild/NuGet warning dump (plus notes). `true` collapses successful-load warnings to category and code counts (`MSB3270×28`, `NU1701×5`, …). Health block and project list stay. Failures (`Workspace Load Failed`, missing TFM/Compile, BuildHost, client abort) always print in full. Not session-cached.
- **Catalog size** — minified `tools/list` UTF-8: full 62 tools / 39,408 bytes; lite 18 / 12,173.

### v1.2.1

- **`load_workspace` `buildArgs`** — Optional extra arguments appended to later `dotnet build` only: the `run_dotnet_build` probe steps and the pre-test compile used by `run_dotnet_test` / `run_specific_test`. Session-cached like `configuration` / `platform` (not an MSBuildWorkspace property; changing `buildArgs` does not reopen the solution). Omit or whitespace clears the session suffix. Do not put `-c` / `-p:Platform` / `-v` / `--no-incremental` here. Not applied to restore, `dotnet test`, or `dotnet run`.
- **Catalog size** — minified `tools/list` UTF-8: full 62 tools / 39,063 bytes; lite 18 / 11,828.

### v1.2.0

- **Tool profiles** — `ROSLYN_MCP_TOOL_PROFILE=full|lite` (default `full`). Lite starts with 18 core tools (workspace, navigation, build/test, `list_tool_groups` / `get_tool_help` / `enable_tool_group`).
- **Startup groups** — `ROSLYN_MCP_TOOL_GROUPS=decompile,nuget` expands lite before the first `tools/list`. Fallback for clients that ignore `tools/list_changed`.
- **`list_tool_groups` / `get_tool_help`** — compact Markdown help. Parameter names/types/defaults come from the live schema; Markdown does not replace JSON Schema.
- **`enable_tool_group`** — add one catalog group at runtime; one `tools/list_changed` per real change. Idempotent; `full` is a no-op. No runtime disable.
- **Catalog size** — minified `tools/list` UTF-8: full 62 tools / 38,500 bytes; lite 18 / 11,265. Description text on the original 59 tools: 35,445 → 13,387 (−62%).

### v1.1.1

- **`explore_assembly` / decompile `assemblyName`** — dotted simple names (`Spectre.Console`, `Newtonsoft.Json`) are no longer truncated at the last `.`. Only a trailing `.dll` / `.exe` is stripped; previously `Path.GetFileNameWithoutExtension` looked for `Spectre.dll`.

### v1.1.0

- **Disk sync for saved `.cs`** — after `load_workspace`, a `FileSystemWatcher` records dirty source paths (not every keystroke: unsaved editor buffers are ignored). Before `find_symbol_*` / `find_usages` / `get_class_skeleton` / test discovery, only **known** documents are re-read (`WithDocumentText`). Host/git/`dotnet format` edits of already loaded files show up without `reset_workspace`. A new or deleted `.cs` is **not** added/removed from the `Solution` (that would make `TryApplyChanges` write `<Compile Include>` and break SDK globs with NETSDK1022). Instead the project graph is marked composition-stale: call `reset_workspace` then `load_workspace` (or `load_workspace` alone — stale skips the load cache). MSBuild decides membership on reload — the file is not guaranteed to enter the workspace. `.csproj`/`.sln`/`Directory.Build.props` set a separate graph-file stale hint. `reset_workspace` remains for generated `obj` files after build. Linux uses inotify (watch-limit errors log and degrade; they do not crash the process).

### v1.0.35

- **VS 2026 / MSBuild 18 BuildHost (`XMakeElements`)** — `Microsoft.CodeAnalysis.*` **5.9.0** (Roslyn AppDomain isolation for the net472 BuildHost). `load_workspace` / `find_symbol_*` no longer die with a raw `TypeInitializationException` on machines with Visual Studio 2026; residual crashes return dedicated **Workspace Load Failed (VS 2026 / MSBuild 18 BuildHost)** and are **not** `MCP_MSBUILD_SDK_MISMATCH`. Workaround: load a single SDK-style `.csproj`. `search_code` / host Grep still work.

### v1.0.34

- **`load_workspace` design-time MSBuild warnings** — MSBuildWorkspace wraps ASP.NET/SDK deprecation (`IncludeOpenAPIAnalyzers` / ASPDEPR007), processor-architecture mismatch (MSB3270), and analyzer-project metadata refs as `Msbuild failed when processing the file` with `Failure` kind, often **without** a `warning XXXX` prefix. Those no longer fail load when projects opened; remapped to **Warning (MSBuild design-time)**. Wrapped messages without `error NU|MSB|NETSDK` or a known-hard inner text (`could not be loaded`, missing `Compile`, empty TFM, SDK not found) are warnings. Explicit errors and unloadable projects still fail.

### v1.0.33

- **`load_workspace` `targetFramework`** — Optional MSBuild `TargetFramework` global property (same idea as `dotnet build -f`). Needed when `Directory.Build.props` / csproj sets `TargetFrameworks`: the CrossTargeting outer evaluation has no `Compile` target and Roslyn cannot load. Dedicated failure **Workspace Load Failed (missing Compile target)** lists TFMs from the nearest props and tells the agent to retry with one inner TFM (e.g. `net10.0`). Not inherited by `run_dotnet_build`. `get_code_skeleton` / host Grep remain usable without a workspace.

### v1.0.32

- **`run_specific_test` slow-test duration** — VSTest console lines for long runs use `[1 s]` / `[1 m 28 s]`, not only `[12 ms]`. Parser now accepts those units so a passing filtered test is not reported as **no matching tests**.

### v1.0.31

- **`run_dotnet_test` / `run_specific_test` pre-test build** — When `noBuild=false`, compile with a separate incremental `dotnet build` (same `-c` / `-p:Platform`), then `dotnet test --no-build --no-restore`. VSTest summary is parsed from the test process only, so MSBuild warning dumps no longer produce **`Status: partial`**. Build failure/timeout is reported and tests are not started. `noBuild=true` still skips the extra compile. Shared `timeoutSeconds` covers both processes.

### v1.0.30

- **`apply_patch` replaceAll hang** — `replaceAll=true` no longer rescans the inserted `newString`. When `newString` contains `oldString` (typical rename `Foo` → `Ns.Foos`, production case `AllSoftNotificationHelper` → `HelperContainer.Eis.AllSoftNotificationHelpers`) the old `while (IndexOf)` loop grew the file forever and never returned — OpenCode showed a freeze with nothing useful in logs. Matching now advances past each insert (same as `string.Replace`); logs `ApplyPatch start/matched/wrote` with lengths, `newContainsOld`, replacement count, and elapsed ms.

### v1.0.29

- **`load_workspace` configuration / platform** — Optional MSBuild global properties (same names as the VS active solution config). Cached for `run_dotnet_build` / `run_dotnet_test` / `run_specific_test` when those tools omit `-c` / `-p:Platform`. Empty `TargetFramework` (`ResolvePackageAssets`) stays a load failure, with a dedicated report (retry with IDE config, or Bazel-generated csproj are not evaluable).

### v1.0.28

- **`run_dotnet_test` / `run_specific_test` .slnx summary** — VSTest/MSBuild console may omit `Passed:` when tests fail (and omit `Failed:` on success). Parser infers `Passed = Total − Failed − Skipped` so a fail-only `Total tests` block is not reported as **partial**.

### v1.0.27

- **`load_workspace` NU1701 / MSBuild wrapper** — Package TFM-compat restore warnings (`NU1701`, netfx assets in a netcore/net10 project) remapped to warnings — do not fail load when other projects opened. The word `failed` in Roslyn's `Msbuild failed when processing the file` wrapper is no longer treated as fatal by itself; explicit `error NU|MSB|NETSDK` and unloadable projects still fail load.

### v1.0.26

- **Agent diagnostics for workspace/test discovery** — `load_workspace` cancelled by the MCP host returns **Workspace Load Cancelled (client abort)** (not MSBuild failure) with OpenCode timeout guidance; `get_test_list` with `count: 0` explains wrong `.csproj` scope; `run_specific_test` no-match reports Roslyn vs `dotnet test` path mismatch and name-suffix fallback.

### v1.0.25

- **`run_dotnet_build` trust** — Default `noIncremental=true` (`--no-incremental`) so MSBuild up-to-date cache cannot report a fake success after edits; set `false` only for large monorepos that accept incremental risk. Effective exit uses the **last** `dotnet build` step (restore exit 0 cannot mask a failed build with no rebuild). Success/failure metadata includes `Configuration` and `NoIncremental`.

### v1.0.24

- **SDK env metadata** — `run_dotnet_build` / `run_dotnet_test` / `run_specific_test` report inherited `MSBuildSDKsPath`, strip vs `global.json` pin action, and SDK version inferred from MSBuild/NETSDK log paths

### v1.0.23

- **`dotnet` child env** — Without `global.json` pin, strip inherited `MSBuildSDKsPath` / `MSBUILD_EXE_PATH` / `DOTNET_MSBUILD_SDK_RESOLVER_*` (MSBuildLocator / IDE pollution) so `run_dotnet_build` / `run_dotnet_test` use the host SDK instead of e.g. 9.x → `NETSDK1045` on net10

### v1.0.22

- **`run_specific_test` method filter** — VSTest-safe filters: Roslyn method FQN without `()`; always `FullyQualifiedName~` (not `=`); no bogus leading `.` when `methodName` is already a dotted FQN; escape `\ ( ) & | = ! ~`

### v1.0.21

- **`load_workspace` soft prune** — Unused `PackageReference` / NuGet prune advisories (`will not be pruned`, prune package data) remapped to warnings — do not fail load when projects opened

### v1.0.20

- **`configuration`** — Optional `configuration` (`dotnet -c`) on `run_dotnet_build`, `run_dotnet_test`, `run_specific_test` for multi-config solutions (`Sit-Debug`, `Dit-Debug`, …)

### v1.0.19

- **`.slnx` support** — `load_workspace`, `run_dotnet_build`, `run_dotnet_test` / `run_specific_test`, discovery, and NuGet/format paths accept `.slnx`; prefer solution files for multi-config repos
- **OpenCode host timeout** — Document + `opencode.json.sample` / `install2opencode.ps1`: `"timeout": 600000` ms — avoids MCP `-32001` (~60s); separate from tool `timeoutSeconds`

### v1.0.18

- **Tool descriptions** — Accurate agent-facing `[Description]` across build/test/decompile/NuGet/navigation/AST params

### v1.0.17

- **`run_dotnet_test` / `run_specific_test`** — Optional `noBuild` / `noRestore` (`--no-build` / `--no-restore`); after build use `noBuild=true` for faster re-runs. Default `noBuild=false` compiles in a separate `dotnet build` then tests with `--no-build`.

### v1.0.16

- **`run_dotnet_test` / `run_specific_test`** — `timeoutSeconds` default **300**; kill process tree on timeout/cancel
- **`run_dotnet_build` probe** — Overall wall-clock budget (~300s) + per-step timeout; skip escalate when budget exhausted
- **`execute_dotnet_command`** — Same default timeout + kill on cancel
- **Silent fail UX** — Hints for zombie `dotnet` / locked `obj` when exit≠0 and no parsed diagnostics

### v1.0.15

- **`search_code`** — `caseSensitive` (default `false`); leftover branding → `caseSensitive=true`
- **No-workspace UX** — Semantic tools list candidate `.sln`/`.slnx` under `ROSLYN_MCP_WORKSPACE` / cwd (no auto-load)
- **`rename_project`** — SDK-style dir+csproj+ProjectReference+.sln/.slnx; `dryRun`; no namespace chain
- **Branding recipe** — Documented in `AGENTS.md.sample` (hybrid MCP + host edit)

### v1.0.14

- **`run_dotnet_run`** — SDK-pinned `dotnet run`, separate stdout/stderr, timeout, truncated output (stderr tail for progress)
- **`run_nuget_audit`** — Structured vulnerability table from `dotnet list package --vulnerable`
- **`get_changed_files`** — Git porcelain status + suggested test projects (no diff body)
- **`load_workspace`** — **Workspace health** block: SDK/global.json, restore assets, tool count
- **`execute_dotnet_command`** — SDK pinning + truncated stdout/stderr
- **`find_usages` / `find_symbol_references`** — **find_references** family; prefer `find_usages` when only `symbolName` is known
- **`get_project_graph` / `list_projects`** — Project dependency graph
- **`rename_symbol`** — `previewOnly=true` default workflow; **C# symbols only**
- **`run_format`** — `dotnet format` wrapper

### v1.0.13

- **`AssemblyReferenceResolver`** — Exact `{name}.dll`; deps.json + NuGet fallback
- **`DecompilerHost`** — NuGet / BCL / runtime pack resolver for ILSpy tools

### v1.0.10–v1.0.12

Build/test SDK pinning, VSTest parser, decompiler `assemblyPath`, `MCP_MSBUILD_SDK_MISMATCH` — see git history.

### Not implemented (see AGENTS.md.sample — Secrets)

- Read-only TFS / HTTP probe MCP tools
- MCP «secret configured: yes/no» without reading values
- Unified diff in `get_changed_files` (use host/shell `git diff` when allowed)
