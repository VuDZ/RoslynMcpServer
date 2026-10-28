using System.IO.Pipes;
using ModelContextProtocol.Server;
using PipeRelay.Transport;
using PipeRelay.Transport.Models;

namespace PipeRelay;

internal sealed class CounterHost(CounterState state, string pipeName)
{
    public async Task Run(string? readyFile, CancellationToken cancellationToken)
    {
        var connections = new List<Task>();
        var pipe = CreatePipe();
        try
        {
            if (readyFile is not null)
            {
                await File.WriteAllTextAsync(readyFile, Environment.ProcessId.ToString(), cancellationToken);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var connected = pipe;
                try
                {
                    await connected.WaitForConnectionAsync(cancellationToken);
                }
                catch (UnauthorizedAccessException)
                {
                    pipe = CreatePipe();
                    await connected.DisposeAsync();
                    Console.Error.WriteLine("A different OS identity was rejected before handshake.");
                    continue;
                }

                // Keep a listener instance alive before a short-lived connection can dispose the Unix shared socket.
                // Evidence: docs/workspace-host-process/epoch-0-design-and-spike/evidence/task-03-pipe-relay-spike.md#c03-02
                pipe = CreatePipe();
                connections.Add(Serve(connected, cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await pipe.DisposeAsync();
            await Task.WhenAll(connections);
        }
    }

    private NamedPipeServerStream CreatePipe() => new(pipeName, PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task Serve(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using var ownedPipe = pipe;
        try
        {
            using var handshakeDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshakeDeadline.CancelAfter(TimeSpan.FromSeconds(5));
            var request = await LocalHandshake.Read(pipe, handshakeDeadline.Token);
            LocalHandshake.Validate(request, pipeName);
            if (request.Generation != Guid.Empty)
            {
                throw new InvalidDataException("Client cannot choose a host generation.");
            }

            await LocalHandshake.Write(pipe,
                new Handshake(1, BuildIdentity.Value, pipeName, request.HarnessId, state.Generation), handshakeDeadline.Token);
            await using var stream = new BoundedStream(pipe);
            await using var server = McpServer.Create(new StreamServerTransport(stream, stream), new McpServerOptions
            {
                ServerInfo = new() { Name = "counter-host", Version = "0.1" },
                ToolCollection = state.Tools(),
            });
            await server.RunAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Local connection closed: {exception.GetType().Name}");
        }
    }
}
