using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PipeRelay.Transport;
using PipeRelay.Transport.Models;

namespace PipeRelay;

internal sealed class Facade : IAsyncDisposable
{
    public McpServerPrimitiveCollection<McpServerTool> Tools { get; }

    public Facade(CounterState? local, string pipeName)
    {
        _local = local;
        _pipeName = pipeName;
        _remote = new Lazy<Task<McpClient>>(Connect);
        _extra = McpServerTool.Create((Func<string>)(() => "Session-local extra"), new() { Name = "extra" });
        Tools = new()
        {
            McpServerTool.Create((Func<CancellationToken, Task<CallToolResult>>)Count, new() { Name = "count" }),
            McpServerTool.Create(
                (Func<int, IProgress<ProgressNotificationValue>, CancellationToken, Task<CallToolResult>>)Slow,
                new() { Name = "slow" }),
            McpServerTool.Create((Func<string>)Toggle, new() { Name = "toggle" }),
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        if (_remote.IsValueCreated)
        {
            try
            {
                var client = await _remote.Value;
                await client.DisposeAsync();
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
                or OperationCanceledException or McpException or JsonException)
            {
            }
        }

        _lifetime.Dispose();
    }

    private Task<CallToolResult> Count(CancellationToken cancellationToken) => _local is not null
        ? Task.FromResult(Text(_local.Count()))
        : Forward("count", null, null, cancellationToken);

    private async Task<CallToolResult> Slow(
        int seconds, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken)
    {
        return _local is not null
            ? Text(await _local.Slow(seconds, progress, cancellationToken))
            : await Forward("slow", new Dictionary<string, object?> { ["seconds"] = seconds }, progress, cancellationToken);
    }

    private string Toggle()
    {
        lock (_gate)
        {
            if (Tools.Contains(_extra))
            {
                Tools.Remove(_extra);
            }
            else
            {
                Tools.Add(_extra);
            }

            return JsonSerializer.Serialize(new
            {
                enabled = Tools.Contains(_extra),
                processId = Environment.ProcessId,
                serverGc = System.Runtime.GCSettings.IsServerGC,
                assemblies = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).Order().ToArray(),
            });
        }
    }

    private async Task<CallToolResult> Forward(string name, IReadOnlyDictionary<string, object?>? arguments,
        IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var client = await _remote.Value.WaitAsync(deadline.Token);
            var requestId = new RequestId(Guid.NewGuid().ToString("N"));
            var progressToken = new ProgressToken(Guid.NewGuid().ToString("N"));
            await using var registration = client.RegisterNotificationHandler("notifications/progress", (notification, _) =>
            {
                var update = notification.Params.Deserialize<ProgressNotificationParams>(McpJsonUtilities.DefaultOptions);
                if (update?.ProgressToken == progressToken)
                {
                    progress?.Report(update.Progress);
                }

                return ValueTask.CompletedTask;
            });
            try
            {
                return await client.SendRequestAsync<CallToolRequestParams, CallToolResult>("tools/call", new()
                {
                    Name = name,
                    Arguments = arguments?.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value)),
                    Meta = new JsonObject { ["progressToken"] = progressToken.ToString() },
                }, requestId: requestId, cancellationToken: deadline.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                // Explicitly await cancellation delivery; cancelling the local wait alone left the SDK 2.2 host running.
                // Evidence: docs/workspace-host-process/epoch-0-design-and-spike/evidence/task-03-pipe-relay-spike.md#c03-01
                using var sendDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await client.SendNotificationAsync("notifications/cancelled",
                    new CancelledNotificationParams { RequestId = requestId }, cancellationToken: sendDeadline.Token);
                throw;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or McpException or JsonException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new() { IsError = true, Content = [new TextContentBlock { Text = "Host unavailable; request was not retried. Outcome may be unknown." }] };
        }
    }

    private async Task<McpClient> Connect()
    {
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await pipe.ConnectAsync(deadline.Token);
            var request = new Handshake(1, BuildIdentity.Value, _pipeName, "spike-" + Environment.ProcessId, Guid.Empty);
            await LocalHandshake.Write(pipe, request, deadline.Token);
            var reply = await LocalHandshake.Read(pipe, deadline.Token);
            LocalHandshake.Validate(reply, _pipeName);
            if (reply.Generation == Guid.Empty || reply.HarnessId != request.HarnessId)
            {
                throw new InvalidDataException("Host handshake has no valid generation or correlation.");
            }

            var stream = new BoundedStream(pipe);
            return await McpClient.CreateAsync(new StreamClientTransport(stream, stream),
                new() { ProtocolVersion = "2025-11-25", InitializationTimeout = TimeSpan.FromSeconds(5) },
                cancellationToken: deadline.Token);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    private static CallToolResult Text(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    private readonly CounterState? _local;
    private readonly string _pipeName;
    private readonly Lazy<Task<McpClient>> _remote;
    private readonly McpServerTool _extra;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
}
