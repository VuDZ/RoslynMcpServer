using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Plugins;

namespace RoslynMcpServer.Tools;

public sealed class ToolHelpTools
{
    private readonly McpToolActivationService _activation;
    private readonly IServiceProvider _services;

    public ToolHelpTools(McpToolActivationService activation, IServiceProvider services)
    {
        _activation = activation;
        _services = services;
    }

    [McpServerTool(Name = "list_tool_groups", Title = "List tool groups")]
    [Description("Lists tool groups with purpose, active state, member names, and startup fallback syntax. Does not enable groups.")]
    public Task<string> ListToolGroups(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(ToolTelemetry.TraceAndReturn(
            nameof(ListToolGroups),
            McpToolHelpFormatter.FormatGroups(_activation)));
    }

    [McpServerTool(Name = "get_tool_help", Title = "Get tool help")]
    [Description("Returns Markdown help for one tool: kind, group, live parameters, and focused pitfalls. Unknown names return close matches.")]
    public Task<string> GetToolHelp(
        [Description("Exact public tool name, for example find_usages.")]
        string toolName,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        // A plugin tool has no article of its own: its text comes from the accepted method of the start
        // report, which the start pass registered in this container.
        var pluginStartup = _services.GetService<PluginStartup.Report>() ?? PluginStartup.Report.Empty;
        return Task.FromResult(ToolTelemetry.TraceAndReturn(
            nameof(GetToolHelp),
            McpToolHelpFormatter.FormatToolHelp(toolName, _activation, pluginStartup)));
    }

    [McpServerTool(Name = "enable_tool_group", Title = "Enable tool group")]
    [Description("Adds every missing tool in one catalog group to this session and notifies via tools/list_changed. Idempotent; full profile is a no-op.")]
    public Task<string> EnableToolGroup(
        [Description("Exact group name, case-insensitive. Example: files.")]
        string group,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(ToolTelemetry.TraceAndReturn(
            nameof(EnableToolGroup),
            _activation.EnableGroup(group).Markdown));
    }
}
