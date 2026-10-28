using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PipeRelay.Checks;

internal static class SessionChecks
{
    public static async Task<object> Run(ClientPair pair, string protocol)
    {
        var first = await Count(pair.First, pair.Token);
        var second = await Count(pair.Second, pair.Token);
        SpikeChecks.Require(first.GetProperty("count").GetInt32() == 1 && second.GetProperty("count").GetInt32() == 2,
            "Two clients must share the host counter.");
        SpikeChecks.Require(first.GetProperty("hostPid").GetInt32() == pair.Host.Id
            && first.GetProperty("generation").GetString() == second.GetProperty("generation").GetString(),
            "Results must identify the same host and generation.");

        var count = ExplicitCount(pair.First, pair.Token);
        var slow = pair.Second.SendRequestAsync<CallToolRequestParams, CallToolResult>("tools/call", new()
        {
            Name = "slow",
            Arguments = new Dictionary<string, JsonElement> { ["seconds"] = JsonSerializer.SerializeToElement(1) },
        }, requestId: new RequestId(777), cancellationToken: pair.Token).AsTask();
        await Task.WhenAll(count, slow);
        SpikeChecks.Require((await count).GetProperty("count").GetInt32() == 3
            && (await slow).Content.OfType<TextContentBlock>().Single().Text == "Completed",
            "Identical client ids must return each caller's distinct tool result.");

        var inventory = SpikeChecks.Read(await pair.Second.CallToolAsync("toggle", cancellationToken: pair.Token));
        var assemblies = inventory.GetProperty("assemblies").EnumerateArray().Select(a => a.GetString()!).ToArray();
        SpikeChecks.Require(!assemblies.Any(a => a.StartsWith("Microsoft.CodeAnalysis") || a.StartsWith("Microsoft.Build")
            || a.StartsWith("RoslynMcpServer")), "A pure proxy must not load Roslyn, MSBuild or production services.");
        await pair.Second.CallToolAsync("toggle", cancellationToken: pair.Token);
        var initialSecondChanges = pair.SecondChanges;
        await pair.First.CallToolAsync("toggle", cancellationToken: pair.Token);
        SpikeChecks.Require((await pair.First.ListToolsAsync(cancellationToken: pair.Token)).Any(t => t.Name == "extra")
            && !(await pair.Second.ListToolsAsync(cancellationToken: pair.Token)).Any(t => t.Name == "extra"),
            "Tool activation must remain inside the calling facade session.");

        if (protocol == "2026-07-28")
        {
            await Task.Delay(100, pair.Token);
            SpikeChecks.Require(pair.FirstChanges == 0 && pair.SecondChanges == 0,
                "July clients must not receive list changes before subscriptions/listen.");
            await pair.Listen(pair.First);
            await pair.Listen(pair.Second);
        }
        else
        {
            await pair.Until(() => pair.FirstChanges == 1 && pair.SecondChanges == 2);
            initialSecondChanges = pair.SecondChanges;
        }

        var before = pair.FirstChanges;
        await pair.First.CallToolAsync("toggle", cancellationToken: pair.Token);
        await pair.Until(() => pair.FirstChanges == before + 1);
        SpikeChecks.Require(pair.SecondChanges == initialSecondChanges, "One session's toggle must not notify another session.");

        await Cancellation(pair);
        return new { generation = first.GetProperty("generation").GetString(), proxyAssemblies = assemblies,
            sharedCounter = true, identicalRequestIds = true, isolatedToolsAndSubscriptions = true, isolatedCancellation = true };
    }

    public static async Task<object> Disconnect(ClientPair pair)
    {
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = pair.Second.CallToolAsync("slow", new Dictionary<string, object?> { ["seconds"] = 10 },
            progress: new InlineProgress(_ => began.TrySetResult()), cancellationToken: pair.Token).AsTask();
        await began.Task.WaitAsync(pair.Token);
        var start = System.Diagnostics.Stopwatch.StartNew();
        await pair.KillHost();
        var error = await pending.WaitAsync(TimeSpan.FromSeconds(3));
        SpikeChecks.Require(error.IsError == true, "Host death must fail an in-flight call explicitly.");
        var calls = await pair.Second.ListToolsAsync(cancellationToken: pair.Token);
        SpikeChecks.Require(calls.Any(t => t.Name == "count"), "tools/list must remain available after host death.");
        var retry = await pair.Second.CallToolAsync("count", cancellationToken: pair.Token);
        SpikeChecks.Require(retry.IsError == true && !pair.Second.Completion.IsCompleted,
            "A failed host must not trigger local execution, replay or loss of the facade session.");
        return new { explicitError = true, facadeAlive = true, automaticRecovery = false, elapsedMilliseconds = start.Elapsed.TotalMilliseconds };
    }

    private static async Task<JsonElement> Count(McpClient client, CancellationToken cancellationToken) =>
        SpikeChecks.Read(await client.CallToolAsync("count", cancellationToken: cancellationToken));

    private static async Task<JsonElement> ExplicitCount(McpClient client, CancellationToken cancellationToken) =>
        SpikeChecks.Read(await client.SendRequestAsync<CallToolRequestParams, CallToolResult>("tools/call", new() { Name = "count" },
            requestId: new RequestId(777), cancellationToken: cancellationToken));

    private static async Task Cancellation(ClientPair pair)
    {
        var beganFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beganSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstProgress = 0;
        var secondProgress = 0;
        await using var firstObserver = pair.First.RegisterNotificationHandler("notifications/progress", (notification, _) =>
        {
            var progress = notification.Params.Deserialize<ProgressNotificationParams>(McpJsonUtilities.DefaultOptions)!;
            SpikeChecks.Require(progress.ProgressToken.ToString() == "same", "Wrong first progress token.");
            Interlocked.Increment(ref firstProgress);
            beganFirst.TrySetResult();
            return ValueTask.CompletedTask;
        });
        await using var secondObserver = pair.Second.RegisterNotificationHandler("notifications/progress", (notification, _) =>
        {
            var progress = notification.Params.Deserialize<ProgressNotificationParams>(McpJsonUtilities.DefaultOptions)!;
            SpikeChecks.Require(progress.ProgressToken.ToString() == "same", "Wrong second progress token.");
            Interlocked.Increment(ref secondProgress);
            beganSecond.TrySetResult();
            return ValueTask.CompletedTask;
        });
        using var stopSecond = CancellationTokenSource.CreateLinkedTokenSource(pair.Token);
        var firstSlow = Slow(pair.First, pair.Token);
        var secondSlow = Slow(pair.Second, stopSecond.Token);
        await Task.WhenAll(beganFirst.Task, beganSecond.Task).WaitAsync(pair.Token);
        await pair.Second.SendNotificationAsync("notifications/cancelled",
            new CancelledNotificationParams { RequestId = new RequestId(888) }, cancellationToken: pair.Token);
        await stopSecond.CancelAsync();
        try
        {
            await secondSlow;
        }
        catch (OperationCanceledException)
        {
        }

        SpikeChecks.Require((await firstSlow).IsError != true, "Cancelling the second request must not cancel the first.");
        var state = await Count(pair.First, pair.Token);
        SpikeChecks.Require(state.GetProperty("cancelled").GetInt32() == 1 && state.GetProperty("active").GetInt32() == 0,
            "Exactly one host operation must be cancelled and the other must complete.");
        SpikeChecks.Require(firstProgress == 20 && secondProgress >= 1 && secondProgress < firstProgress,
            "Progress must remain isolated even when clients reuse request ids and progress tokens.");
    }

    private static Task<CallToolResult> Slow(McpClient client, CancellationToken cancellationToken) =>
        client.SendRequestAsync<CallToolRequestParams, CallToolResult>("tools/call", new()
        {
            Name = "slow",
            Arguments = new Dictionary<string, JsonElement> { ["seconds"] = JsonSerializer.SerializeToElement(2) },
            Meta = new JsonObject { ["progressToken"] = "same" },
        }, requestId: new RequestId(888), cancellationToken: cancellationToken).AsTask();

    private sealed class InlineProgress(Action<ProgressNotificationValue> report) : IProgress<ProgressNotificationValue>
    {
        public void Report(ProgressNotificationValue value) => report(value);
    }
}
