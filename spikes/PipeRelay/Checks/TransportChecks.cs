using System.IO.Pipes;
using PipeRelay.Transport;
using PipeRelay.Transport.Models;

namespace PipeRelay.Checks;

internal static class TransportChecks
{
    public static async Task<object> Run(ClientPair pair)
    {
        foreach (var request in new[]
        {
            new Handshake(2, BuildIdentity.Value, pair.PipeName, "check", Guid.Empty),
            new Handshake(1, "wrong-build", pair.PipeName, "check", Guid.Empty),
            new Handshake(1, BuildIdentity.Value, "wrong-key", "check", Guid.Empty),
            new Handshake(1, BuildIdentity.Value, pair.PipeName, "check", Guid.NewGuid()),
        })
        {
            await using var pipe = await Connect(pair.PipeName, pair.Token);
            await LocalHandshake.Write(pipe, request, pair.Token);
            await Closed(pipe, pair.Token);
        }

        await using (var pipe = await Connect(pair.PipeName, pair.Token))
        {
            await pipe.WriteAsync(new byte[] { 1, 16, 0, 0 }, pair.Token);
            await Closed(pipe, pair.Token);
        }

        await using (var pipe = await Connect(pair.PipeName, pair.Token))
        {
            await LocalHandshake.Write(pipe, new(1, BuildIdentity.Value, pair.PipeName, "check", Guid.Empty), pair.Token);
            var reply = await LocalHandshake.Read(pipe, pair.Token);
            LocalHandshake.Validate(reply, pair.PipeName);
            SpikeChecks.Require(reply.Generation != Guid.Empty, "Host must identify its generation.");
            await pipe.WriteAsync(Enumerable.Repeat((byte)'x', 65537).ToArray(), pair.Token);
            await Closed(pipe, pair.Token);
        }

        using (var memory = new MemoryStream(new byte[65537]))
        using (var bounded = new BoundedStream(memory))
        {
            try
            {
                await bounded.ReadExactlyAsync(new byte[65537], pair.Token);
                throw new InvalidOperationException("The internal frame limit was not enforced.");
            }
            catch (InvalidDataException)
            {
            }
        }

        var copy = Path.Combine(pair.DirectoryPath, "incompatible");
        Directory.CreateDirectory(copy);
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory))
        {
            File.Copy(path, Path.Combine(copy, Path.GetFileName(path)));
        }

        await using (var assembly = new FileStream(Path.Combine(copy, "PipeRelay.dll"), FileMode.Append))
        {
            await assembly.WriteAsync(new byte[] { 0 }, pair.Token);
        }

        await using var incompatible = await ModelContextProtocol.Client.McpClient.CreateAsync(
            SpikeChecks.TransportForAssembly(Path.Combine(copy, "PipeRelay.dll"), "auto", pair.PipeName),
            new() { ProtocolVersion = "2025-11-25" }, cancellationToken: pair.Token);
        var rejected = await incompatible.CallToolAsync("count", cancellationToken: pair.Token);
        SpikeChecks.Require(rejected.IsError == true, "A changed executable DLL must not connect to this host.");
        var valid = await pair.Second.CallToolAsync("count", cancellationToken: pair.Token);
        SpikeChecks.Require(valid.IsError != true, "Rejected clients must not damage the host listener.");
        return new { handshakeRejections = 5, oversizedLineRejected = true, modifiedDllRejected = true, listenerSurvived = true };
    }

    private static async Task<NamedPipeClientStream> Connect(string pipeName, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(cancellationToken);
        return pipe;
    }

    private static async Task Closed(Stream stream, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            SpikeChecks.Require(await stream.ReadAsync(new byte[1], deadline.Token) == 0, "Rejected channel remained open.");
        }
        catch (IOException)
        {
        }
    }
}
