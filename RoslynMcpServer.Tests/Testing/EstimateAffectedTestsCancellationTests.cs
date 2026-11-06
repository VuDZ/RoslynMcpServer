using System.Collections.Concurrent;
using System.IO.Pipelines;
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

namespace RoslynMcpServer.Tests.Testing;

[Collection("BaselineObservationGap")]
public sealed class EstimateAffectedTestsCancellationTests
{
    [Fact]
    public async Task Tools_call_cancellation_kills_the_process_tree_skips_later_projects_and_logs_all_outcomes()
    {
        using var logs = new RecordingLogs();
        var toolRequest = new TaskCompletionSource<RequestId>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "lite" });
        builder.Services.Configure<McpServerOptions>(options => options.Filters.Message.IncomingFilters.Insert(0,
            next => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcRequest { Method: "tools/call" } callRequest)
                {
                    toolRequest.TrySetResult(callRequest.Id);
                }
                await next(context, cancellationToken).ConfigureAwait(false);
            }));
        using var host = builder.Build();
        await using var fixture = new TestImpactRunFixture(host.Services.GetRequiredService<SolutionManager>());
        await fixture.LoadAsync();
        await fixture.ChangeAsync("First", fixture.HangingBody());
        await fixture.ChangeAsync("Second", "GC.KeepAlive(value); Assert.True(true);");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        await using var transport = new StreamServerTransport(
            clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream(), "test-impact-cancellation");
        await using var server = McpServer.Create(transport,
            host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value,
            host.Services.GetRequiredService<ILoggerFactory>(), host.Services);
        var serving = server.RunAsync(lifetime.Token);
        await using var client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
            new McpClientOptions
            {
                ProtocolVersion = McpInitializeHandshake.ProtocolVersion,
                ClientInfo = new Implementation { Name = "test-impact-cancellation", Version = "1.0" }
            }, cancellationToken: lifetime.Token);
        using var request = new CancellationTokenSource();
        int[] ids = [];
        try
        {
            var operation = client.CallToolAsync("estimate_affected_tests",
                new Dictionary<string, object?> { ["run"] = true, ["timeoutSeconds"] = 0 },
                cancellationToken: request.Token).AsTask();
            ids = await fixture.WaitForProcessesAsync(operation);
            // Use the observed id so cancellation traverses the actual MCP notification handler.
            await client.SendNotificationAsync(NotificationMethods.CancelledNotification,
                new CancelledNotificationParams { RequestId = await toolRequest.Task, Reason = "Test requested cancellation." },
                McpJsonUtilities.DefaultOptions, cancellationToken: lifetime.Token);
            await request.CancelAsync();
            try
            {
                await operation;
            }
            catch (OperationCanceledException)
            {
            }
            // MCP clients may stop waiting before the server has finished killing its child processes.
            await TestImpactRunFixture.AssertStoppedAsync(ids);
            using var logDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!logs.Messages.Any(message => message.StartsWith("Rapid TIA run:", StringComparison.Ordinal)))
            {
                await Task.Delay(25, logDeadline.Token);
            }
            var summary = Assert.Single(logs.Messages,
                message => message.StartsWith("Rapid TIA run:", StringComparison.Ordinal));
            Assert.Contains("First.csproj=cancelled", summary, StringComparison.Ordinal);
            Assert.Contains("Second.csproj=not run", summary, StringComparison.Ordinal);
            Assert.DoesNotContain('\n', summary);
            Assert.False(File.Exists(fixture.MarkerPath("Second")));
            var empty = await client.CallToolAsync("get_changed_files", cancellationToken: lifetime.Token);
            Assert.False(empty.IsError ?? false);
        }
        finally
        {
            await request.CancelAsync();
            TestImpactRunFixture.Kill(ids);
            await lifetime.CancelAsync();
            try
            {
                await serving;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private sealed class RecordingLogs : ILoggerProvider
    {
        internal ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}
