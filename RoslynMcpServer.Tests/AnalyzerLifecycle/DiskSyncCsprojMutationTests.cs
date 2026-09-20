using RoslynMcpServer.LifecycleTestHost;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
public sealed class DiskSyncCsprojMutationTests
{
    private readonly ITestOutputHelper _output;

    public DiskSyncCsprojMutationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AnalyzerLifecycleFact]
    public async Task New_cs_flush_does_not_mutate_csproj_and_type_appears_after_reload()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(load.Ok, load.Error);

        var before = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Assert.True(before.Ok, before.Error);

        var consumerDir = Path.GetDirectoryName(fixture.ConsumerSourcePath);
        Assert.False(string.IsNullOrWhiteSpace(consumerDir));
        var newPath = Path.Combine(consumerDir!, "DiskSyncNewType.cs");
        File.WriteAllText(newPath, "public sealed class DiskSyncNewType {}");

        var wait = await host.SendAsync(
            new HostCommand { Op = "waitDirty", Path = newPath, TimeoutMs = 15_000 });
        if (!wait.DirtyDelivered)
        {
            Assert.Fail(
                "FSW dirty event was not delivered within timeout. pending="
                + wait.PendingDirtyCount
                + " err="
                + wait.Error
                + " stderr="
                + host.StderrSnapshot);
        }

        var flushedKnown = await host.SendAsync(
            new HostCommand { Op = "flushFind", Path = fixture.ConsumerSourcePath });
        Assert.True(flushedKnown.Ok, flushedKnown.Error);
        AssertCompositionHintOnly(flushedKnown);

        var afterFlush = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, afterFlush);
        var consumerCsproj = File.ReadAllText(fixture.ConsumerProjectPath);
        Assert.DoesNotContain("DiskSyncNewType.cs", consumerCsproj, StringComparison.OrdinalIgnoreCase);

        var flushedNew = await host.SendAsync(new HostCommand { Op = "flushFind", Path = newPath });
        Assert.False(flushedNew.Ok);
        Assert.Equal("flush-find-missed-document", flushedNew.Error);
        AssertCompositionHintOnly(flushedNew);

        var build = await host.SendAsync(
            new HostCommand
            {
                Op = "build",
                Path = fixture.ConsumerProjectPath,
                NoIncremental = true,
                TimeoutMs = 60_000,
            });
        Assert.True(build.Ok && build.BuildExitCode == 0, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.DoesNotContain("NETSDK1022", build.BuildOutput ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var reload = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(reload.Ok, reload.Error);
        Assert.True(reload.ReopenedGraph, "Stale composition must skip the load cache.");
        Assert.False(reload.CacheHit);

        var found = await host.SendAsync(new HostCommand { Op = "flushFind", Path = newPath });
        Assert.True(found.Ok, found.Error);
        Assert.Contains("DiskSyncNewType", found.DocumentText ?? string.Empty, StringComparison.Ordinal);
        _output.WriteLine("reload CacheHit={0} ReopenedGraph={1}", reload.CacheHit, reload.ReopenedGraph);
    }

    [AnalyzerLifecycleFact]
    public async Task Force_refresh_all_flush_sets_composition_stale_without_new_file()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(load.Ok, load.Error);

        var forced = await host.SendAsync(new HostCommand { Op = "forceRefreshAll" });
        Assert.True(forced.Ok, forced.Error);
        Assert.False(forced.ProjectGraphStale);

        var flushed = await host.SendAsync(new HostCommand { Op = "flushGetter" });
        Assert.True(flushed.Ok, flushed.Error);
        AssertCompositionHintOnly(flushed);
    }

    [AnalyzerLifecycleFact]
    public async Task Deleted_known_cs_stays_in_snapshot_and_updateDocument_does_not_recreate_file()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(load.Ok, load.Error);

        var original = File.ReadAllText(fixture.ConsumerSourcePath);
        File.Delete(fixture.ConsumerSourcePath);

        var wait = await host.SendAsync(
            new HostCommand { Op = "waitDirty", Path = fixture.ConsumerSourcePath, TimeoutMs = 15_000 });
        if (!wait.DirtyDelivered)
        {
            Assert.Fail(
                "FSW dirty event was not delivered within timeout. pending="
                + wait.PendingDirtyCount
                + " err="
                + wait.Error
                + " stderr="
                + host.StderrSnapshot);
        }

        var flushed = await host.SendAsync(
            new HostCommand { Op = "flushFind", Path = fixture.ConsumerSourcePath });
        Assert.True(flushed.Ok, "Known document must remain after delete: " + flushed.Error);
        AssertCompositionHintOnly(flushed);
        Assert.False(File.Exists(fixture.ConsumerSourcePath));

        var update = await host.SendAsync(
            new HostCommand
            {
                Op = "updateDocument",
                Path = fixture.ConsumerSourcePath,
                Text = original + Environment.NewLine + "// recreated",
            });
        Assert.False(File.Exists(fixture.ConsumerSourcePath), "missing-on-disk must not recreate the file. status=" + update.WriteStatus);

        var mcpWrite = await host.SendAsync(
            new HostCommand
            {
                Op = "writeFile",
                Path = fixture.ConsumerSourcePath,
                Text = original + Environment.NewLine + "// mcp-recreate",
            });
        Assert.False(
            File.Exists(fixture.ConsumerSourcePath),
            "update_file_content must not recreate missing-on-disk. status="
            + mcpWrite.WriteStatus
            + " body="
            + mcpWrite.DocumentText);
        Assert.Equal("missing-on-disk", mcpWrite.WriteReason);
        Assert.Contains("Refused to recreate", mcpWrite.DocumentText ?? string.Empty, StringComparison.Ordinal);
    }

    private static void AssertCompositionHintOnly(HostResponse response)
    {
        Assert.True(response.ProjectGraphStale, "Expected composition-stale after flush.");
        Assert.False(string.IsNullOrWhiteSpace(response.ProjectGraphStaleHint));
        Assert.Contains(
            "appeared or disappeared outside the loaded workspace snapshot",
            response.ProjectGraphStaleHint,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "`.csproj` / `.sln` / `Directory.Build.props` changed on disk",
            response.ProjectGraphStaleHint,
            StringComparison.Ordinal);
    }
}
