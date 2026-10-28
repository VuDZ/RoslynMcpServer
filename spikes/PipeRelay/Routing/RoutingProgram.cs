using System.Text.Json;
using ModelContextProtocol.Server;
using PipeRelay.Routing.Models;
using PipeRelay.Transport;

namespace PipeRelay.Routing;

internal static class RoutingProgram
{
    public static async Task<int> Run(string mode, string optionsFile)
    {
        var options = JsonSerializer.Deserialize<RouteOptions>(await File.ReadAllTextAsync(optionsFile))!;
        var files = new RouteFiles(options);
        if (mode == "routed-host")
        {
            try
            {
                using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await RouteFiles.Barrier(options.BeforeLeaseBarrier, startup.Token);
                await new DedicatedHost(options, files).Run();
            }
            catch (Exception exception)
            {
                files.Event("fatal", Guid.Empty, exception.ToString());
                return 1;
            }

            return 0;
        }

        await using var session = new RoutingSession(options);
        await using var facade = new Facade(null, files.PipeName, session);
        using var lifetime = new CancellationTokenSource();
        // MCP 2026 subscriptions can keep RunAsync pending after EOF; tie their cancellation to the input lifetime.
        await using var input = new BoundedStream(Console.OpenStandardInput(), endOfInput: () => lifetime.Cancel());
        await using var server = McpServer.Create(new StreamServerTransport(
            input, Console.OpenStandardOutput()), new()
        {
            ServerInfo = new() { Name = "routing-facade", Version = "0.1" },
            Capabilities = new() { Tools = new() { ListChanged = true } },
            ToolCollection = facade.Tools,
        });
        try
        {
            await server.RunAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        files.Event("frontend-eof", Guid.Empty);
        return 0;
    }
}
