using System.Diagnostics;
using System.IO.Pipes;
using ModelContextProtocol.Server;
using PipeRelay.Routing.Models;
using PipeRelay.Transport;

namespace PipeRelay.Routing;

internal sealed class DedicatedHost(RouteOptions options, RouteFiles files)
{
    public async Task Run()
    {
        using var lease = files.TryLock("host.lock");
        if (lease is null)
        {
            files.Event("lease-rejected", Guid.Empty);
            return;
        }

        using var lifetime = new CancellationTokenSource();
        var state = new FakeWorkspace(options, files, lifetime.Token);
        files.Event("owner", state.Generation);
        files.Event("initialization", state.Generation);
        using (var startup = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
        {
            await RouteFiles.Barrier(options.BeforeReadyBarrier, startup.Token);
        }

        if (options.CrashBeforeReady)
        {
            files.Event("controlled-crash", state.Generation);
            return;
        }

        var connections = new List<Task>();
        var pipe = CreatePipe();
        files.Event("ready", state.Generation);
        File.WriteAllText(files.PathFor("ready"), System.Text.Json.JsonSerializer.Serialize(new
        {
            pid = Environment.ProcessId, ticks = Checks.ProcessIdentity.Stamp(Environment.ProcessId),
            generation = state.Generation,
        }));
        var monitor = Monitor(state.Generation, lifetime);
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var connected = pipe;
                try
                {
                    await connected.WaitForConnectionAsync(lifetime.Token);
                }
                catch (UnauthorizedAccessException)
                {
                    pipe = CreatePipe();
                    await connected.DisposeAsync();
                    files.Event("peer-rejected", state.Generation);
                    continue;
                }

                pipe = CreatePipe();
                connections.RemoveAll(task => task.IsCompletedSuccessfully);
                connections.Add(Serve(connected, state, lifetime.Token));
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            await lifetime.CancelAsync();
            await pipe.DisposeAsync();
            await Task.WhenAll(connections);
            await monitor;
            await state.Stop();
            File.WriteAllText(files.PathFor("next-launch"), "0");
            files.Event("exit", state.Generation);
            lease.Dispose();
            files.Event("lease-released", state.Generation);
        }
    }

    private NamedPipeServerStream CreatePipe() => new(files.PipeName, PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task Serve(NamedPipeServerStream pipe, FakeWorkspace state, CancellationToken cancellationToken)
    {
        await using var owned = pipe;
        var admitted = false;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            var request = await LocalHandshake.ReadValue<RouteHello>(pipe, deadline.Token);
            if (request.Identity != BuildIdentity.Value || request.Key != files.Key
                || request.Descriptor != options.Descriptor || request.Generation != Guid.Empty)
            {
                files.Event("hello-rejected", state.Generation);
                return;
            }

            lock (_gate)
            {
                if (_closing)
                {
                    files.Event("admission-closed", state.Generation);
                    return;
                }

                _clients++;
                admitted = true;
                _everConnected = true;
            }

            files.Event("connected", state.Generation);
            await LocalHandshake.WriteValue(pipe, request with { Generation = state.Generation }, deadline.Token);
            await using var stream = new BoundedStream(pipe, () => files.Event("cancel-received", state.Generation));
            await using var server = McpServer.Create(new StreamServerTransport(stream, stream), new()
            {
                ServerInfo = new() { Name = "dedicated-fake-workspace", Version = "0.1" },
                ToolCollection = state.Tools(),
            });
            await server.RunAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException
            or System.Text.Json.JsonException or ModelContextProtocol.McpException)
        {
            files.Event("connection-error", state.Generation, exception.GetType().Name);
        }
        finally
        {
            if (admitted)
            {
                lock (_gate)
                {
                    _clients--;
                    _lastDisconnect = Stopwatch.GetTimestamp();
                }

                files.Event("disconnected", state.Generation);
            }
        }
    }

    private async Task Monitor(Guid generation, CancellationTokenSource lifetime)
    {
        while (!lifetime.IsCancellationRequested)
        {
            await Task.Delay(50);
            lock (_gate)
            {
                var grace = _everConnected ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(8);
                if (_clients != 0 || Stopwatch.GetElapsedTime(_lastDisconnect) < grace)
                {
                    continue;
                }

            }

            files.Event("closing-candidate", generation);
            await ExitBarrier(options.BeforeClosingBarrier, lifetime.Token);
            lock (_gate)
            {
                if (_clients != 0 || Stopwatch.GetElapsedTime(_lastDisconnect) < TimeSpan.FromSeconds(1))
                {
                    continue;
                }

                _closing = true;
            }

            files.Event("closing", generation);
            await ExitBarrier(options.ExitBarrier, lifetime.Token);
            await lifetime.CancelAsync();
        }
    }

    private async Task ExitBarrier(string? path, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await RouteFiles.Barrier(path, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            files.Event("exit-barrier-timeout", Guid.Empty);
        }
    }

    private readonly object _gate = new();
    private int _clients;
    private bool _everConnected;
    private bool _closing;
    private long _lastDisconnect = Stopwatch.GetTimestamp();
}
