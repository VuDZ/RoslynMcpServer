using System.IO.Pipes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Client;
using PipeRelay.Routing.Models;
using PipeRelay.Transport;

namespace PipeRelay.Routing;

internal sealed class RouteConnection(McpClient client, RouteOptions options, Guid generation) : IAsyncDisposable
{
    public McpClient Client { get; } = client;
    public RouteOptions Options { get; } = options;
    public Guid Generation { get; } = generation;
    public bool Failed { get; set; }

    public static async Task<RouteConnection> Connect(RouteOptions options, RouteFiles files,
        CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", files.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(cancellationToken);
            await LocalHandshake.WriteValue(pipe,
                new RouteHello(BuildIdentity.Value, files.Key, options.Descriptor, Guid.Empty), cancellationToken);
            var reply = await LocalHandshake.ReadValue<RouteHello>(pipe, cancellationToken);
            if (reply.Identity != BuildIdentity.Value || reply.Key != files.Key
                || reply.Descriptor != options.Descriptor || reply.Generation == Guid.Empty)
            {
                throw new InvalidDataException("Incompatible descriptor or generation.");
            }

            var stream = new BoundedStream(pipe);
            var client = await McpClient.CreateAsync(new StreamClientTransport(stream, stream),
                new() { ProtocolVersion = "2025-11-25", InitializationTimeout = TimeSpan.FromSeconds(3) },
                cancellationToken: cancellationToken);
            return new(client, options, reply.Generation);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => Client.DisposeAsync();
}
