using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace RoslynMcpServer.Hosting;

/// <summary>
/// Process-wide runtime tool activation. The current host uses stdio (one session per process).
/// The live SDK tool collection is shared for that process; a future HTTP/multi-session host
/// must not treat this singleton as per-session state.
/// </summary>
public sealed class McpToolActivationService
{
    private readonly object _gate = new();
    private readonly McpToolSurface _surface;
    private readonly McpRuntimeToolCollection _tools;
    private readonly IServiceProvider _services;
    private readonly ILogger<McpToolActivationService> _logger;
    private readonly HashSet<string> _dynamicGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _dynamicGroupsOrder = [];

    public McpToolActivationService(
        McpToolSurface surface,
        McpRuntimeToolCollection tools,
        IServiceProvider services,
        ILogger<McpToolActivationService> logger)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Profile => _surface.Profile;

    public IReadOnlyList<string> StartupGroups => _surface.StartupGroups;

    public int StartupToolCount => _surface.RegisteredToolCount;

    public IReadOnlyList<string> DynamicGroups
    {
        get
        {
            lock (_gate)
            {
                return _dynamicGroupsOrder.ToArray();
            }
        }
    }

    public IReadOnlyList<string> ActiveGroups
    {
        get
        {
            lock (_gate)
            {
                return ComputeActiveGroupsUnlocked();
            }
        }
    }

    public int CurrentToolCount
    {
        get
        {
            lock (_gate)
            {
                return _tools.Count > 0 ? _tools.Count : _surface.RegisteredToolCount;
            }
        }
    }

    public string FormatStartupGroupsDisplay() => FormatGroupDisplay(_surface.StartupGroups);

    public string FormatStartupGroupsMarkdown() => FormatGroupMarkdown(_surface.StartupGroups);

    public string FormatDynamicGroupsDisplay() => FormatGroupDisplay(DynamicGroups);

    public string FormatDynamicGroupsMarkdown() => FormatGroupMarkdown(DynamicGroups);

    public string FormatActiveGroupsDisplay() => FormatGroupDisplay(ActiveGroups);

    public bool IsToolActive(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            if (_tools.TryGetPrimitive(name, out _))
            {
                return true;
            }

            if (_tools.Count == 0)
            {
                return _surface.RegisteredTools.Any(d => d.Name.Equals(name, StringComparison.Ordinal));
            }

            return false;
        }
    }

    public McpToolEnableResult EnableGroup(string? group)
    {
        if (!McpToolCatalog.TryNormalizeGroup(group, out var canonical))
        {
            _logger.LogInformation("Rejected unknown MCP tool group '{Group}'", group?.Trim());
            return new McpToolEnableResult
            {
                Changed = false,
                Markdown = FormatUnknownGroup(group),
            };
        }

        lock (_gate)
        {
            if (_surface.Profile == McpToolProfileOptions.FullProfile)
            {
                return new McpToolEnableResult
                {
                    Changed = false,
                    Markdown = FormatFullNoOp(canonical),
                };
            }

            EnsureStartupToolsUnlocked();

            var missing = new List<McpServerTool>();
            var addedNames = new List<string>();
            foreach (var descriptor in McpToolCatalog.All.Where(d => d.Group == canonical))
            {
                if (_tools.TryGetPrimitive(descriptor.Name, out _))
                {
                    continue;
                }

                missing.Add(descriptor.CreateFactory()(_services));
                addedNames.Add(descriptor.Name);
            }

            if (missing.Count == 0)
            {
                return new McpToolEnableResult
                {
                    Changed = false,
                    Markdown = FormatAlreadyActive(canonical),
                };
            }

            var added = _tools.TryAddMany(missing);
            if (added > 0 && _dynamicGroups.Add(canonical))
            {
                _dynamicGroupsOrder.Add(canonical);
            }

            _logger.LogInformation(
                "Enabled MCP tool group {Group}; added {Count} tools ({Tools})",
                canonical,
                added,
                string.Join(", ", addedNames));

            return new McpToolEnableResult
            {
                Changed = added > 0,
                NewlyEnabledTools = addedNames,
                Markdown = FormatEnabled(canonical, addedNames),
            };
        }
    }

    private void EnsureStartupToolsUnlocked()
    {
        if (_tools.Count >= _surface.RegisteredToolCount)
        {
            return;
        }

        var missing = new List<McpServerTool>();
        foreach (var descriptor in _surface.RegisteredTools)
        {
            if (_tools.TryGetPrimitive(descriptor.Name, out _))
            {
                continue;
            }

            missing.Add(descriptor.CreateFactory()(_services));
        }

        if (missing.Count > 0)
        {
            _tools.TryAddMany(missing);
        }
    }

    private IReadOnlyList<string> ComputeActiveGroupsUnlocked()
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        if (_tools.Count == 0)
        {
            foreach (var tool in _surface.RegisteredTools)
            {
                active.Add(tool.Group);
            }
        }
        else
        {
            foreach (var tool in _tools)
            {
                var descriptor = McpToolCatalog.All.FirstOrDefault(d => d.Name.Equals(tool.ProtocolTool.Name, StringComparison.Ordinal));
                if (descriptor is not null)
                {
                    active.Add(descriptor.Group);
                }
            }
        }

        foreach (var group in _dynamicGroupsOrder)
        {
            active.Add(group);
        }

        return McpToolGroups.All.Where(active.Contains).ToArray();
    }

    private string FormatEnabled(string group, IReadOnlyList<string> tools) =>
        $"""
        # enable_tool_group

        Enabled `{group}`. New tools: {FormatToolNameList(tools)}.

        Active groups: {FormatGroupMarkdown(ComputeActiveGroupsUnlocked())}.

        Restart with ROSLYN_MCP_TOOL_GROUPS={group}
        """;

    private string FormatAlreadyActive(string group) =>
        $"""
        # enable_tool_group

        Group `{group}` is already active. No tools added.

        Active groups: {FormatGroupMarkdown(ComputeActiveGroupsUnlocked())}.

        Restart with ROSLYN_MCP_TOOL_GROUPS={group}
        """;

    private string FormatFullNoOp(string group) =>
        $"""
        # enable_tool_group

        Profile `full` already registers every tool. Enabling `{group}` is a no-op.

        Active groups: {FormatGroupMarkdown(ComputeActiveGroupsUnlocked())}.

        Restart with ROSLYN_MCP_TOOL_GROUPS={group}
        """;

    private static string FormatUnknownGroup(string? group)
    {
        var shown = string.IsNullOrWhiteSpace(group) ? "(empty)" : $"`{group.Trim()}`";
        var valid = string.Join(", ", McpToolGroups.All.Select(g => $"`{g}`"));
        return
            $"Unknown group {shown}. Valid groups: {valid}. Call `list_tool_groups`.";
    }

    private static string FormatToolNameList(IReadOnlyList<string> tools) =>
        tools.Count == 0 ? "(none)" : string.Join(", ", tools.Select(n => $"`{n}`"));

    private static string FormatGroupDisplay(IReadOnlyList<string> groups) =>
        groups.Count == 0 ? "(none)" : string.Join(", ", groups);

    private static string FormatGroupMarkdown(IReadOnlyList<string> groups) =>
        groups.Count == 0 ? "(none)" : $"`{string.Join(", ", groups)}`";
}
