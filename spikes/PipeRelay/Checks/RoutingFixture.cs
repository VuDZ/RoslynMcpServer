using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PipeRelay.Routing;
using PipeRelay.Routing.Models;

namespace PipeRelay.Checks;

internal sealed class RoutingFixture : IAsyncDisposable
{
    public string Root { get; } = Path.Combine(OperatingSystem.IsWindows() ? AppContext.BaseDirectory : "/tmp",
        "r4-" + Guid.NewGuid().ToString("N")[..8]);
    public CancellationToken Token => _deadline.Token;
    public List<McpClient> Clients { get; } = [];
    public int Changes => Volatile.Read(ref _changes);
    public int Acknowledged => Volatile.Read(ref _acknowledged);

    public string Config(RouteOptions options)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, "config-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(options));
        return path;
    }

    public async Task<McpClient> Open(string config, string protocol, string? temporaryDirectory = null,
        string? assembly = null)
    {
        var start = SpikeChecks.StartInfo("route", config);
        if (assembly is not null)
        {
            start.ArgumentList[0] = assembly;
        }

        if (temporaryDirectory is not null)
        {
            Directory.CreateDirectory(temporaryDirectory);
            start.Environment["TMPDIR"] = temporaryDirectory;
        }

        var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        McpClient client;
        try
        {
            client = await McpClient.CreateAsync(new StreamClientTransport(
                process.StandardInput.BaseStream, process.StandardOutput.BaseStream), new()
        {
            ProtocolVersion = protocol,
            DiscoverProbeTimeout = Timeout.InfiniteTimeSpan,
            InitializationTimeout = TimeSpan.FromSeconds(30),
            Handlers = new()
            {
                NotificationHandlers =
                [
                    new("notifications/tools/list_changed", (_, _) =>
                    {
                        Interlocked.Increment(ref _changes);
                        return ValueTask.CompletedTask;
                    }),
                    new(NotificationMethods.SubscriptionsAcknowledgedNotification, (_, _) =>
                    {
                        Interlocked.Increment(ref _acknowledged);
                        return ValueTask.CompletedTask;
                    }),
                ],
            },
            }, cancellationToken: Token);
        }
        catch (Exception exception)
        {
            process.Kill();
            await process.WaitForExitAsync(Token);
            throw new InvalidOperationException("Frontend initialization failed: " + await errors, exception);
        }

        lock (Clients)
        {
            Clients.Add(client);
            _processes.Add(client, process);
            _errors.Add(errors);
        }

        return client;
    }

    public async Task Close(McpClient client)
    {
        Process? process;
        lock (_processes)
        {
            if (!_processes.Remove(client, out process))
            {
                return;
            }
        }

        // SDK StdioClientSessionTransport 2.2 waits, then kills the process tree before closing stdin.
        // Use direct EOF so ordinary disconnect is observed separately from harness tree termination.
        process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync();
            }

            await client.DisposeAsync();
            process.Dispose();
        }
    }

    public async Task Listen(McpClient client)
    {
        var expected = Acknowledged + 1;
        _listens.Add(client.SendRequestAsync<SubscriptionsListenRequestParams, EmptyResult>("subscriptions/listen", new()
        {
            Notifications = new() { ToolsListChanged = true },
        }, cancellationToken: Token).AsTask());
        await Until(() => Acknowledged == expected);
    }

    public JsonElement[] Events() => Directory.Exists(Root)
        ? Directory.GetFiles(Root, "events-*.jsonl", SearchOption.AllDirectories)
            .SelectMany(path => ReadEvents(path)
                .Select(line => JsonSerializer.Deserialize<JsonElement>(line))).ToArray()
        : [];

    public int Count(string name) => Events().Count(e => e.GetProperty("name").GetString() == name);

    public async Task Until(Func<bool> condition, int seconds = 10)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    public async Task KillOwner(RouteOptions options)
    {
        var ready = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(new RouteFiles(options).PathFor("ready")));
        await Kill(ready.GetProperty("pid").GetInt32(), ready.GetProperty("ticks").GetInt64());
    }

    public async Task Kill(int pid, long ticks)
    {
        using var process = Process.GetProcessById(pid);
        SpikeChecks.Require(ProcessIdentity.Stamp(pid) == ticks, "Fixture PID was reused.");
        process.Kill();
        await process.WaitForExitAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await Task.WhenAll(Clients.Select(Close));
        await Task.WhenAll(_errors);

        await _deadline.CancelAsync();
        foreach (var listen in _listens)
        {
            try
            {
                await listen;
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or ModelContextProtocol.McpException)
            {
            }
        }

        await Task.Delay(1300);
        var leaked = new List<int>();
        var events = Events();
        var children = events.Where(e => e.GetProperty("name").GetString() == "owner")
            .Concat(events.Where(e => e.GetProperty("name").GetString() == "spawned")
                .Select(e => e.GetProperty("data")))
            .DistinctBy(e => (e.GetProperty("pid").GetInt32(), e.GetProperty("ticks").GetInt64()));
        foreach (var record in children)
        {
            try
            {
                using var process = Process.GetProcessById(record.GetProperty("pid").GetInt32());
                if (!process.HasExited && ProcessIdentity.Stamp(process.Id) == record.GetProperty("ticks").GetInt64())
                {
                    // Failed scenarios still clean only their own recorded process; the leak remains a test failure.
                    leaked.Add(process.Id);
                    process.Kill();
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception exception) when (exception is ArgumentException or FileNotFoundException or DirectoryNotFoundException)
            {
            }
        }

        _deadline.Dispose();
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "routing-fixture-last.json"),
            JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, root = Root, events = Events(), leaked }));
        SpikeChecks.Require(leaked.Count == 0, "Hosts survived final disconnect: " + string.Join(",", leaked));
    }

    private static string[] ReadEvents(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        var lastNewline = text.LastIndexOf('\n');
        return lastNewline < 0 ? [] : text[..lastNewline].Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(90));
    private readonly List<Task<EmptyResult>> _listens = [];
    private int _changes;
    private int _acknowledged;
    private bool _disposed;
    private readonly Dictionary<McpClient, Process> _processes = [];
    private readonly List<Task<string>> _errors = [];
}
