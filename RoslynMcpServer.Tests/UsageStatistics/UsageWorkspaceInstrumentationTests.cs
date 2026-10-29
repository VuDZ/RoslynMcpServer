using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RoslynMcpServer.Config;
using RoslynMcpServer.Diagnostics.UsageStatistics;
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
        try
        {
            await manager.LoadAsync(files.ProjectPath, CancellationToken.None, "Release", "Any CPU", "net10.0");
            resident = usage.WorkspaceKey;
            Assert.NotNull(resident);
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

        var events = files.ReadEvents();
        var loads = events.Where(item => item.GetProperty("ev").GetString() == "load").ToArray();
        Assert.Equal(3, loads.Length);
        Assert.True(loads[0].GetProperty("success").GetBoolean());
        Assert.False(loads[0].GetProperty("cache_hit").GetBoolean());
        Assert.True(loads[1].GetProperty("cache_hit").GetBoolean());
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

    [Fact]
    public async Task Config_lazy_load_and_plugin_reads_mark_the_call_as_workspace_dependent()
    {
        using var files = new UsageTestFiles();
        files.CreateProject();
        File.WriteAllText(
            Path.Combine(files.Root, RoslynMcpFileSettings.FileName),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["workspace-path"] = files.ProjectPath }));
        var settings = RoslynMcpFileSettings.LoadFromDirectories(null, files.Root);
        using var usage = new UsageStatisticsService(files.UsageDirectory);
        var manager = CreateManager(usage, settings);
        await usage.StartAsync(CancellationToken.None);
        try
        {
            var call = usage.BeginTool("plugin_semantic", requiresWorkspace: false);
            Assert.NotNull(await manager.GetPublishedSolutionAsync());
            usage.EndTool(call, success: true);
            var direct = usage.BeginTool("plugin_direct", requiresWorkspace: false);
            Assert.NotNull(manager.GetCurrentSolution());
            usage.EndTool(direct, success: true);
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            await usage.StopAsync(CancellationToken.None);
        }

        var events = files.ReadEvents();
        var load = Assert.Single(events.Where(item => item.GetProperty("ev").GetString() == "load"));
        Assert.Equal("lazy", load.GetProperty("source").GetString());
        Assert.True(load.GetProperty("success").GetBoolean());
        var tool = Assert.Single(events.Where(item => item.GetProperty("ev").GetString() == "tool"
            && item.GetProperty("name").GetString() == "plugin_semantic"));
        Assert.True(tool.GetProperty("lazy_load").GetBoolean());
        Assert.True(tool.GetProperty("requires_workspace").GetBoolean());
        var directTool = Assert.Single(events.Where(item => item.GetProperty("ev").GetString() == "tool"
            && item.GetProperty("name").GetString() == "plugin_direct"));
        Assert.True(directTool.GetProperty("requires_workspace").GetBoolean());
        Assert.False(directTool.GetProperty("lazy_load").GetBoolean());
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
