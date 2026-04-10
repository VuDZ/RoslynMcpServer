using System.ComponentModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Tools;

public sealed class ServerLifecycleTools
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ServerLifecycleTools> _logger;

    public ServerLifecycleTools(IHostApplicationLifetime lifetime, ILogger<ServerLifecycleTools> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    [McpServerTool(Name = "stop_mcp_server", Title = "Stop MCP server")]
    [Description(
        "Gracefully stops this Roslyn MCP server process after the tool returns. Use when you rebuilt the server itself (or need a clean process): then run dotnet build from a terminal if needed and restart MCP in Cursor. For refreshing only the loaded C# solution in memory, prefer reset_workspace followed by load_workspace.")]
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
            + "**Without killing the process:** call `reset_workspace`, then `load_workspace` to reload the analyzed solution from disk.\n";

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
