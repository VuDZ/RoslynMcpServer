using System.IO.Pipelines;
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
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "full" });
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

            await client.CallToolAsync(
                "decompile_type",
                new Dictionary<string, object?>
                {
                    ["assemblyName"] = "private-argument-sentinel",
                    ["fullTypeName"] = "PrivateArgumentSentinel.Type",
                },
                cancellationToken: cancellation.Token);
            await client.CallToolAsync(
                "decompile_type",
                new Dictionary<string, object?>
                {
                    ["assemblyPath"] = Path.Combine(files.Root, "private-argument-sentinel.dll"),
                    ["fullTypeName"] = "PrivateArgumentSentinel.Type",
                },
                cancellationToken: cancellation.Token);
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
        Assert.Equal(4, calls.Length);
        Assert.True(calls.Single(item => item.GetProperty("name").GetString() == "get_tool_help").GetProperty("success").GetBoolean());
        Assert.False(calls.Single(item => item.GetProperty("name").GetString() == "read_file_range").GetProperty("success").GetBoolean());
        Assert.Equal(2, calls.Count(item => item.GetProperty("name").GetString() == "decompile_type"));
        Assert.All(calls, item =>
        {
            Assert.False(item.GetProperty("requires_workspace").GetBoolean());
            Assert.False(item.GetProperty("lazy_load").GetBoolean());
        });
        Assert.All(events.Where(item => item.GetProperty("ev").GetString() == "tool_start"),
            item => Assert.False(item.GetProperty("requires_workspace").GetBoolean()));
        Assert.DoesNotContain(events, item => item.GetRawText().Contains("private-argument-sentinel"));
        Assert.DoesNotContain(events, item => item.GetRawText().Contains("PrivateArgumentSentinel"));
        Assert.DoesNotContain(events, item => item.TryGetProperty("arguments", out _));
    }

    [Fact]
    public void Test_outcomes_distinguish_zero_failure_counters_from_errors()
    {
        // Summary samples in theory display names would resemble extra test-run summaries.
        (string Text, bool Success)[] samples =
        [
            ("Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13", true),
            ("## Filtered tests passed\nTotal: **1486** · Passed: **1486** · Failed: **0**", true),
            ("## dotnet command\n### StdOut\n```text\nPassed! - Failed: 0, Passed: 13\n```", true),
            ("Total: **2** · Passed: **1** · Failed: **1**", false),
            ("Failed! - Failed: 10, Passed: 0, Total: 10", false),
            ("Failed: 01", false),
            ("Failed: Unable to enumerate tests.", false),
            ("Failed: 0, Passed: 1\nFailed: 1, Passed: 0", false),
            ("Error: Failed: 0", false),
            ("**Status:** partial\nFailed: **0**", false),
        ];
        foreach (var sample in samples)
        {
            Assert.Equal(sample.Success, UsageStatisticsFilters.IsSuccessfulText(sample.Text));
        }
    }
}
