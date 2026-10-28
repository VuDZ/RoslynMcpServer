using System.Diagnostics;
using ModelContextProtocol.Client;

namespace PipeRelay.Checks;

internal static class CountBenchmark
{
    public static async Task<object> Run(McpClient client, CancellationToken cancellationToken)
    {
        for (var index = 0; index < 10; index++)
        {
            SpikeChecks.Read(await client.CallToolAsync("count", cancellationToken: cancellationToken));
        }

        var milliseconds = new double[200];
        for (var index = 0; index < milliseconds.Length; index++)
        {
            var start = Stopwatch.GetTimestamp();
            SpikeChecks.Read(await client.CallToolAsync("count", cancellationToken: cancellationToken));
            milliseconds[index] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        var sorted = milliseconds.Order().ToArray();
        return new { count = milliseconds.Length, warmup = 10, p50Milliseconds = sorted[99],
            p95Milliseconds = sorted[189], samplesMilliseconds = milliseconds };
    }
}
