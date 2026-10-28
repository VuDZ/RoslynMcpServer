using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PipeRelay.Routing.Models;

namespace PipeRelay.Routing;

internal sealed class FakeWorkspace(RouteOptions options, RouteFiles files, CancellationToken lifetime)
{
    public Guid Generation => _counter.Generation;

    public McpServerPrimitiveCollection<McpServerTool> Tools() => new()
    {
        McpServerTool.Create((Func<CancellationToken, Task<string>>)Load, new() { Name = "load" }),
        McpServerTool.Create((Func<CancellationToken, Task<string>>)Count, new() { Name = "count" }),
        McpServerTool.Create((Func<int, IProgress<ProgressNotificationValue>, CancellationToken, Task<string>>)Slow,
            new() { Name = "slow" }),
        McpServerTool.Create((Func<string, CancellationToken, Task<string>>)Effect, new() { Name = "effect" }),
    };

    public async Task Stop()
    {
        Task? attempt;
        lock (_gate)
        {
            attempt = _load;
        }

        if (attempt is not null)
        {
            try
            {
                await attempt;
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException)
            {
            }
        }
    }

    private async Task<string> Load(CancellationToken cancellationToken)
    {
        Task attempt;
        int waiterId;
        lock (_gate)
        {
            attempt = _load ??= Initialize();
            waiterId = attempt.IsCompleted ? 0 : ++_nextWaiterId;
            if (waiterId != 0)
            {
                _activeWaiters++;
                files.Event("load-waiter", Generation, new { waiterId, activeWaiters = _activeWaiters });
            }
        }

        var cancelled = false;
        try
        {
            // A cancelled waiter leaves the shared attempt running while any proxy is connected.
            await attempt.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            throw;
        }
        finally
        {
            if (waiterId != 0)
            {
                lock (_gate)
                {
                    _activeWaiters--;
                    if (cancelled)
                    {
                        _cancelledWaiters++;
                    }

                    files.Event("load-waiter-ended", Generation, new
                    {
                        waiterId, activeWaiters = _activeWaiters, cancelled, cancelledWaiters = _cancelledWaiters,
                    });
                }
            }
        }

        return JsonSerializer.Serialize(new { descriptor = options.Descriptor, generation = Generation });
    }

    private async Task Initialize()
    {
        files.Event("load-start", Generation);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await RouteFiles.Barrier(options.LoadBarrier, deadline.Token);
            await Task.Delay(options.LoadMilliseconds, deadline.Token);
            if (options.FailLoad)
            {
                throw new IOException("Controlled shared load failure.");
            }

            files.Event("load-success", Generation);
        }
        catch
        {
            files.Event("load-failure", Generation);
            throw;
        }
    }

    private async Task<string> Count(CancellationToken cancellationToken)
    {
        await Load(cancellationToken);
        return JsonSerializer.Serialize(new { descriptor = options.Descriptor,
            value = JsonSerializer.Deserialize<JsonElement>(_counter.Count()) });
    }

    private async Task<string> Slow(int seconds, IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken)
    {
        await Load(cancellationToken);
        return await _counter.Slow(seconds, progress, cancellationToken);
    }

    private async Task<string> Effect(string replyBarrier, CancellationToken cancellationToken)
    {
        await Load(cancellationToken);
        File.AppendAllText(files.PathFor("effects"), Generation + "\n");
        files.Event("effect", Generation);
        await RouteFiles.Barrier(replyBarrier, cancellationToken);
        return "Effect completed";
    }

    private readonly CounterState _counter = new();
    private readonly object _gate = new();
    private Task? _load;
    private int _nextWaiterId;
    private int _activeWaiters;
    private int _cancelledWaiters;
}
