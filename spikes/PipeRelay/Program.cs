using ModelContextProtocol.Server;

namespace PipeRelay;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var mode = args.FirstOrDefault() ?? "auto";
            if (mode == "verify")
            {
                return await SpikeChecks.Run(args.ElementAtOrDefault(1) ?? "all");
            }

            using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var pipe = args.ElementAtOrDefault(1) ?? "pr-" + BuildIdentity.Value[..16];
            var ready = args.ElementAtOrDefault(2);
            if (mode is not ("auto" or "direct" or "host"))
            {
                throw new ArgumentException("Expected auto, host, direct or verify.");
            }

            var state = mode == "host" || mode == "direct" || args.Contains("--local-host") ? new CounterState() : null;
            using var listenerLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var listener = mode == "host" || args.Contains("--local-host")
                ? new CounterHost(state!, pipe).Run(ready, listenerLifetime.Token)
                : Task.CompletedTask;
            if (mode == "host")
            {
                await listener;
                return 0;
            }

            await using var facade = new Facade(state, pipe);
            var options = new McpServerOptions
            {
                ServerInfo = new() { Name = "PipeRelay", Version = "0.1" },
                Capabilities = new() { Tools = new() { ListChanged = true } },
                ToolCollection = facade.Tools,
            };
            await using var server = McpServer.Create(
                new StreamServerTransport(Console.OpenStandardInput(), Console.OpenStandardOutput()), options);
            try
            {
                await server.RunAsync(lifetime.Token);
            }
            finally
            {
                await listenerLifetime.CancelAsync();
                await listener;
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }
}
