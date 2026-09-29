using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcpServer.Hosting;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class McpToolGroupEnablementIntegrationTests
{
    [Fact]
    public async Task Initialize_handshake_receives_one_tools_list_changed_without_listen()
    {
        var notifications = 0;
        await using var session = await LiteSession.StartAsync(
            McpInitializeHandshake.ProtocolVersion,
            ListChangedHandlers(() => Interlocked.Increment(ref notifications)));

        Assert.Equal(McpInitializeHandshake.ProtocolVersion, session.Client.NegotiatedProtocolVersion);

        var before = await session.Client.ListToolsAsync(cancellationToken: session.Token);
        Assert.DoesNotContain(before, t => t.Name == "list_directory_tree");
        Assert.Contains(before, t => t.Name == "enable_tool_group");

        var enable = await session.Client.CallToolAsync(
            "enable_tool_group",
            new Dictionary<string, object?> { ["group"] = "files" },
            cancellationToken: session.Token);
        Assert.False(enable.IsError ?? false);
        Assert.Contains("`list_directory_tree`", ReadText(enable), StringComparison.Ordinal);

        await WaitForAsync(() => Volatile.Read(ref notifications) >= 1, session.Token);
        Assert.Equal(1, Volatile.Read(ref notifications));

        var after = await session.Client.ListToolsAsync(cancellationToken: session.Token);
        var tree = Assert.Single(after, t => t.Name == "list_directory_tree");
        Assert.Equal(JsonValueKind.Object, tree.JsonSchema.ValueKind);
        Assert.True(
            tree.JsonSchema.TryGetProperty("properties", out var properties)
            && properties.TryGetProperty("directoryPath", out _),
            "list_directory_tree schema should include directoryPath.");

        var temp = Directory.CreateTempSubdirectory("mcp-epoch3-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(temp.FullName, "readme.txt"), "ok", session.Token);
            var listed = await session.Client.CallToolAsync(
                "list_directory_tree",
                new Dictionary<string, object?>
                {
                    ["directoryPath"] = temp.FullName,
                    ["maxDepth"] = 1,
                },
                cancellationToken: session.Token);

            Assert.False(listed.IsError ?? false);
            Assert.Contains("readme.txt", ReadText(listed), StringComparison.Ordinal);
        }
        finally
        {
            temp.Delete(recursive: true);
        }

        var noop = await session.Client.CallToolAsync(
            "enable_tool_group",
            new Dictionary<string, object?> { ["group"] = "files" },
            cancellationToken: session.Token);
        Assert.False(noop.IsError ?? false);
        Assert.Contains("already active", ReadText(noop), StringComparison.Ordinal);
        var second = await BecameTrueAsync(() => Volatile.Read(ref notifications) >= 2, TimeSpan.FromMilliseconds(200), session.Token);
        Assert.False(second);
        Assert.Equal(1, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task July_2026_client_gets_tools_list_changed_only_after_subscriptions_listen()
    {
        var notifications = 0;
        var acknowledged = 0;
        await using var session = await LiteSession.StartAsync(
            McpInitializeHandshake.SubscriptionProtocolVersion,
            ListChangedHandlers(
                () => Interlocked.Increment(ref notifications),
                () => Interlocked.Increment(ref acknowledged)));

        Assert.Equal(McpInitializeHandshake.SubscriptionProtocolVersion, session.Client.NegotiatedProtocolVersion);

        var enableFiles = await session.Client.CallToolAsync(
            "enable_tool_group",
            new Dictionary<string, object?> { ["group"] = "files" },
            cancellationToken: session.Token);
        Assert.False(enableFiles.IsError ?? false);

        // The revision does not push list changes onto the session. A short wait is the negative check.
        var pushedEarly = await BecameTrueAsync(
            () => Volatile.Read(ref notifications) >= 1,
            TimeSpan.FromMilliseconds(300),
            session.Token);
        Assert.False(pushedEarly);
        Assert.Equal(0, Volatile.Read(ref notifications));

        var afterFiles = await session.Client.ListToolsAsync(cancellationToken: session.Token);
        Assert.Contains(afterFiles, t => t.Name == "list_directory_tree");

        await using var listen = session.ListenForToolListChanges();
        var acknowledgedTask = WaitForAsync(() => Volatile.Read(ref acknowledged) >= 1, session.Token);
        var finished = await Task.WhenAny(acknowledgedTask, listen.Completion);
        if (finished == listen.Completion)
        {
            await listen.Completion;
        }

        await acknowledgedTask;

        var enableNavigation = await session.Client.CallToolAsync(
            "enable_tool_group",
            new Dictionary<string, object?> { ["group"] = "navigation" },
            cancellationToken: session.Token);
        Assert.False(enableNavigation.IsError ?? false);
        await WaitForAsync(() => Volatile.Read(ref notifications) >= 1, session.Token);
        Assert.Equal(1, Volatile.Read(ref notifications));
    }

    private static McpClientHandlers ListChangedHandlers(Action onListChanged, Action? onAcknowledged = null)
    {
        List<KeyValuePair<string, Func<JsonRpcNotification, CancellationToken, ValueTask>>> handlers =
        [
            new(
                NotificationMethods.ToolListChangedNotification,
                (_, _) =>
                {
                    onListChanged();
                    return ValueTask.CompletedTask;
                }),
        ];
        if (onAcknowledged is not null)
        {
            handlers.Add(new(
                NotificationMethods.SubscriptionsAcknowledgedNotification,
                (_, _) =>
                {
                    onAcknowledged();
                    return ValueTask.CompletedTask;
                }));
        }

        return new McpClientHandlers { NotificationHandlers = handlers };
    }

    private static string ReadText(CallToolResult result) =>
        string.Join(
            Environment.NewLine,
            result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(20, cancellationToken);
        }
    }

    private static async Task<bool> BecameTrueAsync(
        Func<bool> condition,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            while (!condition())
            {
                await Task.Delay(20, timeoutSource.Token);
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static IHost BuildLiteHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "lite" });
        return builder.Build();
    }

    private sealed class LiteSession : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly CancellationTokenSource _cts;
        private readonly McpServer _server;
        private readonly StreamServerTransport _serverTransport;
        private readonly Task _serverTask;
        private readonly McpClient _client;

        private LiteSession(
            IHost host,
            CancellationTokenSource cts,
            McpServer server,
            StreamServerTransport serverTransport,
            Task serverTask,
            McpClient client)
        {
            _host = host;
            _cts = cts;
            _server = server;
            _serverTransport = serverTransport;
            _serverTask = serverTask;
            _client = client;
        }

        public McpClient Client => _client;

        public CancellationToken Token => _cts.Token;

        public static async Task<LiteSession> StartAsync(string protocolVersion, McpClientHandlers handlers)
        {
            var host = BuildLiteHost();
            var options = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            var serverTransport = new StreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream(),
                serverName: "group-enablement-test");
            var server = McpServer.Create(
                serverTransport,
                options,
                host.Services.GetService<ILoggerFactory>(),
                host.Services);
            var serverTask = server.RunAsync(cts.Token);
            try
            {
                var client = await McpClient.CreateAsync(
                    new StreamClientTransport(
                        clientToServer.Writer.AsStream(),
                        serverToClient.Reader.AsStream()),
                    new McpClientOptions
                    {
                        ProtocolVersion = protocolVersion,
                        ClientInfo = new Implementation { Name = "group-enablement-test", Version = "1.0" },
                        Handlers = handlers,
                    },
                    cancellationToken: cts.Token);
                return new LiteSession(host, cts, server, serverTransport, serverTask, client);
            }
            catch
            {
                await cts.CancelAsync();
                try
                {
                    await serverTask;
                }
                catch (OperationCanceledException)
                {
                }

                await server.DisposeAsync();
                await serverTransport.DisposeAsync();
                host.Dispose();
                cts.Dispose();
                throw;
            }
        }

        /// <summary>
        /// The listen request stays open until cancelled. Notifications are what the caller waits for.
        /// </summary>
        public ToolListListen ListenForToolListChanges()
        {
            var listenCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var listen = _client.SendRequestAsync<SubscriptionsListenRequestParams, EmptyResult>(
                RequestMethods.SubscriptionsListen,
                new SubscriptionsListenRequestParams
                {
                    Notifications = new SubscriptionsListenNotifications { ToolsListChanged = true },
                },
                McpJsonUtilities.DefaultOptions,
                cancellationToken: listenCancellation.Token);
            return new ToolListListen(listenCancellation, listen);
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            await _cts.CancelAsync();
            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException)
            {
            }

            await _server.DisposeAsync();
            await _serverTransport.DisposeAsync();
            _host.Dispose();
            _cts.Dispose();
        }
    }

    private sealed class ToolListListen : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task<EmptyResult> _listen;

        public ToolListListen(CancellationTokenSource cts, ValueTask<EmptyResult> listen)
        {
            _cts = cts;
            _listen = listen.AsTask();
        }

        public Task<EmptyResult> Completion => _listen;

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try
            {
                await _listen;
            }
            catch (OperationCanceledException)
            {
            }
            catch (McpException) when (_cts.IsCancellationRequested)
            {
            }

            _cts.Dispose();
        }
    }
}
