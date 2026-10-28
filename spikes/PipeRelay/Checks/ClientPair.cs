using System.Diagnostics;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PipeRelay.Checks;

internal sealed class ClientPair : IAsyncDisposable
{
    public McpClient First { get; private set; } = null!;
    public McpClient Second { get; private set; } = null!;
    public Process Host { get; private set; } = null!;
    public string PipeName { get; } = "pr-" + Guid.NewGuid().ToString("N");
    public string DirectoryPath { get; } = Path.Combine(AppContext.BaseDirectory, "checks", Guid.NewGuid().ToString("N"));
    public CancellationToken Token => _deadline.Token;
    public int FirstChanges => Volatile.Read(ref _firstChanges);
    public int SecondChanges => Volatile.Read(ref _secondChanges);
    public int Acknowledged => Volatile.Read(ref _acknowledged);

    public static async Task<ClientPair> Open(string protocol, bool roles)
    {
        var pair = new ClientPair();
        Directory.CreateDirectory(pair.DirectoryPath);
        var ready = Path.Combine(pair.DirectoryPath, "ready");
        try
        {
            if (!roles)
            {
                pair._separateHost = Process.Start(SpikeChecks.StartInfo("host", pair.PipeName, ready))!;
                pair._hostOutput = pair._separateHost.StandardOutput.ReadToEndAsync();
                pair._hostErrors = pair._separateHost.StandardError.ReadToEndAsync();
                await pair.WaitReady(ready);
            }

            pair.First = await McpClient.CreateAsync(
                SpikeChecks.Transport(roles ? ["auto", pair.PipeName, ready, "--local-host"] : ["auto", pair.PipeName, ready]),
                new() { ProtocolVersion = protocol, Handlers = pair.Handlers(first: true) }, cancellationToken: pair.Token);
            await pair.WaitReady(ready);
            pair.Host = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(ready, pair.Token)));
            pair._hostStart = pair.Host.StartTime.ToUniversalTime().Ticks;
            pair.Second = await McpClient.CreateAsync(SpikeChecks.Transport("auto", pair.PipeName, ready),
                new() { ProtocolVersion = protocol, Handlers = pair.Handlers(first: false) }, cancellationToken: pair.Token);
            SpikeChecks.Require(pair.First.NegotiatedProtocolVersion == protocol && pair.Second.NegotiatedProtocolVersion == protocol,
                "Clients must use the requested protocol revision.");
            return pair;
        }
        catch
        {
            await pair.DisposeAsync();
            throw;
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

    public async Task Until(Func<bool> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, deadline.Token);
        }
    }

    public async Task KillHost()
    {
        SpikeChecks.Require(Host.StartTime.ToUniversalTime().Ticks == _hostStart
            && Host.MainModule!.FileName == Environment.ProcessPath, "Only this fixture's original host may be killed.");
        Host.Kill();
        await Host.WaitForExitAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _deadline.CancelAsync();
        if (Second is not null)
        {
            await Second.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }

        if (First is not null)
        {
            await First.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }

        if (_separateHost is not null)
        {
            if (!_separateHost.HasExited)
            {
                _separateHost.Kill();
            }

            await _separateHost.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(_hostOutput!, _hostErrors!);
            _separateHost.Dispose();
        }

        foreach (var listen in _listens)
        {
            try
            {
                await listen;
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or McpException)
            {
            }
        }

        if (Host is not null)
        {
            SpikeChecks.Require(Host.HasExited, "A spike host survived fixture cleanup.");
            Host.Dispose();
        }

        _deadline.Dispose();
    }

    private async Task WaitReady(string ready)
    {
        await Until(() => File.Exists(ready) && new FileInfo(ready).Length > 0);
    }

    private McpClientHandlers Handlers(bool first) => new()
    {
        NotificationHandlers =
        [
            new("notifications/tools/list_changed", (_, _) =>
            {
                if (first)
                {
                    Interlocked.Increment(ref _firstChanges);
                }
                else
                {
                    Interlocked.Increment(ref _secondChanges);
                }

                return ValueTask.CompletedTask;
            }),
            new(NotificationMethods.SubscriptionsAcknowledgedNotification, (_, _) =>
            {
                Interlocked.Increment(ref _acknowledged);
                return ValueTask.CompletedTask;
            }),
        ],
    };

    private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(60));
    private readonly List<Task<EmptyResult>> _listens = [];
    private Process? _separateHost;
    private Task<string>? _hostOutput;
    private Task<string>? _hostErrors;
    private long _hostStart;
    private int _firstChanges;
    private int _secondChanges;
    private int _acknowledged;
}
