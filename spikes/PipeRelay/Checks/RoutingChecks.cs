using ModelContextProtocol.Client;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using PipeRelay.Routing;
using PipeRelay.Routing.Models;
using System.Text.Json;

namespace PipeRelay.Checks;

internal static class RoutingChecks
{
    public static async Task<int> Run(string selected)
    {
        var results = new List<object>();
        try
        {
            await RunMatrix(selected, results);
            await WriteResult(selected, results, null);
            return 0;
        }
        catch (Exception exception)
        {
            await WriteResult(selected, results, exception);
            throw;
        }
    }

    public static async Task LegacyRecovery()
    {
        await using var pair = await ClientPair.Open("2025-11-25", roles: false);
        SpikeChecks.Read(await pair.Second.CallToolAsync("count", cancellationToken: pair.Token));
        await pair.KillHost();
        await Task.Delay(100, pair.Token);
        SpikeChecks.Read(await pair.Second.CallToolAsync("count", cancellationToken: pair.Token));
    }

    private static async Task RunMatrix(string selected, List<object> results)
    {
        foreach (var protocol in new[] { "2025-11-25", "2026-07-28" })
        {
            if (selected == "lifecycle")
            {
                await Lifecycle(protocol, results);
                continue;
            }

            if (selected == "boundaries")
            {
                await Boundaries(protocol, results);
                continue;
            }

            if (selected == "startup")
            {
                await StartupDeaths(protocol, results);
                continue;
            }

            for (var round = 0; round < (selected == "quick" ? 1 : 10); round++)
            {
                await ColdRecovery(protocol, round, results);
            }

            await Switching(protocol, results);
            await Failures(protocol, results);
            await AllWaitersCancelled(protocol, results);
            await StartupDeaths(protocol, results);
            await Lifecycle(protocol, results);
            await Boundaries(protocol, results);
        }

    }

    private static async Task WriteResult(string selected, List<object> results, Exception? error)
    {
        var output = Path.Combine(AppContext.BaseDirectory, "routing-result.json");
        var lastFixture = Path.Combine(AppContext.BaseDirectory, "routing-fixture-last.json");
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            utc = DateTimeOffset.UtcNow, os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            runtime = Environment.Version.ToString(), identity = BuildIdentity.Value, selected, results,
            error = error?.ToString(),
            failedFixture = error is not null && File.Exists(lastFixture)
                ? JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(lastFixture)) : (JsonElement?)null,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
    }

    private static async Task ColdRecovery(string protocol, int round, List<object> results)
    {
        await using var fixture = new RoutingFixture();
        var options = new RouteOptions(fixture.Root, "A");
        var config = fixture.Config(options);
        var clients = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => fixture.Open(config, protocol)));
        var first = await Task.WhenAll(clients.Select(client => Count(client, fixture.Token)));
        RequireShared(first, "A");
        SpikeChecks.Require(fixture.Count("owner") == 1 && fixture.Count("initialization") == 1
            && fixture.Count("load-start") == 1, "Cold start duplicated ownership or heavy work.");
        await fixture.KillOwner(options);
        // Cooldown is shared on disk, not ten independent retry loops; the next explicit call starts recovery.
        await Task.Delay(2100, fixture.Token);
        var second = await Task.WhenAll(clients.Select(client => Count(client, fixture.Token)));
        RequireShared(second, "A");
        SpikeChecks.Require(Generation(first[0]) != Generation(second[0]), "Recovery reused the old generation.");
        SpikeChecks.Require(fixture.Count("owner") == 2 && fixture.Count("initialization") == 2
            && fixture.Count("load-start") == 2, "Recovery duplicated ownership or heavy work.");
        await fixture.DisposeAsync();
        results.Add(new { scenario = "cold-and-recovery", protocol, round, root = fixture.Root,
            generations = new[] { Generation(first[0]), Generation(second[0]) }, events = fixture.Events() });
        Console.WriteLine($"PASS cold/recovery {protocol} round {round + 1}");
    }

    private static async Task Switching(string protocol, List<object> results)
    {
        await using var fixture = new RoutingFixture();
        var a = new RouteOptions(fixture.Root, "A");
        var b = new RouteOptions(fixture.Root, "B");
        var first = await fixture.Open(fixture.Config(a), protocol);
        var second = await fixture.Open(fixture.Config(a), protocol);
        var existing = await fixture.Open(fixture.Config(b), protocol);
        var before = await Count(first, fixture.Token);
        await Count(second, fixture.Token);
        var readyB = await Count(existing, fixture.Token);
        if (protocol == "2026-07-28")
        {
            await fixture.Listen(second);
        }

        await second.CallToolAsync("toggle", cancellationToken: fixture.Token);
        await fixture.Until(() => fixture.Changes == 1);
        var progress = 0;
        var pending = second.CallToolAsync("slow", new Dictionary<string, object?> { ["seconds"] = 2 },
            progress: new InlineProgress(_ => Interlocked.Increment(ref progress)), cancellationToken: fixture.Token).AsTask();
        await fixture.Until(() => Volatile.Read(ref progress) > 0);
        SpikeChecks.Read(await second.CallToolAsync("switch", new Dictionary<string, object?>
        {
            ["optionsFile"] = fixture.Config(b),
        }, cancellationToken: fixture.Token));
        SpikeChecks.Require((await pending).IsError != true && progress == 20, "In-flight A did not finish on A.");
        SpikeChecks.Require(Generation(await Count(second, fixture.Token)) == Generation(readyB), "Did not attach existing B.");
        var stillA = await Count(first, fixture.Token);
        SpikeChecks.Require(Generation(stillA) == Generation(before)
            && stillA.GetProperty("value").GetProperty("count").GetInt32() == 3, "Other A session was reset.");

        var c = new RouteOptions(fixture.Root, "C");
        var remainingA = await fixture.Open(fixture.Config(a), protocol);
        await Count(remainingA, fixture.Token);
        SpikeChecks.Read(await first.CallToolAsync("switch", new Dictionary<string, object?>
        {
            ["optionsFile"] = fixture.Config(c),
        }, cancellationToken: fixture.Token));
        SpikeChecks.Require((await Count(first, fixture.Token)).GetProperty("descriptor").GetString() == "C",
            "Initiating proxy did not commit new C.");
        var afterInitiator = await Count(remainingA, fixture.Token);
        SpikeChecks.Require(Generation(afterInitiator) == Generation(before)
            && afterInitiator.GetProperty("value").GetProperty("hostPid").GetInt32()
                == before.GetProperty("value").GetProperty("hostPid").GetInt32()
            && afterInitiator.GetProperty("value").GetProperty("count").GetInt32() == 5,
            "Initiator switch reset A for the remaining proxy.");
        var aKey = new RouteFiles(a).Key;
        SpikeChecks.Require(fixture.Events().Count(e => e.GetProperty("key").GetString() == aKey
            && e.GetProperty("name").GetString() is "owner" or "initialization" or "load-start") == 3,
            "Initiator switch reinitialized A.");
        SpikeChecks.Require(fixture.Count("owner") == 3 && fixture.Count("load-start") == 3, "Switch reloaded a workspace.");
        var failed = await second.CallToolAsync("switch", new Dictionary<string, object?>
        {
            ["optionsFile"] = fixture.Config(new(fixture.Root, "failed-B", FailLoad: true)),
        }, cancellationToken: fixture.Token);
        SpikeChecks.Require(failed.IsError == true && Generation(await Count(second, fixture.Token)) == Generation(readyB),
            "Failed switch lost live B binding.");
        SpikeChecks.Require((await second.ListToolsAsync(cancellationToken: fixture.Token)).Any(tool => tool.Name == "extra"),
            "Session activation was lost on switch.");
        await second.CallToolAsync("toggle", cancellationToken: fixture.Token);
        await fixture.Until(() => fixture.Changes == 2);
        await second.CallToolAsync("toggle", cancellationToken: fixture.Token);
        await fixture.Until(() => fixture.Changes == 3);
        await fixture.KillOwner(b);
        Console.WriteLine("Switching: host B killed");
        await Task.Delay(2100, fixture.Token);
        await Count(second, fixture.Token);
        Console.WriteLine("Switching: host B recovered");
        SpikeChecks.Require((await second.ListToolsAsync(cancellationToken: fixture.Token)).Any(tool => tool.Name == "extra"),
            "Active session tool was lost during recovery.");
        await second.CallToolAsync("toggle", cancellationToken: fixture.Token);
        await fixture.Until(() => fixture.Changes == 4);
        Console.WriteLine("Switching: subscription preserved");
        await CancelAndProgress(second, fixture, b);
        await fixture.DisposeAsync();
        results.Add(new { scenario = "switch-state-and-cancellation", protocol, events = fixture.Events() });
        Console.WriteLine($"PASS switching {protocol}");
    }

    private static async Task CancelAndProgress(McpClient client, RoutingFixture fixture, RouteOptions options)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        var updates = 0;
        var requestId = new RequestId("cancel-routing");
        await using var tracing = client.RegisterNotificationHandler("notifications/progress", (_, _) =>
        {
            Interlocked.Increment(ref updates);
            return ValueTask.CompletedTask;
        });
        var pending = client.SendRequestAsync<CallToolRequestParams, CallToolResult>("tools/call", new()
        {
            Name = "slow", Arguments = new Dictionary<string, JsonElement> { ["seconds"] = JsonSerializer.SerializeToElement(3) },
            Meta = new System.Text.Json.Nodes.JsonObject { ["progressToken"] = "cancel-progress" },
        }, requestId: requestId, cancellationToken: cancel.Token).AsTask();
        await fixture.Until(() => Volatile.Read(ref updates) > 0);
        Console.WriteLine("Cancellation: progress observed");
        var beforeCancel = fixture.Count("cancel-received");
        await cancel.CancelAsync();
        await Task.Delay(100, fixture.Token);
        var automaticObserved = fixture.Count("cancel-received") > beforeCancel;
        Console.WriteLine($"Cancellation trace: automatic observed={automaticObserved}");
        await client.SendNotificationAsync("notifications/cancelled",
            new CancelledNotificationParams { RequestId = requestId }, cancellationToken: fixture.Token);
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            throw new InvalidOperationException("Cancelled waiter completed successfully.");
        }
        catch (OperationCanceledException)
        {
        }

        await fixture.Until(() => fixture.Events().Any(e => e.GetProperty("name").GetString() == "cancel-received"));
        await fixture.Until(() => Count(client, fixture.Token).GetAwaiter().GetResult()
            .GetProperty("value").GetProperty("active").GetInt32() == 0);
        SpikeChecks.Require((await Count(client, fixture.Token)).GetProperty("value").GetProperty("cancelled").GetInt32() == 1,
            "Cancellation did not reach exactly one host operation.");
        SpikeChecks.Require(updates < 30, "Cancellation did not stop host work.");
    }

    private static async Task Failures(string protocol, List<object> results)
    {
        await using var fixture = new RoutingFixture();
        var barrier = Path.Combine(fixture.Root, "load-release");
        var options = new RouteOptions(fixture.Root, "joined", LoadBarrier: barrier);
        var config = fixture.Config(options);
        var clients = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => fixture.Open(config, protocol)));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        var one = clients[0].SendRequestAsync<CallToolRequestParams, CallToolResult>("tools/call",
            new() { Name = "count" }, requestId: new RequestId("one-load-waiter"),
            cancellationToken: cancelled.Token).AsTask();
        var others = clients.Skip(1).Select(client => Count(client, fixture.Token)).ToArray();
        await fixture.Until(() => fixture.Count("load-waiter") == 10);
        await clients[0].SendNotificationAsync("notifications/cancelled",
            new CancelledNotificationParams { RequestId = new RequestId("one-load-waiter") },
            cancellationToken: fixture.Token);
        await cancelled.CancelAsync();
        try
        {
            SpikeChecks.Require((await one.WaitAsync(TimeSpan.FromSeconds(5))).IsError == true,
                "Cancelled load waiter completed successfully.");
        }
        catch (OperationCanceledException)
        {
        }

        await fixture.Until(() => fixture.Events().Any(e => e.GetProperty("name").GetString() == "load-waiter-ended"
            && e.GetProperty("data").GetProperty("activeWaiters").GetInt32() == 9
            && e.GetProperty("data").GetProperty("cancelledWaiters").GetInt32() == 1));
        File.WriteAllText(barrier, "release");
        RequireShared(await Task.WhenAll(others), "joined");
        SpikeChecks.Require(fixture.Count("load-start") == 1, "One cancelled waiter restarted load.");
        await Task.WhenAll(clients.Where((_, index) => index != 1).Select(fixture.Close));
        var failedOptions = new RouteOptions(fixture.Root, "failure", FailLoad: true);
        var failedConfig = fixture.Config(failedOptions);
        var failedClients = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => fixture.Open(failedConfig, protocol)));
        var failures = await Task.WhenAll(failedClients.Select(client => client.CallToolAsync("count", cancellationToken: fixture.Token).AsTask()));
        SpikeChecks.Require(failures.All(value => value.IsError == true) && fixture.Count("load-failure") == 1,
            "Shared load failure caused independent retries.");
        await Task.WhenAll(failedClients.Select(fixture.Close));

        var replyBarrier = Path.Combine(fixture.Root, "reply-release");
        var effect = clients[1].CallToolAsync("effect", new Dictionary<string, object?> { ["replyBarrier"] = replyBarrier },
            cancellationToken: fixture.Token).AsTask();
        await fixture.Until(() => fixture.Count("effect") == 1);
        await fixture.KillOwner(options);
        SpikeChecks.Require((await effect.WaitAsync(TimeSpan.FromSeconds(5))).IsError == true, "Interrupted side effect was hidden.");
        await Task.Delay(2100, fixture.Token);
        await Count(clients[1], fixture.Token);
        SpikeChecks.Require(File.ReadAllLines(new RouteFiles(options).PathFor("effects")).Length == 1,
            "Side effect was replayed.");
        await fixture.DisposeAsync();
        results.Add(new { scenario = "load-join-failure-and-no-replay", protocol, events = fixture.Events() });
        Console.WriteLine($"PASS failures/replay {protocol}");
    }

    private static async Task StartupDeaths(string protocol, List<object> results)
    {
        await using var fixture = new RoutingFixture();
        var release = Path.Combine(fixture.Root, "startup-release");
        var options = new RouteOptions(fixture.Root, "startup", BeforeReadyBarrier: release,
            LaunchBarrier: Path.Combine(fixture.Root, "launcher-release"));
        var client = await fixture.Open(fixture.Config(options), protocol);
        var pending = client.CallToolAsync("count", cancellationToken: fixture.Token).AsTask();
        await fixture.Until(() => fixture.Count("initialization") == 1);
        var launcher = fixture.Events().Single(e => e.GetProperty("name").GetString() == "launch");
        await fixture.Kill(launcher.GetProperty("pid").GetInt32(), launcher.GetProperty("ticks").GetInt64());
        try
        {
            await pending.WaitAsync(TimeSpan.FromMilliseconds(200));
        }
        catch (Exception exception) when (exception is IOException or McpException or TimeoutException)
        {
        }

        var second = await fixture.Open(fixture.Config(options with { LaunchBarrier = null }), protocol);
        var joined = Count(second, fixture.Token);
        File.WriteAllText(release, "release");
        await joined;
        SpikeChecks.Require(fixture.Count("owner") == 1 && fixture.Count("load-start") == 1,
            "Launcher death lost continuous ownership.");

        var blocked = new RouteOptions(fixture.Root, "host-before-ready", BeforeReadyBarrier:
            Path.Combine(fixture.Root, "host-release"));
        var blockedClient = await fixture.Open(fixture.Config(blocked), protocol);
        var blockedCall = blockedClient.CallToolAsync("count", cancellationToken: fixture.Token).AsTask();
        await fixture.Until(() => fixture.Count("owner") == 2);
        var blockedFiles = new RouteFiles(blocked);
        var owner = fixture.Events().Single(e => e.GetProperty("name").GetString() == "owner"
            && e.GetProperty("key").GetString() == blockedFiles.Key);
        await fixture.Kill(owner.GetProperty("pid").GetInt32(), owner.GetProperty("ticks").GetInt64());
        blockedFiles.Event("runner-killed-before-ready", owner.GetProperty("generation").GetGuid());
        SpikeChecks.Require((await blockedCall).IsError == true, "Pre-ready host death did not terminate the attempt.");
        await Task.Delay(2100, fixture.Token);
        File.WriteAllText(blocked.BeforeReadyBarrier!, "release");
        await Count(blockedClient, fixture.Token);
        var leaseRelease = Path.Combine(fixture.Root, "lease-release");
        var replacementRelease = Path.Combine(fixture.Root, "replacement-release");
        var ownerReady = Path.Combine(fixture.Root, "owner-ready");
        var beforeLease = new RouteOptions(fixture.Root, "before-lease", BeforeLeaseBarrier: leaseRelease,
            BeforeReadyBarrier: ownerReady);
        var leaseFiles = new RouteFiles(beforeLease);
        var leaseConfig = fixture.Config(beforeLease);
        var doomed = await fixture.Open(leaseConfig, protocol);
        var interrupted = doomed.CallToolAsync("count", cancellationToken: fixture.Token).AsTask();
        await fixture.Until(() => fixture.Events().Any(e => e.GetProperty("key").GetString() == leaseFiles.Key
            && e.GetProperty("name").GetString() == "spawned"));
        var initiator = fixture.Events().Single(e => e.GetProperty("key").GetString() == leaseFiles.Key
            && e.GetProperty("name").GetString() == "launch");
        await fixture.Kill(initiator.GetProperty("pid").GetInt32(), initiator.GetProperty("ticks").GetInt64());
        await Task.Delay(2100, fixture.Token);
        var replacement = await fixture.Open(fixture.Config(beforeLease with
        {
            BeforeLeaseBarrier = replacementRelease,
        }), protocol);
        var replacementCall = Count(replacement, fixture.Token);
        await fixture.Until(() => fixture.Events().Count(e => e.GetProperty("key").GetString() == leaseFiles.Key
            && e.GetProperty("name").GetString() == "spawned") == 2);
        File.WriteAllText(leaseRelease, "release");
        await fixture.Until(() => fixture.Events().Any(e => e.GetProperty("key").GetString() == leaseFiles.Key
            && e.GetProperty("name").GetString() == "owner"));
        File.WriteAllText(replacementRelease, "release");
        await fixture.Until(() => fixture.Events().Any(e => e.GetProperty("key").GetString() == leaseFiles.Key
            && e.GetProperty("name").GetString() == "lease-rejected"));
        await Task.Delay(400, fixture.Token);
        SpikeChecks.Require(!replacementCall.IsCompleted, "Losing a candidate failed the proxy before the surviving owner became ready.");
        File.WriteAllText(ownerReady, "release");
        await replacementCall;
        SpikeChecks.Require(fixture.Events().Count(e => e.GetProperty("key").GetString() == leaseFiles.Key
            && e.GetProperty("name").GetString() == "initialization") == 1,
            "Launcher death before lease caused duplicate initialization.");
        var crashOptions = new RouteOptions(fixture.Root, "crash-loop", CrashBeforeReady: true);
        var crashConfig = fixture.Config(crashOptions);
        var crashClients = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => fixture.Open(crashConfig, protocol)));
        var crashResults = await Task.WhenAll(crashClients.Select(c => c.CallToolAsync("count", cancellationToken: fixture.Token).AsTask()));
        SpikeChecks.Require(crashResults.All(r => r.IsError == true), "Crash loop escaped the bounded request.");
        var crashFiles = new RouteFiles(crashOptions);
        var attempts = Directory.GetFiles(crashFiles.DirectoryPath, "events-*.jsonl")
            .SelectMany(File.ReadAllLines).Count(line => JsonSerializer.Deserialize<JsonElement>(line).GetProperty("name").GetString() == "launch");
        SpikeChecks.Require(attempts == 1, "Crash loop queued launches from waiting proxies.");
        await fixture.DisposeAsync();
        SpikeChecks.Require(fixture.Count("fatal") == 0, "Startup scenario relied on an unexpected host timeout.");
        results.Add(new { scenario = "launcher-host-death-and-crash-loop", protocol, events = fixture.Events() });
        Console.WriteLine($"PASS startup deaths {protocol}");
    }

    private static async Task AllWaitersCancelled(string protocol, List<object> results)
    {
        await using var fixture = new RoutingFixture();
        var release = Path.Combine(fixture.Root, "all-waiters-release");
        var options = new RouteOptions(fixture.Root, "all-waiters", LoadBarrier: release);
        var config = fixture.Config(options);
        var clients = await Task.WhenAll(fixture.Open(config, protocol), fixture.Open(config, protocol));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        var pending = clients.Select(client => client.SendRequestAsync<CallToolRequestParams, CallToolResult>("tools/call",
            new() { Name = "count" }, requestId: new RequestId("all-waiters"),
            cancellationToken: cancelled.Token).AsTask()).ToArray();
        await fixture.Until(() => fixture.Count("load-waiter") == 2);
        await Task.WhenAll(clients.Select(client => client.SendNotificationAsync("notifications/cancelled",
            new CancelledNotificationParams { RequestId = new RequestId("all-waiters") },
            cancellationToken: fixture.Token)));
        await cancelled.CancelAsync();
        foreach (var response in pending)
        {
            try
            {
                await response.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }
        }

        await fixture.Until(() => fixture.Events().Any(e => e.GetProperty("name").GetString() == "load-waiter-ended"
            && e.GetProperty("data").GetProperty("activeWaiters").GetInt32() == 0
            && e.GetProperty("data").GetProperty("cancelledWaiters").GetInt32() == 2));
        SpikeChecks.Require(fixture.Count("load-failure") == 0,
            "Cancelling all waiters aborted the shared attempt despite connected proxies.");
        var joined = Count(clients[0], fixture.Token);
        await fixture.Until(() => fixture.Count("load-waiter") == 3);
        File.WriteAllText(release, "release");
        await joined;
        SpikeChecks.Require(fixture.Count("load-start") == 1 && fixture.Count("load-success") == 1,
            "A new waiter did not join the continuing shared load.");
        await fixture.DisposeAsync();
        results.Add(new { scenario = "all-waiters-cancelled-connected-proxies-keep-load", protocol, events = fixture.Events() });
        Console.WriteLine($"PASS all waiters {protocol}");
    }

    private static async Task Lifecycle(string protocol, List<object> results)
    {
        await using var fixture = new RoutingFixture();
        var beforeClosing = Path.Combine(fixture.Root, "before-closing");
        var exit = Path.Combine(fixture.Root, "exit-release");
        var options = new RouteOptions(fixture.Root, "lifecycle", BeforeClosingBarrier: beforeClosing, ExitBarrier: exit);
        var config = fixture.Config(options);
        var first = await fixture.Open(config, protocol);
        var second = await fixture.Open(config, protocol);
        var before = await Count(first, fixture.Token);
        await Count(second, fixture.Token);
        await fixture.Close(first);
        SpikeChecks.Require(Generation(await Count(second, fixture.Token)) == Generation(before), "Initiator disconnect killed shared host.");
        await fixture.Close(second);
        await fixture.Until(() => fixture.Count("closing-candidate") >= 1);
        var third = await fixture.Open(config, protocol);
        SpikeChecks.Require(Generation(await Count(third, fixture.Token)) == Generation(before),
            "New connection during grace did not retain the generation.");
        File.WriteAllText(beforeClosing, "release");
        SpikeChecks.Require(fixture.Count("closing") == 0, "Admission before closing did not cancel the exit decision.");
        await fixture.Close(third);
        await fixture.Until(() => fixture.Count("closing") == 1);
        var fourth = await fixture.Open(config, protocol);
        var racing = Count(fourth, fixture.Token);
        await fixture.Until(() => fixture.Count("admission-closed") >= 1);
        var files = new RouteFiles(options);
        using (var lease = files.TryLock("host.lock"))
        {
            SpikeChecks.Require(lease is null && fixture.Count("owner") == 1 && fixture.Count("initialization") == 1,
                "A second owner initialized while the exiting host still held the lease.");
        }

        File.WriteAllText(exit, "release");
        var recovered = await racing.WaitAsync(TimeSpan.FromSeconds(9));
        SpikeChecks.Require(Generation(recovered) != Generation(before), "Last disconnect did not end the generation.");
        var proxy = SpikeChecks.Read(await fourth.CallToolAsync("toggle", cancellationToken: fixture.Token));
        using (var process = System.Diagnostics.Process.GetProcessById(proxy.GetProperty("processId").GetInt32()))
        {
            process.Kill();
            await process.WaitForExitAsync(fixture.Token);
        }

        await fixture.Until(() => fixture.Count("exit") == 2);
        SpikeChecks.Require(fixture.Count("owner") == 2 && fixture.Count("initialization") == 2
            && fixture.Count("load-start") == 2 && fixture.Count("lease-released") == 2
            && fixture.Count("exit-barrier-timeout") == 0, "Closing race did not finish with one new owner/load.");
        await fixture.DisposeAsync();
        results.Add(new { scenario = "disconnect-grace-reconnect-and-kill-last-proxy", protocol, events = fixture.Events() });
        Console.WriteLine($"PASS lifecycle {protocol}");
    }

    private static async Task Boundaries(string protocol, List<object> results)
    {
        await using var fixture = new RoutingFixture();
        var options = new RouteOptions(fixture.Root, "boundary");
        var files = new RouteFiles(options);
        File.WriteAllText(files.PathFor("ready"), "stale discovery from another generation");
        File.WriteAllText(files.PathFor("next-launch"), long.MaxValue.ToString());
        File.WriteAllText(files.PathFor("failed-at"), long.MaxValue.ToString());
        var config = fixture.Config(options);
        var first = await fixture.Open(config, protocol, Path.Combine(fixture.Root, "tmp1"));
        var second = await fixture.Open(config, protocol, Path.Combine(fixture.Root, "tmp2"));
        var values = await Task.WhenAll(Count(first, fixture.Token), Count(second, fixture.Token));
        RequireShared(values, "boundary");
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        {
            await using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", files.PipeName,
                System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
            await pipe.ConnectAsync(deadline.Token);
            await Transport.LocalHandshake.WriteValue(pipe,
                new RouteHello(BuildIdentity.Value, files.Key, "wrong-workspace", Guid.Empty), deadline.Token);
            var buffer = new byte[1];
            try
            {
                SpikeChecks.Require(await pipe.ReadAsync(buffer, deadline.Token) == 0, "Wrong descriptor was admitted.");
            }
            catch (IOException)
            {
            }
        }

        SpikeChecks.Require(Generation(await Count(first, fixture.Token)) == Generation(values[0]),
            "Incompatible hello changed the workspace.");
        var slow = await first.CallToolAsync("slow", new Dictionary<string, object?> { ["seconds"] = 16 },
            cancellationToken: fixture.Token);
        SpikeChecks.Require(slow.IsError != true, "The legacy 15-second forwarding deadline remains.");

        var copiedSource = Path.Combine(fixture.Root, "source");
        Directory.CreateDirectory(copiedSource);
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
        {
            File.Copy(file, Path.Combine(copiedSource, Path.GetFileName(file)));
        }

        var copiedOptions = new RouteOptions(fixture.Root, "publish");
        var copiedConfig = fixture.Config(copiedOptions);
        var copiedClient = await fixture.Open(copiedConfig, protocol,
            assembly: Path.Combine(copiedSource, "PipeRelay.dll"));
        var publishBefore = await Count(copiedClient, fixture.Token);
        var survivor = await fixture.Open(copiedConfig, protocol);
        await Count(survivor, fixture.Token);
        await fixture.Close(copiedClient);
        var original = Path.Combine(copiedSource, "PipeRelay.dll");
        await using (var update = new FileStream(original, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            update.Seek(0, SeekOrigin.End);
            await update.WriteAsync(new byte[] { 0 }, fixture.Token);
        }

        SpikeChecks.Require(Generation(await Count(survivor, fixture.Token)) == Generation(publishBefore),
            "Updating the source publish invalidated the shadow host.");
        var partial = new RouteOptions(fixture.Root, "partial-image");
        var partialFiles = new RouteFiles(partial);
        Directory.CreateDirectory(partialFiles.PathFor("image"));
        File.WriteAllText(Path.Combine(partialFiles.PathFor("image"), "PipeRelay.runtimeconfig.json"), "partial");
        var partialClient = await fixture.Open(fixture.Config(partial), protocol);
        SpikeChecks.Require((await partialClient.CallToolAsync("count", cancellationToken: fixture.Token)).IsError == true
            && !fixture.Events().Any(e => e.GetProperty("key").GetString() == partialFiles.Key
                && e.GetProperty("name").GetString() is "launch" or "owner"), "Partial image was launched.");
        await fixture.DisposeAsync();
        results.Add(new { scenario = "tmpdir-stale-descriptor-deadline-publish-partial-image", protocol, events = fixture.Events() });
        Console.WriteLine($"PASS boundaries {protocol}");
    }

    private static async Task<JsonElement> Count(McpClient client, CancellationToken cancellationToken) =>
        SpikeChecks.Read(await client.CallToolAsync("count", cancellationToken: cancellationToken));

    private static string Generation(JsonElement value) => value.GetProperty("value").GetProperty("generation").GetString()!;

    private static void RequireShared(JsonElement[] values, string descriptor)
    {
        SpikeChecks.Require(values.All(value => value.GetProperty("descriptor").GetString() == descriptor)
            && values.Select(Generation).Distinct().Count() == 1
            && values.Select(v => v.GetProperty("value").GetProperty("count").GetInt32()).Distinct().Count() == values.Length,
            "Calls did not share one workspace/generation/counter.");
    }

    private sealed class InlineProgress(Action<ProgressNotificationValue> action) : IProgress<ProgressNotificationValue>
    {
        public void Report(ProgressNotificationValue value) => action(value);
    }
}
