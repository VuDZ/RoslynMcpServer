using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace PipeRelay;

internal sealed class CounterState
{
    public Guid Generation { get; } = Guid.NewGuid();

    public string Count() => JsonSerializer.Serialize(new
    {
        count = Interlocked.Increment(ref _count),
        hostPid = Environment.ProcessId,
        generation = Generation,
        cancelled = Volatile.Read(ref _cancelled),
        active = Volatile.Read(ref _active),
    });

    public async Task<string> Slow(int seconds, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken)
    {
        if (seconds is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "Duration must be between 1 and 120 seconds.");
        }

        Interlocked.Increment(ref _active);
        try
        {
            for (var tick = 0; tick < seconds * 10; tick++)
            {
                progress.Report(new() { Progress = tick, Total = seconds * 10, Message = "Host tick" });
                await Task.Delay(100, cancellationToken);
            }

            return "Completed";
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelled);
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }

    public McpServerPrimitiveCollection<McpServerTool> Tools() => new()
    {
        McpServerTool.Create((Func<string>)Count, new() { Name = "count" }),
        McpServerTool.Create(
            (Func<int, IProgress<ProgressNotificationValue>, CancellationToken, Task<string>>)Slow,
            new() { Name = "slow" }),
    };

    private int _count;
    private int _cancelled;
    private int _active;
}
