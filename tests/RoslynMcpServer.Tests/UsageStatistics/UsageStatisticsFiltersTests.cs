using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Diagnostics.UsageStatistics;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Services;
using RoslynMcpServer.Tests.SourceStructure;
using Xunit;

namespace RoslynMcpServer.Tests.UsageStatistics;

public sealed class UsageStatisticsFiltersTests
{
    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task Real_protocol_filters_capture_explicit_outcomes_without_arguments(string protocolVersion)
    {
        using var files = new UsageTestFiles();
        using var usage = new UsageStatisticsService(files.UsageDirectory);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(usage);
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "full" });
        using var host = builder.Build();
        var cancelRequestId = new TaskCompletionSource<RequestId>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelRecorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var collection = host.Services.GetRequiredService<McpRuntimeToolCollection>();
        AddProbe(collection, "usage_throw", (Func<string>)(() => throw new InvalidOperationException("Probe failed.")));
        AddProbe(collection, "usage_partial", (Func<Task<string>>)PartialResultAsync);
        AddProbe(collection, "usage_cancel", (Func<CancellationToken, Task<string>>)WaitForCancellationAsync);

        await usage.StartAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var input = new Pipe();
        var output = new Pipe();
        await using var transport = new StreamServerTransport(input.Reader.AsStream(), output.Writer.AsStream(), "usage-test");
        var options = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        options.Filters.Message.IncomingFilters.Insert(0, next => async (context, token) =>
        {
            if (context.JsonRpcMessage is JsonRpcRequest { Method: "tools/call" } request
                && request.Params?["name"]?.GetValue<string>() == "usage_cancel")
            {
                cancelRequestId.TrySetResult(request.Id);
            }

            await next(context, token);
        });
        options.Filters.Request.CallToolFilters.Insert(0, next => async (context, token) =>
        {
            try
            {
                return await next(context, token);
            }
            finally
            {
                if (context.Params?.Name == "usage_cancel")
                {
                    cancelRecorded.TrySetResult();
                }
            }
        });
        await using var server = McpServer.Create(
            transport, options, host.Services.GetService<ILoggerFactory>(), host.Services);
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

            var repository = RepositoryRoot.Find();
            var rejectedDiagnostics = await client.CallToolAsync(
                "get_diagnostics_for_file",
                new Dictionary<string, object?> { ["filePath"] = Path.Combine(repository, "README.md") },
                cancellationToken: cancellation.Token);
            Assert.False(rejectedDiagnostics.IsError ?? false);
            Assert.StartsWith("Path must point to a .cs file:", Assert.Single(rejectedDiagnostics.Content.OfType<TextContentBlock>()).Text);

            var sourcePath = Path.Combine(RepositoryRoot.FindProductionProjectDirectory(), "Diagnostics", "ToolLogAnalyzer.cs");
            var source = await client.CallToolAsync(
                "get_file_content",
                new Dictionary<string, object?> { ["filePath"] = sourcePath },
                cancellationToken: cancellation.Token);
            Assert.False(source.IsError ?? false);
            Assert.Equal(await File.ReadAllTextAsync(sourcePath, cancellation.Token),
                Assert.Single(source.Content.OfType<TextContentBlock>()).Text);

            var thrown = await client.CallToolAsync("usage_throw", cancellationToken: cancellation.Token);
            Assert.True(thrown.IsError ?? false);

            var partial = await client.CallToolAsync("usage_partial", cancellationToken: cancellation.Token);
            Assert.False(partial.IsError ?? false);
            Assert.Equal("Retained data.", Assert.Single(partial.Content.OfType<TextContentBlock>()).Text);

            using var cancelCall = new CancellationTokenSource();
            var pending = client.CallToolAsync("usage_cancel", cancellationToken: cancelCall.Token).AsTask();
            await cancelStarted.Task.WaitAsync(cancellation.Token);
            await client.SendNotificationAsync(
                NotificationMethods.CancelledNotification,
                new CancelledNotificationParams
                {
                    RequestId = await cancelRequestId.Task.WaitAsync(cancellation.Token),
                    Reason = "Test requested cancellation.",
                },
                McpJsonUtilities.DefaultOptions,
                cancellationToken: cancellation.Token);
            await cancelCall.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            await cancelRecorded.Task.WaitAsync(cancellation.Token);

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

            await VerifyWorkspaceOutcomesAsync(client, host.Services.GetRequiredService<SolutionManager>(), cancellation.Token);
            await VerifyRenameOutcomeAsync(client, failSolutionWrite: true, cancellation.Token);
            await VerifyRenameOutcomeAsync(client, failSolutionWrite: false, cancellation.Token);
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

        var events = await files.ReadEventsAsync(usage);
        var clientEvent = Assert.Single(events, item => item.GetProperty("ev").GetString() == "client"
            && item.GetProperty("name").GetString() == "usage-integration");
        Assert.Equal(protocolVersion, clientEvent.GetProperty("protocol").GetString());
        var calls = events.Where(item => item.GetProperty("ev").GetString() == "tool").ToArray();
        Assert.Equal(15, calls.Length);
        Assert.True(Succeeded("get_tool_help"));
        Assert.True(Succeeded("get_file_content"));
        Assert.False(Succeeded("get_diagnostics_for_file"));
        Assert.False(Succeeded("read_file_range"));
        Assert.False(Succeeded("usage_throw"));
        Assert.False(Succeeded("usage_partial"));
        Assert.False(Succeeded("usage_cancel"));
        Assert.Equal(new[] { true, false }, Outcomes("load_workspace"));
        Assert.Equal(new[] { false, true }, Outcomes("find_symbol_definition"));
        Assert.Equal(2, calls.Count(item => item.GetProperty("name").GetString() == "decompile_type"));
        Assert.All(calls, item =>
        {
            Assert.Equal(item.GetProperty("name").GetString() is "get_diagnostics_for_file" or "find_symbol_definition" or "load_workspace",
                item.GetProperty("requires_workspace").GetBoolean());
            Assert.False(item.GetProperty("lazy_load").GetBoolean());
        });
        var starts = events.Where(item => item.GetProperty("ev").GetString() == "tool_start").ToArray();
        Assert.Equal(calls.Length, starts.Length);
        var renameOutcomes = starts.Where(start => start.GetProperty("name").GetString() == "rename_project")
            .Select(start => Assert.Single(calls, call => call.GetProperty("name").GetString() == "rename_project"
                && call.GetProperty("call").GetInt64() == start.GetProperty("call").GetInt64()))
            .Select(call => call.GetProperty("success").GetBoolean());
        Assert.Equal(new[] { false, true }, renameOutcomes);
        Assert.All(calls, call => Assert.Contains(starts,
            start => start.GetProperty("call").GetInt64() == call.GetProperty("call").GetInt64()));
        Assert.All(starts, item => Assert.Equal(item.GetProperty("name").GetString() is "get_diagnostics_for_file" or "find_symbol_definition" or "load_workspace",
            item.GetProperty("requires_workspace").GetBoolean()));
        Assert.DoesNotContain(events, item => item.GetRawText().Contains("private-argument-sentinel"));
        Assert.DoesNotContain(events, item => item.GetRawText().Contains("PrivateArgumentSentinel"));
        Assert.DoesNotContain(events, item => item.TryGetProperty("arguments", out _));

        bool Succeeded(string name) => Assert.Single(Outcomes(name));

        IEnumerable<bool> Outcomes(string name) => calls
            .Where(item => item.GetProperty("name").GetString() == name)
            .Select(item => item.GetProperty("success").GetBoolean());

        static async Task<string> PartialResultAsync()
        {
            await Task.Yield();
            return ToolTelemetry.TraceFailureAndReturn("usage_partial", "Retained data.");
        }

        async Task<string> WaitForCancellationAsync(CancellationToken token)
        {
            cancelStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return string.Empty;
        }
    }

    private static async Task VerifyWorkspaceOutcomesAsync(
        McpClient client,
        SolutionManager manager,
        CancellationToken cancellationToken)
    {
        using var published = new UsageTestFiles();
        using var graphOnly = new UsageTestFiles();
        published.CreateProject();
        graphOnly.CreateProject();
        manager.SearchPrewarmEnabled = false;
        try
        {
            var loaded = await client.CallToolAsync(
                "load_workspace",
                new Dictionary<string, object?> { ["workspacePath"] = published.ProjectPath },
                cancellationToken: cancellationToken);
            Assert.False(loaded.IsError ?? false);
            Assert.Collection(
                loaded.Content.OfType<TextContentBlock>(),
                block => Assert.Contains("Successfully loaded workspace.", block.Text, StringComparison.Ordinal),
                block => Assert.StartsWith("Warning: Use the containing .sln or .slnx", block.Text, StringComparison.Ordinal));

            var invalid = await client.CallToolAsync(
                "find_symbol_definition",
                new Dictionary<string, object?>
                {
                    ["filePath"] = Path.Combine(Path.GetDirectoryName(published.ProjectPath)!, "Widget.cs"),
                    ["line"] = 0,
                    ["column"] = 1,
                },
                cancellationToken: cancellationToken);
            Assert.False(invalid.IsError ?? false);
            Assert.Equal("Line must be >= 1 (got 0).", Assert.Single(invalid.Content.OfType<TextContentBlock>()).Text);

            var notFound = await client.CallToolAsync(
                "find_symbol_definition",
                new Dictionary<string, object?> { ["symbolName"] = "AbsentFromUsageTest" },
                cancellationToken: cancellationToken);
            Assert.False(notFound.IsError ?? false);
            Assert.Contains("no matching type or member declarations", Assert.Single(notFound.Content.OfType<TextContentBlock>()).Text);

            manager.DiscardNextProvenanceSnapshot = true;
            var partial = await client.CallToolAsync(
                "load_workspace",
                new Dictionary<string, object?>
                {
                    ["workspacePath"] = graphOnly.ProjectPath,
                    ["shadowCopyInSolutionAnalyzers"] = true,
                },
                cancellationToken: cancellationToken);
            Assert.False(partial.IsError ?? false);
            Assert.Collection(
                partial.Content.OfType<TextContentBlock>(),
                block =>
                {
                    Assert.Contains("Opened MSBuild graph.", block.Text, StringComparison.Ordinal);
                    Assert.Contains("Semantic workspace is unavailable.", block.Text, StringComparison.Ordinal);
                },
                block => Assert.StartsWith("Warning: Use the containing .sln or .slnx", block.Text, StringComparison.Ordinal));
        }
        finally
        {
            await manager.ClearWorkspaceAsync(CancellationToken.None);
        }
    }

    private static async Task VerifyRenameOutcomeAsync(McpClient client, bool failSolutionWrite, CancellationToken cancellationToken)
    {
        using var files = new UsageTestFiles();
        var directory = Directory.CreateDirectory(Path.Combine(files.Root, "Old"));
        var project = Path.Combine(directory.FullName, "Old.csproj");
        File.WriteAllText(project, """<Project Sdk="Microsoft.NET.Sdk" />""");
        var solution = Path.Combine(files.Root, "Demo.slnx");
        const string OriginalSolution = """<Solution><Project Path="Old/Old.csproj" /></Solution>""";
        if (failSolutionWrite && !OperatingSystem.IsWindows())
        {
            // Moving the project leaves the external solution link readable during planning and dangling at write time.
            var target = Path.Combine(directory.FullName, "solution.xml");
            File.WriteAllText(target, OriginalSolution);
            File.CreateSymbolicLink(solution, target);
        }
        else
        {
            File.WriteAllText(solution, OriginalSolution);
        }

        using var readLock = failSolutionWrite && OperatingSystem.IsWindows() ? File.OpenRead(solution) : null;
        var response = await client.CallToolAsync(
            "rename_project",
            new Dictionary<string, object?>
            {
                ["projectPath"] = project,
                ["newProjectName"] = "New",
                ["dryRun"] = false,
                ["searchRoot"] = files.Root,
            },
            cancellationToken: cancellationToken);
        var message = Assert.Single(response.Content.OfType<TextContentBlock>()).Text;
        Assert.False(response.IsError ?? false);
        Assert.True(File.Exists(Path.Combine(files.Root, "New", "New.csproj")), message);
        if (failSolutionWrite)
        {
            Assert.Contains("Moved directory", message, StringComparison.Ordinal);
            Assert.Contains("### Partial failure", message, StringComparison.Ordinal);
            Assert.Contains($"- `{solution}`:", message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("Rename applied successfully.", message, StringComparison.Ordinal);
            Assert.Contains("New/New.csproj", File.ReadAllText(solution), StringComparison.Ordinal);
        }
    }

    private static void AddProbe(McpRuntimeToolCollection collection, string name, Delegate handler)
    {
        var tool = McpServerTool.Create(handler.Method, handler.Target, new McpServerToolCreateOptions { Name = name });
        Assert.True(collection.TryAdd(tool));
    }
}
