using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using RoslynMcpServer.Tools;

namespace RoslynMcpServer.Hosting;

/// <summary>
/// Single source of truth for MCP tools: names, host methods, groups, lite-core membership,
/// and read/write/process classification. Drives DI host registration, SDK tool registration,
/// profile selection, counts, tests, and help generation.
/// </summary>
public static class McpToolCatalog
{
    /// <summary>
    /// Public names reserved for later epochs. Empty after Epoch 3 registered <c>enable_tool_group</c>.
    /// </summary>
    public static IReadOnlyList<string> ReservedBootstrapToolNames { get; } = [];

    /// <summary>
    /// Epoch 1 minified <c>tools/list</c> UTF-8 sizes captured before Epoch 2 description compaction.
    /// </summary>
    public const int Epoch1FullCatalogBytes = 64181;

    public const int Epoch1LiteCatalogBytes = 23115;

    /// <summary>
    /// Epoch 1 tool+parameter <see cref="DescriptionAttribute"/> character total for the original 59 tools.
    /// Recapture with <see cref="SumDescriptionCharacters"/> on that set if the catalog is rebuilt.
    /// </summary>
    public const int Epoch1DescriptionCharacters = 35445;

    public static IReadOnlyList<McpToolDescriptor> All { get; } = Build();

    public static IReadOnlyList<Type> HostTypes { get; } =
        All.Select(d => d.HostType).Distinct().ToArray();

    public static McpToolSurface CreateSurface(McpToolProfileOptions? options = null)
    {
        options ??= new McpToolProfileOptions();
        var profile = NormalizeProfile(options.Profile);
        var startupGroups = ParseStartupGroups(options.Groups);

        IReadOnlyList<McpToolDescriptor> selected = profile == McpToolProfileOptions.FullProfile
            ? All
            : SelectLite(startupGroups);

        return new McpToolSurface
        {
            Profile = profile,
            StartupGroups = startupGroups,
            RegisteredTools = selected,
        };
    }

    public static McpToolSurface CreateSurfaceFromEnvironment() =>
        CreateSurface(McpToolProfileOptions.FromEnvironment());

    private static IReadOnlyList<McpToolDescriptor> SelectLite(IReadOnlyList<string> startupGroups)
    {
        var extra = startupGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        extra.Remove(McpToolGroups.Core);
        return All
            .Where(d => d.InLiteCore || extra.Contains(d.Group))
            .ToArray();
    }

    private static string NormalizeProfile(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return McpToolProfileOptions.FullProfile;
        }

        var trimmed = raw.Trim();
        if (trimmed.Equals(McpToolProfileOptions.FullProfile, StringComparison.OrdinalIgnoreCase))
        {
            return McpToolProfileOptions.FullProfile;
        }

        if (trimmed.Equals(McpToolProfileOptions.LiteProfile, StringComparison.OrdinalIgnoreCase))
        {
            return McpToolProfileOptions.LiteProfile;
        }

        throw new InvalidOperationException(
            $"Unknown tool profile '{trimmed}'. Valid profiles: {McpToolProfileOptions.FullProfile}, {McpToolProfileOptions.LiteProfile}. "
            + $"Set {McpToolProfileOptions.ProfileVariableName} or omit it for {McpToolProfileOptions.FullProfile}.");
    }

    private static IReadOnlyList<string> ParseStartupGroups(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var canonical = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == 0)
            {
                continue;
            }

            if (!TryNormalizeGroup(token, out var group))
            {
                throw new InvalidOperationException(
                    $"Unknown tool group '{token}'. Valid groups: {string.Join(", ", McpToolGroups.All)}. "
                    + $"Set {McpToolProfileOptions.GroupsVariableName} to a comma-separated subset.");
            }

            if (seen.Add(group))
            {
                canonical.Add(group);
            }
        }

        return canonical;
    }

    public static bool TryNormalizeGroup(string? token, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? group)
    {
        group = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trimmed = token.Trim();
        foreach (var candidate in McpToolGroups.All)
        {
            if (candidate.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                group = candidate;
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<McpToolDescriptor> Build()
    {
        McpToolDescriptor[] entries =
        [
            Core<ServerLifecycleTools>("get_mcp_server_info", readOnly: true),
            Core<ToolHelpTools>("list_tool_groups", readOnly: true),
            Core<ToolHelpTools>("get_tool_help", readOnly: true),
            Core<ToolHelpTools>("enable_tool_group", readOnly: false),
            Core<WorkspaceTools>("load_workspace", readOnly: false, executesProcess: true),
            Core<WorkspaceTools>("reset_workspace", readOnly: false),
            Core<CodeSkeletonTools>("get_code_skeleton", readOnly: true),
            Core<CodeAnalysisTools>("get_class_skeleton", readOnly: true),
            Core<CodeAnalysisTools>("get_diagnostics_for_file", readOnly: true),
            Core<NavigationTools>("find_symbol_definition", readOnly: true),
            Core<NavigationTools>("find_usages", readOnly: true),
            Core<NavigationTools>("find_symbol_references", readOnly: true),
            Core<NavigationTools>("find_implementations", readOnly: true),
            Core<NavigationTools>("get_call_graph", readOnly: true),
            Core<BuildTools>("run_dotnet_build", readOnly: false, executesProcess: true),
            Core<TestTools>("run_dotnet_test", readOnly: false, executesProcess: true),
            Core<TestTools>("run_specific_test", readOnly: false, executesProcess: true),
            Core<TestTools>("run_test_by_filter", readOnly: false, executesProcess: true),
            Core<UtilityTools>("get_changed_files", readOnly: true, executesProcess: true),

            Group<RoslynTools>("get_file_content", McpToolGroups.Files, readOnly: true),
            Group<UtilityTools>("read_file_range", McpToolGroups.Files, readOnly: true),
            Group<UtilityTools>("search_code", McpToolGroups.Files, readOnly: true),
            Group<UtilityTools>("list_directory_tree", McpToolGroups.Files, readOnly: true),
            Group<UtilityTools>("get_method_body", McpToolGroups.Files, readOnly: true),
            Group<EditingTools>("update_file_content", McpToolGroups.Files, readOnly: false),
            Group<UtilityTools>("apply_patch", McpToolGroups.Files, readOnly: false),

            Group<AstTools>("add_using", McpToolGroups.Editing, readOnly: false),
            Group<AstTools>("remove_using", McpToolGroups.Editing, readOnly: false),
            Group<AstTools>("organize_usings", McpToolGroups.Editing, readOnly: false),
            Group<AstTools>("add_method_to_class", McpToolGroups.Editing, readOnly: false),
            Group<AstTools>("update_method_body", McpToolGroups.Editing, readOnly: false),
            Group<AstTools>("add_property_to_class", McpToolGroups.Editing, readOnly: false),
            Group<AstTools>("add_field_to_class", McpToolGroups.Editing, readOnly: false),
            Group<AstTools>("remove_member", McpToolGroups.Editing, readOnly: false),
            Group<AstTools>("add_type_to_class_bases", McpToolGroups.Editing, readOnly: false),
            Group<AstTools>("implement_interface", McpToolGroups.Editing, readOnly: false),
            Group<CodeFixTools>("get_code_fixes", McpToolGroups.Editing, readOnly: true),
            Group<CodeFixTools>("apply_code_fix", McpToolGroups.Editing, readOnly: false),
            Group<RefactoringTools>("extract_interface", McpToolGroups.Editing, readOnly: false),
            Group<RefactoringTools>("move_type_to_new_file", McpToolGroups.Editing, readOnly: false),
            Group<UtilityTools>("run_format", McpToolGroups.Editing, readOnly: false, executesProcess: true),
            Group<UtilityTools>("rename_symbol", McpToolGroups.Editing, readOnly: false),
            Group<TestTools>("generate_test_method_stub", McpToolGroups.Editing, readOnly: false),

            Group<CodeAnalysisTools>("explore_assembly", McpToolGroups.Decompile, readOnly: true),
            Group<CodeAnalysisTools>("decompile_type", McpToolGroups.Decompile, readOnly: true),
            Group<CodeAnalysisTools>("get_decompiled_class_skeleton", McpToolGroups.Decompile, readOnly: true),
            Group<CodeAnalysisTools>("get_decompiled_method_body", McpToolGroups.Decompile, readOnly: true),

            Group<NuGetTools>("list_nuget_packages", McpToolGroups.NuGet, readOnly: true, executesProcess: true),
            Group<NuGetTools>("run_nuget_audit", McpToolGroups.NuGet, readOnly: true, executesProcess: true),
            Group<NuGetTools>("list_outdated_packages", McpToolGroups.NuGet, readOnly: true, executesProcess: true),
            Group<NuGetTools>("search_nuget_registry", McpToolGroups.NuGet, readOnly: true, executesProcess: true),
            Group<ProjectTools>("add_package_reference", McpToolGroups.NuGet, readOnly: false),
            Group<ProjectTools>("remove_package_reference", McpToolGroups.NuGet, readOnly: false),

            Group<UtilityTools>("list_projects", McpToolGroups.Project, readOnly: true),
            Group<UtilityTools>("get_project_graph", McpToolGroups.Project, readOnly: true),
            Group<ProjectTools>("rename_project", McpToolGroups.Project, readOnly: false),

            Group<RunTools>("run_dotnet_run", McpToolGroups.Runtime, readOnly: false, executesProcess: true),
            Group<UtilityTools>("execute_dotnet_command", McpToolGroups.Runtime, readOnly: false, executesProcess: true),
            Group<TestTools>("get_test_list", McpToolGroups.Runtime, readOnly: true),

            Group<UtilityTools>("read_log_tail", McpToolGroups.Operations, readOnly: true),
            Group<UtilityTools>("tail_tool_log", McpToolGroups.Operations, readOnly: true),
            Group<UtilityTools>("manage_agent_scratchpad", McpToolGroups.Operations, readOnly: false),
            Group<ServerLifecycleTools>("stop_mcp_server", McpToolGroups.Operations, readOnly: false, executesProcess: true),
        ];

        Validate(entries);
        return entries;
    }

    private static McpToolDescriptor Core<THost>(string name, bool readOnly, bool executesProcess = false)
        where THost : class =>
        Create<THost>(name, McpToolGroups.Core, inLiteCore: true, readOnly, executesProcess);

    private static McpToolDescriptor Group<THost>(string name, string group, bool readOnly, bool executesProcess = false)
        where THost : class =>
        Create<THost>(name, group, inLiteCore: false, readOnly, executesProcess);

    private static McpToolDescriptor Create<THost>(
        string name,
        string group,
        bool inLiteCore,
        bool readOnly,
        bool executesProcess)
        where THost : class
    {
        var hostType = typeof(THost);
        return new McpToolDescriptor
        {
            Name = name,
            HostType = hostType,
            Method = GetToolMethod(hostType, name),
            Group = group,
            InLiteCore = inLiteCore,
            IsReadOnly = readOnly,
            ExecutesProcess = executesProcess,
        };
    }

    public static int SumDescriptionCharacters(IEnumerable<McpToolDescriptor>? tools = null)
    {
        var total = 0;
        foreach (var descriptor in tools ?? All)
        {
            var methodDescription = descriptor.Method.GetCustomAttribute<DescriptionAttribute>()?.Description;
            if (!string.IsNullOrEmpty(methodDescription))
            {
                total += methodDescription.Length;
            }

            foreach (var parameter in descriptor.Method.GetParameters())
            {
                var parameterDescription = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;
                if (!string.IsNullOrEmpty(parameterDescription))
                {
                    total += parameterDescription.Length;
                }
            }
        }

        return total;
    }

    private static MethodInfo GetToolMethod(Type hostType, string toolName)
    {
        var matches = hostType
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m =>
            {
                var attr = m.GetCustomAttribute<McpServerToolAttribute>();
                if (attr is null)
                {
                    return false;
                }

                var name = attr.Name ?? m.Name;
                return string.Equals(name, toolName, StringComparison.Ordinal);
            })
            .ToArray();

        if (matches.Length == 0)
        {
            throw new InvalidOperationException(
                $"Tool catalog: '{toolName}' was not found on {hostType.FullName}.");
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Tool catalog: '{toolName}' is ambiguous on {hostType.FullName}.");
        }

        return matches[0];
    }

    private static void Validate(IReadOnlyList<McpToolDescriptor> entries)
    {
        var duplicateNames = entries
            .GroupBy(d => d.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicateNames.Length > 0)
        {
            throw new InvalidOperationException(
                "Tool catalog has duplicate public names: " + string.Join(", ", duplicateNames));
        }

        foreach (var reserved in ReservedBootstrapToolNames)
        {
            if (entries.Any(d => d.Name.Equals(reserved, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Tool catalog must not register reserved bootstrap name '{reserved}' in this epoch.");
            }
        }

        foreach (var entry in entries)
        {
            if (!McpToolGroups.All.Contains(entry.Group, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Tool '{entry.Name}' uses unknown group '{entry.Group}'.");
            }

            if (entry.InLiteCore != (entry.Group == McpToolGroups.Core))
            {
                throw new InvalidOperationException(
                    $"Tool '{entry.Name}' lite-core flag must match group '{McpToolGroups.Core}'.");
            }
        }

        var reflected = DiscoverAttributedTools();
        var catalogNames = entries.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        var reflectedNames = reflected.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);

        var missing = reflectedNames.Except(catalogNames, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "Tool catalog is missing attributed tools: " + string.Join(", ", missing));
        }

        var extra = catalogNames.Except(reflectedNames, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (extra.Length > 0)
        {
            throw new InvalidOperationException(
                "Tool catalog lists names with no [McpServerTool] method: " + string.Join(", ", extra));
        }
    }

    internal static IReadOnlyList<(string Name, Type HostType, MethodInfo Method)> DiscoverAttributedTools()
    {
        var toolsAssembly = typeof(WorkspaceTools).Assembly;
        var found = new List<(string Name, Type HostType, MethodInfo Method)>();
        foreach (var type in toolsAssembly.GetTypes())
        {
            if (!type.IsClass || type.IsAbstract)
            {
                continue;
            }

            foreach (var method in type.GetMethods(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr is null)
                {
                    continue;
                }

                found.Add((attr.Name ?? method.Name, type, method));
            }
        }

        return found;
    }
}
