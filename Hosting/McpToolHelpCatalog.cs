namespace RoslynMcpServer.Hosting;

/// <summary>
/// Supplemental help only. Names, types, required/default values, and group membership
/// are generated from the catalog and the registered method schema.
/// </summary>
public static class McpToolHelpCatalog
{
    public const string ServerInstructions =
        "Call list_tool_groups, get_tool_help, and enable_tool_group; clients that ignore tools/list_changed should restart with ROSLYN_MCP_TOOL_GROUPS.";

    public static IReadOnlyDictionary<string, McpToolHelpEntry> Entries { get; } = Build();

    public static bool TryGet(string toolName, out McpToolHelpEntry entry)
    {
        foreach (var pair in Entries)
        {
            if (pair.Key.Equals(toolName, StringComparison.Ordinal))
            {
                entry = pair.Value;
                return true;
            }
        }

        entry = null!;
        return false;
    }

    private static IReadOnlyDictionary<string, McpToolHelpEntry> Build() =>
        new Dictionary<string, McpToolHelpEntry>(StringComparer.Ordinal)
        {
            ["list_tool_groups"] = new()
            {
                Workflow = "Call this first on a lite profile to see which groups are active and which tool names they contain.",
                RelatedTools = ["get_tool_help", "enable_tool_group"],
            },
            ["get_tool_help"] = new()
            {
                Workflow = "Pass an exact public tool name. Parameters are generated from the live schema, not from this help catalog.",
                Pitfalls = "Unknown names return close matches. This is not a substitute for JSON Schema on tools/list.",
                RelatedTools = ["list_tool_groups", "enable_tool_group"],
            },
            ["enable_tool_group"] = new()
            {
                Workflow = "Pass one exact group name. Adds missing typed tools and sends tools/list_changed. Repeated calls and the full profile are no-ops.",
                Pitfalls = "There is no disable in this release. Clients that ignore tools/list_changed must restart with ROSLYN_MCP_TOOL_GROUPS.",
                RelatedTools = ["list_tool_groups", "get_tool_help"],
            },
            ["get_mcp_server_info"] = new()
            {
                Workflow = "Use after publish/reload to confirm the binary path, profile, startup groups, dynamic groups, and current tool count.",
            },
            ["load_workspace"] = new()
            {
                Prerequisites = "workspacePath must be a .sln, .slnx, or .csproj file, not a directory.",
                Workflow = "Call this before C# analysis. Prefer a solution file for multi-config repos. Pass targetFramework when the project uses TargetFrameworks. run_dotnet_build and run_dotnet_test inherit configuration/platform when omitted. Optional buildArgs is a session suffix for later `dotnet build` (probe and pre-test build). On a large .sln pass briefOutput=true to collapse MSBuild/NuGet warnings; failures still print in full. logProjectOutputDiagnostics=true logs per-project OutputFilePath/GeneratedFilesOutputDirectory and AnalyzerReference existence/timestamp to the MCP log — use when an analyzer/generator project is not producing generated sources under a Directory.Build.props that overrides OutputPath. shadowCopyInSolutionAnalyzers=true fixes that case: rewrites the broken AnalyzerReference to a shadow copy of the referenced project's own resolved output (requires that project already built once).",
                Pitfalls = "Host abort mid-load is a client timeout, not an MSBuild failure. Restore/design-time warnings do not fail load; NU/MSB/NETSDK errors do. A changed .csproj/.sln does not auto-reopen MSBuild — call again or reset_workspace. Unsaved editor buffers are ignored. logProjectOutputDiagnostics output goes only to the server log (tail_tool_log / read_log_tail), not to this tool's return value. shadowCopyInSolutionAnalyzers only rewrites references whose file name matches an unambiguous in-solution project AssemblyName; it does not build anything itself.",
                RelatedTools = ["reset_workspace", "run_dotnet_build", "run_dotnet_test"],
            },
            ["reset_workspace"] = new()
            {
                Workflow = "Use after building so the next load_workspace picks up generated obj files and project-graph changes.",
                Pitfalls = "Saved .cs edits sync without reset. Do not use this to restart a rebuilt MCP binary — use stop_mcp_server.",
                RelatedTools = ["load_workspace", "stop_mcp_server"],
            },
            ["get_code_skeleton"] = new()
            {
                Workflow = "Pass a .cs file or a directory (recursive, skips bin/obj/Test/Tests, at most 20 files). No workspace required.",
                Pitfalls = "Does not accept assemblyName/typeName. Method bodies are stripped.",
                RelatedTools = ["get_class_skeleton", "get_decompiled_class_skeleton"],
            },
            ["get_class_skeleton"] = new()
            {
                Prerequisites = "Requires load_workspace and a document in that workspace.",
                RelatedTools = ["get_code_skeleton", "get_decompiled_class_skeleton"],
            },
            ["get_diagnostics_for_file"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                Workflow = "Use the returned id/line/column with get_code_fixes.",
                RelatedTools = ["get_code_fixes", "apply_code_fix"],
            },
            ["find_symbol_definition"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                Workflow = "Pass the exact identifier. Matching is case-insensitive. Use this for where a type or member is declared.",
                Pitfalls = "Do not use text search or a terminal grep for declarations. Unsaved editor buffers are ignored.",
                RelatedTools = ["find_usages", "find_symbol_references", "search_code"],
            },
            ["find_usages"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                Workflow = "Solution-wide search by declared simple name. If several declarations share a name, one primary symbol is chosen (types preferred).",
                Pitfalls = "Not for interface/base hierarchy. Output is capped.",
                RelatedTools = ["find_symbol_definition", "find_symbol_references", "find_implementations"],
            },
            ["find_symbol_references"] = new()
            {
                Prerequisites = "Requires load_workspace and the declaring .cs file.",
                RelatedTools = ["find_usages", "find_symbol_definition"],
            },
            ["find_implementations"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                Workflow = "Use for which types implement an interface or derive from a base. Transitive by default.",
                Pitfalls = "Do not use find_usages or text search for this — they miss indirect hierarchy.",
                RelatedTools = ["find_usages", "find_symbol_definition"],
            },
            ["get_call_graph"] = new()
            {
                Prerequisites = "Requires load_workspace.",
            },
            ["run_dotnet_build"] = new()
            {
                Prerequisites = "workspacePath must be a .csproj, .sln, or .slnx file, not a directory.",
                Workflow = "Use after edits to verify compile. Omit configuration/platform to inherit load_workspace. Extra `dotnet build` args inherit from load_workspace `buildArgs`. Default noIncremental=true so up-to-date cache cannot hide errors. Pass projectName with a .sln/.slnx workspacePath to build one project via its solution-folder MSBuild target (`-t`). Configuration on build steps is `-p:Configuration`.",
                Pitfalls = "Do not use execute_dotnet_command for ordinary builds. Restore success cannot mask a failed build. projectName requires a .sln/.slnx, not a .csproj. Ambiguous names need the virtual path (Folder\\Project).",
                RelatedTools = ["run_dotnet_test", "execute_dotnet_command", "load_workspace"],
            },
            ["run_dotnet_test"] = new()
            {
                Workflow = "Runs the full suite. Directories are allowed (unlike run_dotnet_build). Omit configuration/platform to inherit load_workspace. Pre-test `dotnet build` also inherits load_workspace `buildArgs`. After a successful build, pass noBuild=true. Optional binariesPath is a bin directory: loaded .sln/.slnx plus a .csproj workspacePath; runs AssemblyName.dll from that directory. When noBuild=false, builds that project via the loaded solution `-t` first.",
                Pitfalls = "For one class or method use run_specific_test. For a raw VSTest --filter use run_test_by_filter. Do not hand-write filters via execute_dotnet_command. binariesPath is a directory, not a DLL path; the DLL must sit directly in it, with .runtimeconfig.json / .deps.json beside it.",
                RelatedTools = ["run_specific_test", "run_test_by_filter", "run_dotnet_build", "execute_dotnet_command"],
            },
            ["run_specific_test"] = new()
            {
                Workflow = "Provide className and/or methodName. Prefer simple class name plus short method name. The tool builds a VSTest-safe filter internally. Optional binariesPath is a bin directory: loaded .sln/.slnx plus a .csproj workspacePath; runs AssemblyName.dll from that directory. When noBuild=false, builds that project via the loaded solution `-t` first.",
                Pitfalls = "Do not use execute_dotnet_command. For TestCategory or a raw FullyQualifiedName expression use run_test_by_filter. At least one of className or methodName is required. binariesPath is a directory, not a DLL path; the DLL must sit directly in it, with .runtimeconfig.json / .deps.json beside it.",
                RelatedTools = ["run_dotnet_test", "run_test_by_filter", "get_test_list", "execute_dotnet_command"],
            },
            ["run_test_by_filter"] = new()
            {
                Workflow = "Pass a raw VSTest --filter (FullyQualifiedName~MyClass, TestCategory=Smoke). Default noBuild=true. Optional binariesPath is a bin directory: loaded .sln/.slnx plus a .csproj workspacePath; the tool runs the AssemblyName.dll found there. When noBuild=false, builds that project via the loaded solution `-t` first.",
                Pitfalls = "Do not put method () in the filter. Empty filter is an error. binariesPath is a directory, not a DLL path; the DLL must sit directly in it, with .runtimeconfig.json / .deps.json beside it. Prefer run_specific_test for one class or method.",
                RelatedTools = ["run_specific_test", "run_dotnet_test", "run_dotnet_build", "execute_dotnet_command"],
            },
            ["get_changed_files"] = new()
            {
                Workflow = "Lists git changed/untracked files. Does not return diffs — use host git tools for patches.",
                RelatedTools = ["run_specific_test"],
            },
            ["get_file_content"] = new()
            {
                Workflow = "Reads disk, not the workspace. Large files are truncated. For a window use read_file_range; for a method use get_method_body.",
                RelatedTools = ["read_file_range", "get_method_body"],
            },
            ["read_file_range"] = new()
            {
                Workflow = "startLine is 1-based. Returns original line numbers.",
                RelatedTools = ["get_file_content"],
            },
            ["search_code"] = new()
            {
                Workflow = "Text/regex search over source files. No workspace required. Default root is loaded workspace or process CWD; default extension is .cs.",
                Pitfalls = "Not for finding where a symbol is declared — use find_symbol_definition. Skips bin, obj, .git, and .vs.",
                RelatedTools = ["find_symbol_definition", "find_usages"],
            },
            ["list_directory_tree"] = new()
            {
                Workflow = "Relative directoryPath uses process CWD. Skips bin, obj, .git, and .vs.",
            },
            ["get_method_body"] = new()
            {
                Workflow = "Reads disk. First matching method wins; use update_method_body with parameterTypes when overloads matter.",
                RelatedTools = ["update_method_body", "get_file_content"],
            },
            ["update_file_content"] = new()
            {
                Pitfalls = "Overwrites the entire file. Creates missing parent directories. Prefer AST tools for structured C# edits.",
                RelatedTools = ["apply_patch", "add_method_to_class"],
            },
            ["apply_patch"] = new()
            {
                Workflow = "The only search-and-replace tool. Default replaceAll=false. Tries exact match, then whitespace-tolerant matching.",
                Pitfalls = "String literals with internal spaces may not match the flexible fallback. Prefer AST tools for usings and member inserts.",
                RelatedTools = ["update_file_content", "add_using", "update_method_body"],
            },
            ["add_using"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                Workflow = "Prefer this over apply_patch for imports.",
                RelatedTools = ["remove_using", "organize_usings", "apply_patch"],
            },
            ["remove_using"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                RelatedTools = ["add_using", "organize_usings"],
            },
            ["organize_usings"] = new()
            {
                Prerequisites = "Requires load_workspace.",
            },
            ["add_method_to_class"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                Workflow = "Pass a full method declaration. Prefer this over apply_patch.",
                RelatedTools = ["update_method_body", "generate_test_method_stub"],
            },
            ["update_method_body"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                Workflow = "Pass statements or a full block. Use parameterTypes when overloads exist.",
                RelatedTools = ["get_method_body", "add_method_to_class"],
            },
            ["add_property_to_class"] = new()
            {
                Prerequisites = "Requires load_workspace.",
            },
            ["add_field_to_class"] = new()
            {
                Prerequisites = "Requires load_workspace.",
            },
            ["remove_member"] = new()
            {
                Prerequisites = "Requires load_workspace.",
            },
            ["add_type_to_class_bases"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                RelatedTools = ["implement_interface"],
            },
            ["implement_interface"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                Workflow = "Adds the interface and NotImplemented stubs for missing members.",
                RelatedTools = ["add_type_to_class_bases", "extract_interface"],
            },
            ["get_code_fixes"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                Workflow = "Call get_diagnostics_for_file first. Use the returned fixIndex with apply_code_fix.",
                RelatedTools = ["get_diagnostics_for_file", "apply_code_fix"],
            },
            ["apply_code_fix"] = new()
            {
                Prerequisites = "Requires load_workspace. Call get_code_fixes first.",
                Pitfalls = "previewOnly=true returns a diff without writing.",
                RelatedTools = ["get_code_fixes"],
            },
            ["extract_interface"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                Pitfalls = "previewOnly=true writes nothing. Default createNewFile=true.",
                RelatedTools = ["implement_interface", "move_type_to_new_file"],
            },
            ["move_type_to_new_file"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                Workflow = "When typeName is omitted, moves every top-level type whose name does not match the current file name.",
                Pitfalls = "previewOnly=true writes nothing.",
            },
            ["run_format"] = new()
            {
                Workflow = "Runs dotnet format. Directories are allowed. verifyOnly checks without writing.",
                RelatedTools = ["execute_dotnet_command"],
            },
            ["rename_symbol"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                Pitfalls = "Default previewOnly=true — set false to write. Does not rename project folders; use rename_project for that.",
                RelatedTools = ["rename_project", "find_symbol_references"],
            },
            ["generate_test_method_stub"] = new()
            {
                Prerequisites = "Requires load_workspace.",
                RelatedTools = ["add_method_to_class", "run_specific_test"],
            },
            ["explore_assembly"] = new()
            {
                Prerequisites = "load_workspace is required when using assemblyName only.",
                Workflow = "Pass assemblyName without .dll, or assemblyPath to a dll. Lists visible top-level types.",
                RelatedTools = ["decompile_type", "get_decompiled_class_skeleton"],
            },
            ["decompile_type"] = new()
            {
                Prerequisites = "load_workspace is required when using assemblyName only.",
                Workflow = "For large types use get_decompiled_class_skeleton or get_decompiled_method_body instead.",
                RelatedTools = ["explore_assembly", "get_decompiled_class_skeleton", "get_decompiled_method_body", "get_class_skeleton"],
            },
            ["get_decompiled_class_skeleton"] = new()
            {
                Prerequisites = "load_workspace is required when using assemblyName only.",
                Workflow = "Signatures only, no method bodies. Use for NuGet/DLL types, not workspace source.",
                RelatedTools = ["get_class_skeleton", "get_decompiled_method_body", "decompile_type"],
            },
            ["get_decompiled_method_body"] = new()
            {
                Prerequisites = "load_workspace is required when using assemblyName only.",
                Workflow = "Returns all overloads matching methodName.",
                RelatedTools = ["get_decompiled_class_skeleton", "get_method_body"],
            },
            ["list_nuget_packages"] = new()
            {
                Workflow = "Use before adding or upgrading packages. Do not guess installed versions.",
                RelatedTools = ["search_nuget_registry", "list_outdated_packages", "run_nuget_audit"],
            },
            ["run_nuget_audit"] = new()
            {
                Workflow = "Separate from compile errors in run_dotnet_build.",
                RelatedTools = ["list_nuget_packages"],
            },
            ["list_outdated_packages"] = new()
            {
                RelatedTools = ["list_nuget_packages", "search_nuget_registry"],
            },
            ["search_nuget_registry"] = new()
            {
                Workflow = "Verify package id and version before add_package_reference. Do not invent ids or versions.",
                RelatedTools = ["add_package_reference", "list_nuget_packages"],
            },
            ["add_package_reference"] = new()
            {
                Workflow = "Verify id/version with search_nuget_registry first. Call load_workspace after because the in-memory workspace is cleared.",
                RelatedTools = ["search_nuget_registry", "remove_package_reference"],
            },
            ["remove_package_reference"] = new()
            {
                Workflow = "Call load_workspace after because the in-memory workspace is cleared.",
                RelatedTools = ["add_package_reference"],
            },
            ["list_projects"] = new()
            {
                Prerequisites = "Requires a loaded workspace, or pass workspacePath to load one.",
                RelatedTools = ["get_project_graph", "load_workspace"],
            },
            ["get_project_graph"] = new()
            {
                Prerequisites = "Requires a loaded workspace, or pass workspacePath to load one.",
                RelatedTools = ["list_projects"],
            },
            ["rename_project"] = new()
            {
                Pitfalls = "Default dryRun=true. Does not rename C# namespaces/types — use rename_symbol after reload.",
                RelatedTools = ["rename_symbol"],
            },
            ["run_dotnet_run"] = new()
            {
                Workflow = "Runs an executable csproj. Prefer this over execute_dotnet_command or a raw shell.",
                RelatedTools = ["execute_dotnet_command", "run_dotnet_build"],
            },
            ["execute_dotnet_command"] = new()
            {
                Workflow = "Raw `dotnet {command}` with truncated stdout/stderr. Prefer specialized build/test/run tools.",
                Pitfalls = "No structured diagnostic parser. Do not use this for ordinary build, test, or filtered tests.",
                RelatedTools = ["run_dotnet_build", "run_dotnet_test", "run_specific_test", "run_test_by_filter", "run_dotnet_run"],
            },
            ["get_test_list"] = new()
            {
                Prerequisites = "Requires load_workspace on the test solution.",
                Workflow = "Optional projectName limits discovery to one loaded Roslyn project (display name, file name, or assembly). Optional nameContains is a case-insensitive substring of the VSTest FQN (namespace, class, method). Both filters run before maxResults.",
                Pitfalls = "Unfiltered count 0 usually means the wrong project was loaded. A filtered count 0 means no match — drop projectName/nameContains before assuming the wrong .sln. Unknown or ambiguous projectName returns the project list, not an empty JSON. Project match is exact, not Contains.",
                RelatedTools = ["run_specific_test", "run_test_by_filter", "list_projects"],
            },
            ["read_log_tail"] = new()
            {
                Workflow = "Omit filePath to read the latest MCP server log. Same default as tail_tool_log.",
                RelatedTools = ["tail_tool_log"],
            },
            ["tail_tool_log"] = new()
            {
                Workflow = "Shortcut for the latest logs/mcp-*.log file.",
                RelatedTools = ["read_log_tail"],
            },
            ["manage_agent_scratchpad"] = new()
            {
                Workflow = "Actions: read, write, append, clear. File lives at .agent_memory/scratchpad.md under process CWD.",
                Pitfalls = "Writes files. CWD may be the repo root or the user profile depending on how the server was started.",
            },
            ["stop_mcp_server"] = new()
            {
                Workflow = "Stops this process after the tool returns so a rebuilt binary can be reloaded.",
                Pitfalls = "Do not use this to refresh saved .cs — they sync automatically. For obj/.csproj graph: reset_workspace then load_workspace.",
                RelatedTools = ["reset_workspace"],
            },
        };
}
