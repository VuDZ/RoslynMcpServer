using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics.UsageStatistics;
using RoslynMcpServer.Hosting;
using Xunit;

namespace RoslynMcpServer.Tests.UsageStatistics;

public sealed class UsageStatisticsFiltersTests
{
    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task Real_protocol_filters_capture_client_identity_and_legacy_tool_errors_without_arguments(string protocolVersion)
    {
        using var files = new UsageTestFiles();
        using var usage = new UsageStatisticsService(files.UsageDirectory);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(usage);
        builder.Services.AddRoslynMcpServerTools();
        using var host = builder.Build();
        await usage.StartAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var input = new Pipe();
        var output = new Pipe();
        await using var transport = new StreamServerTransport(input.Reader.AsStream(), output.Writer.AsStream(), "usage-test");
        await using var server = McpServer.Create(
            transport,
            host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value,
            host.Services.GetService<ILoggerFactory>(),
            host.Services);
        var running = server.RunAsync(cancellation.Token);
        try
        {
            await using var client = await McpClient.CreateAsync(
                new StreamClientTransport(input.Writer.AsStream(), output.Reader.AsStream()),
                new McpClientOptions
                {
                    ProtocolVersion = protocolVersion,
                    ClientInfo = new Implementation { Name = "usage-integration", Version = "1.0" },
                },
                cancellationToken: cancellation.Token);
            var help = await client.CallToolAsync(
                "get_tool_help",
                new Dictionary<string, object?> { ["toolName"] = "read_file_range" },
                cancellationToken: cancellation.Token);
            Assert.False(help.IsError ?? false);

            var failure = await client.CallToolAsync(
                "read_file_range",
                new Dictionary<string, object?>
                {
                    ["filePath"] = Path.Combine(files.Root, "private-argument-sentinel.txt"),
                    ["startLine"] = 1,
                    ["lineCount"] = 1,
                },
                cancellationToken: cancellation.Token);
            Assert.False(failure.IsError ?? false);
            Assert.Contains(failure.Content.OfType<TextContentBlock>(), content => content.Text.StartsWith("Error:"));
        }
        finally
        {
            await cancellation.CancelAsync();
            try
            {
                await running;
            }
            catch (OperationCanceledException)
            {
            }

            await usage.StopAsync(CancellationToken.None);
        }

        var events = files.ReadEvents();
        var clientEvent = Assert.Single(events.Where(item => item.GetProperty("ev").GetString() == "client"
            && item.GetProperty("name").GetString() == "usage-integration"));
        Assert.Equal(protocolVersion, clientEvent.GetProperty("protocol").GetString());
        var calls = events.Where(item => item.GetProperty("ev").GetString() == "tool").ToArray();
        Assert.Equal(2, calls.Length);
        Assert.True(calls.Single(item => item.GetProperty("name").GetString() == "get_tool_help").GetProperty("success").GetBoolean());
        Assert.False(calls.Single(item => item.GetProperty("name").GetString() == "read_file_range").GetProperty("success").GetBoolean());
        Assert.All(calls, item => Assert.False(item.GetProperty("requires_workspace").GetBoolean()));
        Assert.DoesNotContain(events, item => item.GetRawText().Contains("private-argument-sentinel"));
        Assert.DoesNotContain(events, item => item.TryGetProperty("arguments", out _));
    }

    [Fact]
    public void Decompiler_requires_workspace_only_without_an_explicit_assembly_path()
    {
        var descriptor = McpToolCatalog.All.Single(tool => tool.Name == "decompile_type");
        Assert.True(UsageStatisticsFilters.UsesWorkspace(descriptor, new CallToolRequestParams { Name = descriptor.Name }));
        Assert.False(UsageStatisticsFilters.UsesWorkspace(descriptor, new CallToolRequestParams
        {
            Name = descriptor.Name,
            Arguments = new Dictionary<string, JsonElement> { ["assemblyPath"] = JsonSerializer.SerializeToElement("example.dll") },
        }));
    }
}
