using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcpServer.Hosting;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class McpToolGroupEnablementIntegrationTests
{
    [Fact]
    public async Task Lite_session_can_enable_files_and_invoke_list_directory_tree()
    {
        using var host = BuildLiteHost();
        var options = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        await using var serverTransport = new StreamServerTransport(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream(),
            serverName: "epoch3-test");

        await using var server = McpServer.Create(
            serverTransport,
            options,
            host.Services.GetService<ILoggerFactory>(),
            host.Services);
        var serverTask = server.RunAsync(cts.Token);

        var notifications = 0;
        await using var client = await McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream()),
            new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "epoch3-test", Version = "1.0" },
                Handlers = new McpClientHandlers
                {
                    NotificationHandlers =
                    [
                        new(
                            NotificationMethods.ToolListChangedNotification,
                            (_, _) =>
                            {
                                Interlocked.Increment(ref notifications);
                                return ValueTask.CompletedTask;
                            }),
                    ],
                },
            },
            cancellationToken: cts.Token);

        try
        {
            var before = await client.ListToolsAsync(cancellationToken: cts.Token);
            Assert.DoesNotContain(before, t => t.Name == "list_directory_tree");
            Assert.Contains(before, t => t.Name == "enable_tool_group");

            var enable = await client.CallToolAsync(
                "enable_tool_group",
                new Dictionary<string, object?> { ["group"] = "files" },
                cancellationToken: cts.Token);
            Assert.False(enable.IsError ?? false);
            Assert.Contains("`list_directory_tree`", ReadText(enable), StringComparison.Ordinal);

            await WaitForAsync(() => Volatile.Read(ref notifications) >= 1, cts.Token);
            Assert.Equal(1, Volatile.Read(ref notifications));

            var after = await client.ListToolsAsync(cancellationToken: cts.Token);
            var tree = Assert.Single(after, t => t.Name == "list_directory_tree");
            Assert.Equal(JsonValueKind.Object, tree.JsonSchema.ValueKind);
            Assert.True(
                tree.JsonSchema.TryGetProperty("properties", out var properties)
                && properties.TryGetProperty("directoryPath", out _),
                "list_directory_tree schema should include directoryPath.");

            var temp = Directory.CreateTempSubdirectory("mcp-epoch3-");
            try
            {
                await File.WriteAllTextAsync(Path.Combine(temp.FullName, "readme.txt"), "ok", cts.Token);
                var listed = await client.CallToolAsync(
                    "list_directory_tree",
                    new Dictionary<string, object?>
                    {
                        ["directoryPath"] = temp.FullName,
                        ["maxDepth"] = 1,
                    },
                    cancellationToken: cts.Token);

                Assert.False(listed.IsError ?? false);
                Assert.Contains("readme.txt", ReadText(listed), StringComparison.Ordinal);
            }
            finally
            {
                temp.Delete(recursive: true);
            }

            var noop = await client.CallToolAsync(
                "enable_tool_group",
                new Dictionary<string, object?> { ["group"] = "files" },
                cancellationToken: cts.Token);
            Assert.False(noop.IsError ?? false);
            Assert.Contains("already active", ReadText(noop), StringComparison.Ordinal);
            await Task.Delay(200, cts.Token);
            Assert.Equal(1, Volatile.Read(ref notifications));
        }
        finally
        {
            await client.DisposeAsync();
            cts.Cancel();
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static IHost BuildLiteHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "lite" });
        return builder.Build();
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
}
