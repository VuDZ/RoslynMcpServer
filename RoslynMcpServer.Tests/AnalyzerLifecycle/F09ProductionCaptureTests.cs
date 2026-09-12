using RoslynMcpServer.LifecycleTestHost;
using Xunit;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
[Trait("Category", "F09ProductionCapture")]
public sealed class F09ProductionCaptureTests
{
    [AnalyzerLifecycleFact]
    public async Task Replay_failures_publish_fail_closed_status_and_delete_temporary_files()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        _ = await host.SendAsync(new HostCommand
        {
            Op = "injectCaptureFailure",
            CaptureFailureMode = "Missing",
        });
        var missing = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(missing.Ok, missing.Error);
        Assert.Equal("Failed", missing.ProvenanceCaptureStatus);
        Assert.Equal(0, missing.ProvenanceConfirmedBindingCount);
        Assert.Equal(0, missing.ProvenanceTempDirectoryCount);

        _ = await host.SendAsync(new HostCommand { Op = "reset" });
        _ = await host.SendAsync(new HostCommand
        {
            Op = "injectCaptureFailure",
            CaptureFailureMode = "MixedValidAndCorrupt",
        });
        var partial = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(partial.Ok, partial.Error);
        Assert.Equal("Incomplete", partial.ProvenanceCaptureStatus);
        Assert.True(partial.ProvenanceAnalyzerItemCount > 0);
        Assert.Equal(0, partial.ProvenanceConfirmedBindingCount);
        Assert.Equal(0, partial.ProvenanceTempDirectoryCount);
    }

    [AnalyzerLifecycleFact]
    public async Task Physical_load_publishes_complete_snapshot_and_cached_operations_do_not_recapture()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var first = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(first.Ok, first.Error);
        Assert.False(first.CacheHit);
        Assert.Equal(1, first.ProvenanceCaptureCount);
        Assert.True(first.ProvenanceSnapshotPresent);
        Assert.Equal("Complete", first.ProvenanceCaptureStatus);
        Assert.True(first.ProvenanceAnalyzerItemCount > 0);
        Assert.True(first.ProvenanceConfirmedBindingCount > 0);
        Assert.Equal(0, first.ProvenanceTempDirectoryCount);

        var cached = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(cached.CacheHit);
        Assert.Equal(1, cached.ProvenanceCaptureCount);

        var edited = await host.SendAsync(new HostCommand
        {
            Op = "updateDocument",
            Path = fixture.ConsumerSourcePath,
            Text = fixture.WithConsumerComment("f09-production"),
        });
        Assert.True(edited.Ok, edited.Error);
        Assert.Equal(1, edited.ProvenanceCaptureCount);

        var query = await Epoch1HostOps.OracleAsync(host);
        Assert.Equal(1, query.ProvenanceCaptureCount);

        var refresh = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(refresh.CacheHit);
        Assert.Equal(1, refresh.ProvenanceCaptureCount);
        Assert.Equal(0, refresh.ProvenanceTempDirectoryCount);

        var reset = await host.SendAsync(new HostCommand { Op = "reset" });
        Assert.False(reset.ProvenanceSnapshotPresent);
        Assert.Equal(1, reset.ProvenanceCaptureCount);

        var afterReset = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.False(afterReset.CacheHit);
        Assert.Equal(2, afterReset.ProvenanceCaptureCount);
        Assert.True(afterReset.ProvenanceSnapshotPresent);
        Assert.Equal("Complete", afterReset.ProvenanceCaptureStatus);
    }

    [AnalyzerLifecycleFact]
    public async Task Load_globals_and_graph_stale_each_trigger_one_new_capture()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var first = await host.SendAsync(new HostCommand
        {
            Op = "load",
            Path = fixture.ConsumerProjectPath,
            Configuration = "Debug",
            Platform = "AnyCPU",
            ShadowCopy = false,
        });
        Assert.True(first.Ok, first.Error);
        Assert.Equal(1, first.ProvenanceCaptureCount);

        var changedGlobals = await host.SendAsync(new HostCommand
        {
            Op = "load",
            Path = fixture.ConsumerProjectPath,
            Configuration = "Release",
            Platform = "AnyCPU",
            ShadowCopy = false,
        });
        Assert.True(changedGlobals.Ok, changedGlobals.Error);
        Assert.False(changedGlobals.CacheHit);
        Assert.Equal(2, changedGlobals.ProvenanceCaptureCount);

        await File.AppendAllTextAsync(
            fixture.ConsumerProjectPath,
            Environment.NewLine + "<!-- f09 graph stale -->" + Environment.NewLine);

        HostResponse? graphReload = null;
        var waitStarted = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(waitStarted) < TimeSpan.FromSeconds(10))
        {
            graphReload = await host.SendAsync(new HostCommand
            {
                Op = "load",
                Path = fixture.ConsumerProjectPath,
                Configuration = "Release",
                Platform = "AnyCPU",
                ShadowCopy = false,
            });
            if (graphReload.ReopenedGraph)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), TimeProvider.System);
        }

        Assert.NotNull(graphReload);
        Assert.True(graphReload!.Ok, graphReload.Error);
        Assert.True(graphReload.ReopenedGraph, "Project graph watcher did not mark the workspace stale.");
        Assert.Equal(3, graphReload.ProvenanceCaptureCount);
        Assert.Equal("Complete", graphReload.ProvenanceCaptureStatus);
        Assert.Equal(0, graphReload.ProvenanceTempDirectoryCount);
    }
}
