using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RoslynMcpServer.Config;
using RoslynMcpServer.Diagnostics.UsageStatistics;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Services.Workspace.Models;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.UsageStatistics;

public sealed class UsageWorkspaceInstrumentationTests
{
    [Fact]
    public void Original_three_argument_constructor_remains_invocable()
    {
        var constructor = typeof(SolutionManager).GetConstructor(
            [typeof(ILogger<SolutionManager>), typeof(AnalyzerProvenanceCaptureService), typeof(RoslynMcpFileSettings)]);
        Assert.NotNull(constructor);
        Assert.True(constructor.GetParameters()[2].IsOptional);
        var analyzer = new AnalyzerProvenanceCaptureService(
            NullLogger<AnalyzerProvenanceCaptureService>.Instance,
            Options.Create(new AnalyzerProvenanceCaptureOptions()),
            TimeProvider.System);

        var manager = Assert.IsType<SolutionManager>(constructor.Invoke([NullLogger<SolutionManager>.Instance, analyzer, null]));

        Assert.Same(RoslynMcpFileSettings.Empty, manager.FileSettings);
    }
    [Fact]
    public async Task Load_cache_failed_load_and_cancelled_reset_keep_the_correct_resident_key()
    {
        using var files = new UsageTestFiles();
        files.CreateProject();
        using var usage = new UsageStatisticsService(files.UsageDirectory);
        var manager = CreateManager(usage);
        await usage.StartAsync(CancellationToken.None);
        string? resident = null;
        var watchers = WorkspaceDiskWatcherStatus.None;
        try
        {
            await manager.LoadAsync(files.ProjectPath, CancellationToken.None, "Release", "Any CPU", "net10.0");
            resident = usage.WorkspaceKey;
            Assert.NotNull(resident);
            watchers = manager.DiskWatcherStatus;
            Assert.True(watchers.Started > 0);
            await manager.LoadAsync(files.ProjectPath, CancellationToken.None);
            Assert.Equal(resident, usage.WorkspaceKey);

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                manager.LoadAsync(Path.Combine(files.Root, "Missing.csproj"), CancellationToken.None));
            Assert.Equal(resident, usage.WorkspaceKey);

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.ClearWorkspaceAsync(cancelled.Token));
            Assert.Equal(resident, usage.WorkspaceKey);
            await manager.ClearWorkspaceAsync();
            Assert.Null(usage.WorkspaceKey);
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            await usage.StopAsync(CancellationToken.None);
        }

        var events = await files.ReadEventsAsync(usage);
        var loads = events.Where(item => item.GetProperty("ev").GetString() == "load").ToArray();
        Assert.Equal(3, loads.Length);
        Assert.True(loads[0].GetProperty("success").GetBoolean());
        Assert.False(loads[0].GetProperty("cache_hit").GetBoolean());
        Assert.Equal(watchers.Started, loads[0].GetProperty("watchers").GetInt32());
        Assert.Equal(watchers.Failures.Count, loads[0].GetProperty("watcher_failures").GetInt32());
        Assert.True(loads[1].GetProperty("cache_hit").GetBoolean());
        Assert.Equal(watchers.Started, loads[1].GetProperty("watchers").GetInt32());
        Assert.Equal(resident, loads[1].GetProperty("requested_key").GetString());
        Assert.False(loads[2].GetProperty("success").GetBoolean());
        Assert.Equal(resident, loads[2].GetProperty("workspace_key").GetString());
        Assert.NotEqual(resident, loads[2].GetProperty("requested_key").GetString());
        Assert.All(loads, item =>
        {
            Assert.True(item.GetProperty("duration_ms").GetDouble() >= 0);
            Assert.True(item.GetProperty("lock_wait_ms").GetDouble() >= 0);
        });
        var failedReset = Assert.Single(events.Where(item =>
            item.GetProperty("ev").GetString() == "reset" && !item.GetProperty("success").GetBoolean()));
        Assert.Equal(resident, failedReset.GetProperty("workspace_key").GetString());
        Assert.Equal(resident, failedReset.GetProperty("resident_key").GetString());
        Assert.Contains(events, item => item.GetProperty("ev").GetString() == "reset"
            && item.GetProperty("success").GetBoolean()
            && item.GetProperty("workspace_key").GetString() == resident
            && item.GetProperty("resident_key").ValueKind == JsonValueKind.Null);
    }

    [Theory]
    [InlineData("run_specific_test")]
    [InlineData("plugin_semantic")]
    public async Task Config_lazy_load_marks_builtin_or_plugin_calls_but_loaded_reads_keep_the_catalog_bit(string toolName)
    {
        using var files = new UsageTestFiles();
        files.CreateProject();
        var settings = WriteWorkspaceSettings(files);
        using var usage = new UsageStatisticsService(files.UsageDirectory);
        var manager = CreateManager(usage, settings);
        await usage.StartAsync(CancellationToken.None);
        try
        {
            var call = usage.BeginTool(toolName, requiresWorkspace: false);
            var tools = new TestTools(manager, NullLogger<TestTools>.Instance);
            var result = await tools.RunSpecificTest(files.ProjectPath, className: "UsageMissingTest", maxOutputChars: -1);
            Assert.Contains("Error: `maxOutputChars` must be >= 0.", result);
            usage.EndTool(call, success: false);
            var direct = usage.BeginTool("plugin_direct", requiresWorkspace: false);
            Assert.NotNull(manager.GetCurrentSolution());
            usage.EndTool(direct, success: true);
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            await usage.StopAsync(CancellationToken.None);
        }

        var events = await files.ReadEventsAsync(usage);
        var load = Assert.Single(events.Where(item => item.GetProperty("ev").GetString() == "load"));
        Assert.Equal("lazy", load.GetProperty("source").GetString());
        Assert.True(load.GetProperty("success").GetBoolean());
        var tool = Assert.Single(events.Where(item => item.GetProperty("ev").GetString() == "tool"
            && item.GetProperty("name").GetString() == toolName));
        Assert.True(tool.GetProperty("lazy_load").GetBoolean());
        Assert.True(tool.GetProperty("requires_workspace").GetBoolean());
        var directTool = Assert.Single(events.Where(item => item.GetProperty("ev").GetString() == "tool"
            && item.GetProperty("name").GetString() == "plugin_direct"));
        Assert.False(directTool.GetProperty("requires_workspace").GetBoolean());
        Assert.False(directTool.GetProperty("lazy_load").GetBoolean());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Config_reader_waiting_for_explicit_load_keeps_its_snapshot(bool configuredProjectExists)
    {
        using var configured = new UsageTestFiles();
        if (configuredProjectExists)
        {
            configured.CreateProject();
        }

        using var selected = new UsageTestFiles();
        selected.CreateProject();
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(selected.ProjectPath)!, "Widget.cs"),
            "namespace Selected; public sealed class ChosenWidget { }");
        var settings = WriteWorkspaceSettings(configured);
        using var usage = new UsageStatisticsService(configured.UsageDirectory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var manager = CreateManager(usage, settings);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.AfterPhysicalLoadBeforePrepareAsync = token =>
        {
            entered.TrySetResult();
            return release.Task.WaitAsync(token);
        };

        await usage.StartAsync(deadline.Token);
        try
        {
            var load = manager.LoadAndPrepareAsync(
                selected.ProjectPath,
                shadowCopyInSolutionAnalyzers: true,
                deadline.Token);
            try
            {
                Assert.Same(entered.Task, await Task.WhenAny(entered.Task, load).WaitAsync(deadline.Token));
                Assert.Null(manager.GetCurrentSolution());

                var call = usage.BeginTool("waiting_semantic_reader", requiresWorkspace: true);
                var reader = manager.GetPublishedSolutionAsync(deadline.Token);
                Assert.False(reader.IsCompleted);
                release.TrySetResult();

                var published = await reader.WaitAsync(deadline.Token);
                Assert.NotNull(published);
                Assert.Equal(selected.ProjectPath, Assert.Single(published.Projects).FilePath);
                Assert.Null(manager.LastLazyLoadFailureReport);
                usage.EndTool(call, success: true);

                var navigation = new NavigationTools(manager, NullLogger<NavigationTools>.Instance);
                var definition = await navigation.FindSymbolDefinition(
                    "ChosenWidget", cancellationToken: deadline.Token);
                Assert.Contains("Selected.ChosenWidget", definition, StringComparison.Ordinal);
            }
            finally
            {
                release.TrySetResult();
                await load.WaitAsync(deadline.Token);
            }
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            await usage.StopAsync(CancellationToken.None);
        }

        var events = await configured.ReadEventsAsync(usage);
        var recordedLoad = Assert.Single(events.Where(item => item.GetProperty("ev").GetString() == "load"));
        Assert.Equal("explicit", recordedLoad.GetProperty("source").GetString());
        Assert.True(recordedLoad.GetProperty("success").GetBoolean());
        var readerEvent = Assert.Single(events.Where(item => item.GetProperty("ev").GetString() == "tool"
            && item.GetProperty("name").GetString() == "waiting_semantic_reader"));
        Assert.False(readerEvent.GetProperty("lazy_load").GetBoolean());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Config_load_still_runs_after_explicit_load_publishes_no_snapshot(bool cancelExplicitLoad)
    {
        using var configured = new UsageTestFiles();
        configured.CreateProject();
        using var selected = new UsageTestFiles();
        selected.CreateProject();
        using var usage = new UsageStatisticsService(configured.UsageDirectory);
        var manager = CreateManager(usage, WriteWorkspaceSettings(configured));
        await usage.StartAsync(CancellationToken.None);

        try
        {
            if (cancelExplicitLoad)
            {
                var cancelled = new CancellationToken(canceled: true);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    manager.LoadAsync(selected.ProjectPath, cancelled));
            }
            else
            {
                await Assert.ThrowsAsync<FileNotFoundException>(() =>
                    manager.LoadAsync(Path.ChangeExtension(selected.ProjectPath, ".missing.csproj")));
            }

            Assert.Null(manager.GetCurrentSolution());
            var published = await manager.GetPublishedSolutionAsync();
            Assert.NotNull(published);
            Assert.Equal(configured.ProjectPath, Assert.Single(published.Projects).FilePath);
            Assert.Equal(WorkspaceLoadSource.ConfigFile, manager.WorkspaceLoadSource);
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            await usage.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Loaded_snapshot_access_and_explicit_load_keep_the_catalog_bit()
    {
        using var files = new UsageTestFiles();
        files.CreateProject();
        using var usage = new UsageStatisticsService(files.UsageDirectory);
        var manager = CreateManager(usage);
        await usage.StartAsync(CancellationToken.None);
        try
        {
            var loadCall = usage.BeginTool("plugin_load", requiresWorkspace: false);
            await manager.LoadAsync(files.ProjectPath, CancellationToken.None);
            usage.EndTool(loadCall, success: true);
            string[] toolNames = ["run_specific_test", "run_dotnet_test", "run_test_by_filter", "decompile_type", "plugin_direct"];
            foreach (var name in toolNames)
            {
                var descriptor = McpToolCatalog.All.FirstOrDefault(tool => tool.Name == name);
                Assert.False(descriptor?.RequiresWorkspace ?? false);
                var call = usage.BeginTool(name, descriptor?.RequiresWorkspace ?? false);
                if (name == "run_specific_test")
                {
                    var tools = new TestTools(manager, NullLogger<TestTools>.Instance);
                    var result = await tools.RunSpecificTest(files.ProjectPath, className: "UsageMissingTest", maxOutputChars: -1);
                    Assert.Contains("Error: `maxOutputChars` must be >= 0.", result);
                }

                await manager.EnsureWorkspaceFromConfigAsync();
                Assert.NotNull(await manager.GetPublishedSolutionAfterDiskSyncAsync());
                Assert.NotNull(await manager.GetSanitizedPublishedSolutionAsync());
                Assert.NotNull(manager.GetSanitizedPublishedSolution());
                Assert.NotNull(manager.GetCurrentSolution());
                usage.EndTool(call, success: name != "run_specific_test");
            }
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            await usage.StopAsync(CancellationToken.None);
        }

        var events = await files.ReadEventsAsync(usage);
        var load = Assert.Single(events, item => item.GetProperty("ev").GetString() == "load");
        Assert.Equal("explicit", load.GetProperty("source").GetString());
        var calls = events.Where(item => item.GetProperty("ev").GetString() == "tool").ToArray();
        Assert.Equal(6, calls.Length);
        Assert.All(calls, item =>
        {
            Assert.False(item.GetProperty("requires_workspace").GetBoolean());
            Assert.False(item.GetProperty("lazy_load").GetBoolean());
        });
    }

    [Fact]
    public async Task Tool_events_measure_semantic_lock_contention_and_a_cancelled_acquisition()
    {
        using var files = new UsageTestFiles();
        files.CreateProject();
        using var usage = new UsageStatisticsService(files.UsageDirectory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var manager = CreateManager(usage);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.AfterPhysicalLoadBeforePrepareAsync = token =>
        {
            entered.TrySetResult();
            return release.Task.WaitAsync(token);
        };
        await usage.StartAsync(deadline.Token);
        try
        {
            Assert.Null(usage.MeasureWorkspaceReadLockWait());
            var idle = usage.BeginTool("idle", requiresWorkspace: false);
            Assert.Null(manager.GetCurrentSolution());
            usage.EndTool(idle, success: true);

            var load = manager.LoadAndPrepareAsync(
                files.ProjectPath,
                shadowCopyInSolutionAnalyzers: true,
                deadline.Token);
            try
            {
                Assert.Same(entered.Task, await Task.WhenAny(entered.Task, load).WaitAsync(deadline.Token));
                var readCall = usage.BeginTool("waiting_reader", requiresWorkspace: true);
                var reader = manager.GetPublishedSolutionAsync(deadline.Token);
                Assert.False(reader.IsCompleted);

                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                var cancelledCall = usage.BeginTool("cancelled_reader", requiresWorkspace: true);
                var document = Path.Combine(Path.GetDirectoryName(files.ProjectPath)!, "Widget.cs");
                var cancelledReader = manager.FindDocumentAsync(document, cancellation.Token);
                Assert.False(cancelledReader.IsCompleted);

                await Task.Delay(TimeSpan.FromMilliseconds(60), deadline.Token);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledReader);
                usage.EndTool(cancelledCall, success: false);
                Assert.False(reader.IsCompleted);

                release.TrySetResult();
                var published = await reader;
                Assert.NotNull(published);
                Assert.Same(published, manager.GetCurrentSolution());
                usage.EndTool(readCall, success: true);
            }
            finally
            {
                release.TrySetResult();
                await load;
            }
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            await usage.StopAsync(CancellationToken.None);
        }

        var events = await files.ReadEventsAsync(usage);
        var calls = events.Where(item => item.GetProperty("ev").GetString() == "tool")
            .ToDictionary(item => item.GetProperty("name").GetString()!);
        Assert.False(calls["idle"].TryGetProperty("workspace_read_lock_wait_ms", out _));
        Assert.True(calls["waiting_reader"].GetProperty("workspace_read_lock_wait_ms").GetDouble() >= 20);
        Assert.True(calls["cancelled_reader"].GetProperty("workspace_read_lock_wait_ms").GetDouble() >= 20);
        Assert.False(calls["cancelled_reader"].GetProperty("success").GetBoolean());
    }

    private static RoslynMcpFileSettings WriteWorkspaceSettings(UsageTestFiles files)
    {
        File.WriteAllText(
            Path.Combine(files.Root, RoslynMcpFileSettings.FileName),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["workspace-path"] = files.ProjectPath }));
        return RoslynMcpFileSettings.LoadFromDirectories(null, files.Root);
    }

    private static SolutionManager CreateManager(UsageStatisticsService usage, RoslynMcpFileSettings? settings = null) =>
        new(
            NullLogger<SolutionManager>.Instance,
            new AnalyzerProvenanceCaptureService(
                NullLogger<AnalyzerProvenanceCaptureService>.Instance,
                Options.Create(new AnalyzerProvenanceCaptureOptions()),
                TimeProvider.System),
            settings,
            usage)
        {
            SearchPrewarmEnabled = false,
        };
}
