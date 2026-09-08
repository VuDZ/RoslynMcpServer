using System.ComponentModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class ServerLifecycleTools
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly SolutionManager _solutionManager;
    private readonly ILogger<ServerLifecycleTools> _logger;
    private readonly McpToolActivationService _activation;

    public ServerLifecycleTools(
        IHostApplicationLifetime lifetime,
        SolutionManager solutionManager,
        ILogger<ServerLifecycleTools> logger,
        McpToolActivationService activation)
    {
        _lifetime = lifetime;
        _solutionManager = solutionManager;
        _logger = logger;
        _activation = activation;
    }

    [McpServerTool(Name = "get_mcp_server_info", Title = "Get MCP server info")]
    [Description("Returns binary path, registered tool count, log location, and workspace state.")]
    public Task<string> GetMcpServerInfo(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var info = McpServerInfoHelper.BuildInfoMarkdown(_solutionManager.GetCurrentSolution(), _activation);
        return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(GetMcpServerInfo), info));
    }

    [McpServerTool(Name = "stop_mcp_server", Title = "Stop MCP server")]
    [Description(
        "Stops this MCP server process after the tool returns. Use after rebuilding the server binary, not to refresh saved .cs.")]
    public Task<string> StopMcpServer(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        _logger.LogInformation("stop_mcp_server invoked; scheduling host shutdown.");
        _ = StopAfterResponseAsync();
        var message =
            "## MCP server stopping\n\n"
            + "This process will exit shortly so the host can rebuild binaries without file locks and restart MCP.\n\n"
            + "**Suggested workflow**\n"
            + "1. Wait until MCP tools disconnect.\n"
            + "2. From a terminal: `dotnet build` on RoslynMcpServer (or your solution).\n"
            + "3. In Cursor: MCP → Restart for this server (or reload the window).\n\n"
            + "**Without killing the process:** saved `.cs` already sync into symbol search. "
            + "For generated `obj` / `.csproj` graph only: `reset_workspace`, then `load_workspace`.\n";

        return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(StopMcpServer), message.TrimEnd()));
    }

    private async Task StopAfterResponseAsync()
    {
        try
        {
            await Task.Delay(150, CancellationToken.None).ConfigureAwait(false);
            _lifetime.StopApplication();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop host gracefully; forcing exit.");
            Environment.Exit(1);
        }
    }
}
