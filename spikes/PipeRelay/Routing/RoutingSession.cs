using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using PipeRelay.Routing.Models;

namespace PipeRelay.Routing;

internal sealed class RoutingSession(RouteOptions initial) : IAsyncDisposable
{
    public async Task<CallToolResult> Switch(string optionsFile, CancellationToken cancellationToken)
    {
        var options = JsonSerializer.Deserialize<RouteOptions>(await File.ReadAllTextAsync(optionsFile, cancellationToken))!;
        await _gate.WaitAsync(cancellationToken);
        RouteConnection? candidate = null;
        try
        {
            candidate = await HostDiscovery.Open(options, cancellationToken);
            var loaded = await candidate.Client.CallToolAsync("load", cancellationToken: cancellationToken);
            if (loaded.IsError == true)
            {
                return loaded;
            }

            var old = _current;
            _current = candidate;
            _options = options;
            candidate = null;
            if (old is not null)
            {
                // In-flight calls retain the old connection until completion; new calls observe the committed binding.
                _retired.Add(old);
            }

            await Reap();
            return loaded;
        }
        catch (Exception exception) when (Recoverable(exception))
        {
            return Error();
        }
        finally
        {
            if (candidate is not null)
            {
                await candidate.DisposeAsync();
            }

            _gate.Release();
        }
    }

    public async Task<CallToolResult> Forward(string name, IReadOnlyDictionary<string, object?>? arguments,
        IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        RouteConnection? connection = null;
        var attemptOptions = _options;
        try
        {
            await _gate.WaitAsync(deadline.Token);
            try
            {
                attemptOptions = _options;
                if (_current is not null && !_current.Failed)
                {
                    if (_current.Client.Completion.IsCompleted)
                    {
                        _current.Failed = true;
                    }
                }

                if (_current is null || _current.Failed)
                {
                    if (_current is not null)
                    {
                        _retired.Add(_current);
                        _current = null;
                    }

                    _current = await HostDiscovery.Open(_options, deadline.Token);
                }

                connection = _current;
                _active[connection] = _active.GetValueOrDefault(connection) + 1;
            }
            finally
            {
                _gate.Release();
            }

            var requestId = new RequestId(Guid.NewGuid().ToString("N"));
            var token = new ProgressToken(Guid.NewGuid().ToString("N"));
            await using var registration = connection.Client.RegisterNotificationHandler("notifications/progress", (message, _) =>
            {
                var update = message.Params.Deserialize<ProgressNotificationParams>(McpJsonUtilities.DefaultOptions);
                if (!connection.Failed && update?.ProgressToken == token)
                {
                    progress?.Report(update.Progress);
                }

                return ValueTask.CompletedTask;
            });
            try
            {
                var response = connection.Client.SendRequestAsync<CallToolRequestParams, CallToolResult>("tools/call", new()
                {
                    Name = name,
                    Arguments = arguments?.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value)),
                    Meta = new JsonObject { ["progressToken"] = token.ToString() },
                }, requestId: requestId, cancellationToken: deadline.Token).AsTask();
                var completed = await Task.WhenAny(response, connection.Client.Completion).WaitAsync(deadline.Token);
                if (completed != response)
                {
                    throw new IOException("The host transport ended before the response.");
                }

                var result = await response;
                return connection.Failed ? Error() : result;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                // SDK 2.2 also sends cancellation, but does not await its send task. Await an independent bounded delivery.
                using var delivery = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await connection.Client.SendNotificationAsync("notifications/cancelled",
                    new CancelledNotificationParams { RequestId = requestId }, cancellationToken: delivery.Token);
                throw;
            }
        }
        catch (Exception exception) when (Recoverable(exception) && !cancellationToken.IsCancellationRequested)
        {
            new RouteFiles(connection?.Options ?? attemptOptions).Event("forward-error",
                connection?.Generation ?? Guid.Empty, exception.ToString());
            if (connection is not null)
            {
                connection.Failed = true;
            }

            return Error();
        }
        finally
        {
            if (connection is not null)
            {
                await _gate.WaitAsync();
                try
                {
                    _active[connection]--;
                    await Reap();
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        if (_current is not null)
        {
            await _current.DisposeAsync();
        }

        foreach (var connection in _retired)
        {
            await connection.DisposeAsync();
        }

        _lifetime.Dispose();
        _gate.Dispose();
    }

    private async Task Reap()
    {
        foreach (var connection in _retired.Where(item => _active.GetValueOrDefault(item) == 0).ToArray())
        {
            await connection.DisposeAsync();
            _retired.Remove(connection);
            _active.Remove(connection);
        }
    }

    private static bool Recoverable(Exception exception) => exception is IOException or OperationCanceledException
        or McpException or JsonException or UnauthorizedAccessException;

    private static CallToolResult Error() => new() { IsError = true,
        Content = [new TextContentBlock { Text = "Host unavailable; request was not retried. Outcome may be unknown." }] };

    private RouteOptions _options = initial;
    private RouteConnection? _current;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<RouteConnection, int> _active = [];
    private readonly List<RouteConnection> _retired = [];
}
