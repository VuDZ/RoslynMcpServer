using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Hosting;

namespace RoslynMcpServer.Tools;

public sealed class ToolHelpTools
{
    private readonly McpToolSurface _toolSurface;

    public ToolHelpTools(McpToolSurface toolSurface)
    {
        _toolSurface = toolSurface;
    }

    [McpServerTool(Name = "list_tool_groups", Title = "List tool groups")]
    [Description("Lists tool groups with purpose, active state, member names, and startup fallback syntax. Does not enable groups.")]
    public Task<string> ListToolGroups(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(ToolTelemetry.TraceAndReturn(
            nameof(ListToolGroups),
            McpToolHelpFormatter.FormatGroups(_toolSurface)));
    }

    [McpServerTool(Name = "get_tool_help", Title = "Get tool help")]
    [Description("Returns Markdown help for one tool: kind, group, live parameters, and focused pitfalls. Unknown names return close matches.")]
    public Task<string> GetToolHelp(
        [Description("Exact public tool name, for example find_usages.")]
        string toolName,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(ToolTelemetry.TraceAndReturn(
            nameof(GetToolHelp),
            McpToolHelpFormatter.FormatToolHelp(toolName, _toolSurface)));
    }
}
