using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class BuildProgressIntegrationTests
{
    [Fact]
    public async Task Run_dotnet_build_reports_protocol_progress_for_a_live_build()
    {
        using var host = BuildLiteHost();
        var options = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        await using var serverTransport = new StreamServerTransport(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream(),
            serverName: "build-progress-test");

        await using var server = McpServer.Create(
            serverTransport,
            options,
            host.Services.GetService<ILoggerFactory>(),
            host.Services);
        var serverTask = server.RunAsync(cts.Token);

        await using var client = await McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream()),
            new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "build-progress-test", Version = "1.0" },
            },
            cancellationToken: cts.Token);

        var temp = Directory.CreateTempSubdirectory("mcp-build-progress-");
        try
        {
            var csproj = Path.Combine(temp.FullName, "Slow.csproj");
            await File.WriteAllTextAsync(csproj, SlowProject(), cts.Token);

            var progress = new CollectingProgress();
            var result = await client.CallToolAsync(
                "run_dotnet_build",
                new Dictionary<string, object?> { ["workspacePath"] = csproj },
                progress,
                cancellationToken: cts.Token);

            Assert.False(result.IsError ?? false, ReadText(result));
            Assert.Contains("Build succeeded", ReadText(result), StringComparison.Ordinal);
            await WaitForAsync(() => !progress.Values.IsEmpty, cts.Token);

            var values = progress.Values.ToArray();
            Assert.NotEmpty(values);
            Assert.All(values, value => Assert.True(value.Progress >= 1));
            Assert.All(
                values,
                value =>
                {
                    Assert.NotNull(value.Message);
                    Assert.Contains("dotnet build", value.Message, StringComparison.Ordinal);
                    Assert.DoesNotContain(temp.FullName, value.Message, StringComparison.OrdinalIgnoreCase);
                });
        }
        finally
        {
            temp.Delete(recursive: true);
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

    [Fact]
    public async Task Run_dotnet_build_live_build_matches_the_with_progress_result_when_no_token_is_sent()
    {
        using var host = BuildLiteHost();
        var options = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        await using var serverTransport = new StreamServerTransport(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream(),
            serverName: "build-no-progress-test");

        await using var server = McpServer.Create(
            serverTransport,
            options,
            host.Services.GetService<ILoggerFactory>(),
            host.Services);
        var serverTask = server.RunAsync(cts.Token);

        await using var client = await McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream()),
            new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "build-no-progress-test", Version = "1.0" },
            },
            cancellationToken: cts.Token);

        var temp = Directory.CreateTempSubdirectory("mcp-build-no-progress-");
        try
        {
            var csproj = Path.Combine(temp.FullName, "Slow.csproj");
            await File.WriteAllTextAsync(csproj, SlowProject(), cts.Token);

            // No IProgress argument: the client sends no progress token, the SDK binds its no-op
            // reporter, and the live build must still produce the ordinary build report.
            var result = await client.CallToolAsync(
                "run_dotnet_build",
                new Dictionary<string, object?> { ["workspacePath"] = csproj },
                cancellationToken: cts.Token);

            var text = ReadText(result);
            Assert.False(result.IsError ?? false, text);
            Assert.Contains("Build succeeded", text, StringComparison.Ordinal);
            Assert.Contains("Steps:", text, StringComparison.Ordinal);
            Assert.Contains("dotnet build -v:minimal", text, StringComparison.Ordinal);
        }
        finally
        {
            temp.Delete(recursive: true);
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

    [Fact]
    public void TryCreate_treats_the_sdk_no_op_progress_instance_as_no_progress()
    {
        var nullProgressType = typeof(McpServer).Assembly.GetType(
            McpToolProgressReporter.NoOpProgressTypeFullName,
            throwOnError: false);
        Assert.NotNull(nullProgressType);
        var instance = nullProgressType!
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null);
        var noOp = Assert.IsAssignableFrom<IProgress<ProgressNotificationValue>>(instance);

        Assert.Null(McpToolProgressReporter.TryCreate(noOp));
        Assert.Null(McpToolProgressReporter.TryCreate(null));
        Assert.NotNull(McpToolProgressReporter.TryCreate(new CollectingProgress()));
    }

    [Fact]
    public void Report_swallows_a_failing_progress_channel()
    {
        var reporter = McpToolProgressReporter.TryCreate(new ThrowingProgress());
        Assert.NotNull(reporter);

        reporter!.Report(new Services.CliProgressUpdate("dotnet build -v:minimal", TimeSpan.FromSeconds(5), null));
    }

    [Fact]
    public void Report_emits_strictly_increasing_seconds_and_resets_the_message_on_a_step_boundary()
    {
        var progress = new CollectingProgress();
        var reporter = McpToolProgressReporter.TryCreate(progress)!;

        // Step boundary: no elapsed.
        reporter.Report(new Services.CliProgressUpdate("dotnet build -v:minimal", TimeSpan.Zero, null));
        // Long step heartbeats.
        reporter.Report(new Services.CliProgressUpdate("dotnet build -v:minimal", TimeSpan.FromSeconds(5), null));
        reporter.Report(new Services.CliProgressUpdate("dotnet build -v:minimal", TimeSpan.FromSeconds(10), 1));
        // Next step boundary: the per-step clock resets, the numeric value must not go backwards.
        reporter.Report(new Services.CliProgressUpdate("dotnet restore -v:minimal", TimeSpan.Zero, 1));

        var values = progress.Values.ToArray();
        Assert.Equal(4, values.Length);
        Assert.Equal("dotnet build -v:minimal: starting", values[0].Message);
        Assert.Equal("dotnet build -v:minimal: still running (5s elapsed)", values[1].Message);
        Assert.Equal("dotnet build -v:minimal: still running (10s elapsed); previous step exit 1", values[2].Message);
        Assert.Equal("dotnet restore -v:minimal: starting; previous step exit 1", values[3].Message);
        Assert.Equal([1f, 5f, 10f, 11f], values.Select(v => v.Progress).ToArray());
    }

    private static string SlowProject() => OperatingSystem.IsWindows()
        ? """
          <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
              <TargetFramework>net10.0</TargetFramework>
            </PropertyGroup>
            <Target Name="Slow" BeforeTargets="Build">
              <Exec Command="ping -n 3 127.0.0.1" IgnoreExitCode="true" />
            </Target>
          </Project>
          """
        : """
          <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
              <TargetFramework>net10.0</TargetFramework>
            </PropertyGroup>
            <Target Name="Slow" BeforeTargets="Build">
              <Exec Command="sleep 2" IgnoreExitCode="true" />
            </Target>
          </Project>
          """;

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

    private sealed class CollectingProgress : IProgress<ProgressNotificationValue>
    {
        public ConcurrentQueue<ProgressNotificationValue> Values { get; } = new();

        public void Report(ProgressNotificationValue value) => Values.Enqueue(value);
    }

    private sealed class ThrowingProgress : IProgress<ProgressNotificationValue>
    {
        public void Report(ProgressNotificationValue value) =>
            throw new InvalidOperationException("transport disconnected");
    }
}
