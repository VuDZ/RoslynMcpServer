using RoslynMcpServer.LifecycleTestHost;
using Xunit;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

public sealed partial class AnalyzerLifecycleHostTests
{
    [AnalyzerLifecycleFact]
    public async Task Production_snapshot_selects_each_loaded_inner_tfm_for_rollout()
    {
        // The copy assertion needs a generator that yields one DLL per inner TFM, which only the SDK
        // default output layout produces: the redirected layout puts both TFMs on one Generator.dll.
        using var fixture = GeneratorConsumerFixture.Create(extraConsumers: 1);
        await using var host = LifecycleHostClient.Start();
        await LifecycleHostOps.BuildAsync(host, fixture.SolutionPath);

        fixture.MakeGeneratorMultiTargeted();
        var net10ConsumerSource = Assert.Single(fixture.ExtraConsumerSourcePaths);
        var net10ConsumerDirectory = Path.GetDirectoryName(net10ConsumerSource)!;
        var net10ConsumerProject = Path.Combine(
            net10ConsumerDirectory,
            Path.GetFileName(net10ConsumerDirectory) + ".csproj");
        GeneratorConsumerFixture.SetProjectTargetFramework(net10ConsumerProject, "net10.0");
        await LifecycleHostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        await LifecycleHostOps.BuildAsync(host, net10ConsumerProject);

        var load = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);

        Assert.True(load.Ok, load.Error);
        Assert.Equal("Complete", load.ProvenanceCaptureStatus);
        Assert.True(load.ProvenanceConfirmedBindingCount >= 2);
        var rewrites = load.Rewrite ?? [];
        var applied = rewrites.Where(rewrite => rewrite.Applied).ToArray();
        Assert.Equal(2, applied.Length);
        Assert.Equal(
            2,
            applied
                .Select(rewrite => rewrite.ShadowCopyPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        await LifecycleHostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);

        await using var net10Host = LifecycleHostClient.Start();
        var net10Load = await LifecycleHostOps.LoadAsync(net10Host, fixture.SolutionPath, shadowCopy: true);
        Assert.Equal("Complete", net10Load.ProvenanceCaptureStatus);
        Assert.Contains(
            net10Load.Rewrite ?? [],
            rewrite => rewrite.Applied
                && string.Equals(rewrite.ProjectName, "Consumer1", StringComparison.Ordinal));
        await LifecycleHostOps.RequireMarkerAsync(
            net10Host,
            GeneratorConsumerFixture.MarkerV1,
            "Consumer1");
    }

    [AnalyzerLifecycleFact]
    public async Task Replay_failures_publish_fail_closed_status_and_delete_temporary_files()
    {
        using var fixture = GeneratorConsumerFixture.Create();
        await using var host = LifecycleHostClient.Start();
        await LifecycleHostOps.BuildAsync(host, fixture.SolutionPath);

        _ = await host.SendAsync(new HostCommand
        {
            Op = "injectCaptureFailure",
            CaptureFailureMode = "Missing",
        });
        var missing = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(missing.Ok, missing.Error);
        Assert.Equal("Failed", missing.ProvenanceCaptureStatus);
        Assert.Equal(0, missing.ProvenanceConfirmedBindingCount);
        Assert.Equal("Unavailable", missing.PublicationAdmission);
        Assert.False(missing.PublishedSnapshotPresent);
        Assert.Empty(missing.Rewrite ?? []);
        Assert.False(missing.ShadowEnabled);
        Assert.Equal(0, missing.ProvenanceTempDirectoryCount);

        _ = await host.SendAsync(new HostCommand { Op = "reset" });
        _ = await host.SendAsync(new HostCommand
        {
            Op = "injectCaptureFailure",
            CaptureFailureMode = "MixedValidAndCorrupt",
        });
        var partial = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(partial.Ok, partial.Error);
        Assert.Equal("Incomplete", partial.ProvenanceCaptureStatus);
        Assert.True(partial.ProvenanceAnalyzerItemCount > 0);
        Assert.Equal(0, partial.ProvenanceConfirmedBindingCount);
        Assert.Equal(0, partial.ProvenanceTempDirectoryCount);
    }

    [AnalyzerLifecycleFact]
    public async Task Real_FSW_delivery_then_production_flush_publishes_text_and_marker()
    {
        using var fixture = GeneratorConsumerFixture.Create();
        await using var host = LifecycleHostClient.Start();
        await LifecycleHostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(load.Ok, load.Error);
        await LifecycleHostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var realOutput = LifecycleHostAssert.RequireGeneratorOutput(fixture);

        var newText = fixture.WithConsumerComment("fsw");
        var afterLoad = await host.SendAsync(new HostCommand { Op = "inspect" });
        File.WriteAllText(fixture.ConsumerSourcePath, newText);

        var wait = await host.SendAsync(
            new HostCommand
            {
                Op = "waitDirty",
                Path = fixture.ConsumerSourcePath,
                TimeoutMs = 15_000,
            });
        if (!wait.DirtyDelivered)
        {
            Assert.Fail(
                "FSW dirty event was not delivered within timeout (not a flush failure). pending="
                + wait.PendingDirtyCount
                + " err="
                + wait.Error
                + " stderr="
                + host.StderrSnapshot);
        }

        var flushed = await host.SendAsync(
            new HostCommand { Op = "flushFind", Path = fixture.ConsumerSourcePath });
        Assert.True(flushed.Ok, "Production flush via FindDocumentAsync failed: " + flushed.Error);
        Assert.Equal(newText, flushed.DocumentText);
        Assert.Equal(afterLoad.OverlayPrepareCount, flushed.OverlayPrepareCount);
        Assert.Equal(afterLoad.AnalyzerFileIoCount, flushed.AnalyzerFileIoCount);

        var getter = await host.SendAsync(new HostCommand { Op = "flushGetter" });
        Assert.True(getter.Ok, getter.Error);

        var oracle = await LifecycleHostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var oracleCompact = LifecycleHostAssert.Compact("fsw oracle", oracle, realOutput);
        Assert.False(LifecycleHostAssert.PathsEqual(oracle.OverlayAnalyzerPath, realOutput), oracleCompact);
        Assert.False(LifecycleHostAssert.PathsEqual(oracle.LoadedAnalyzerPath, realOutput), oracleCompact);
        LifecycleHostAssert.AssertPathEqual(oracle.WorkspaceAnalyzerPath, realOutput);
        LifecycleHostAssert.AssertPathEqual(oracle.OverlayAnalyzerPath, oracle.LoadedAnalyzerPath);
        Assert.True(
            LifecycleHostAssert.HasExactPath(oracle.ProcessAnalyzerAssemblies, oracle.OverlayAnalyzerPath!),
            "Shadow path must stay loaded after the flush. " + oracleCompact);
        Assert.False(
            LifecycleHostAssert.HasPath(oracle.ProcessAnalyzerAssemblies, realOutput),
            "Real output must not be loaded after the flush. " + oracleCompact);
        Assert.False(
            LifecycleHostAssert.HasPath(oracle.LoadedAssemblies, realOutput),
            "Real output must not be in the loader snapshot after the flush. " + oracleCompact);

        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        LifecycleHostOps.AssertProjectFilesUnchanged(fixture, snapshot);
        Assert.Equal(newText, fixture.ReadConsumerSource());
        var rebuild = await LifecycleHostOps.AssertForcedGeneratorRebuildWritesBytesAsync(host, fixture);
        Assert.False(string.IsNullOrWhiteSpace(rebuild.After));
        await LifecycleHostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
    }

    [AnalyzerLifecycleFact]
    public async Task New_cs_flush_does_not_mutate_csproj_and_type_appears_after_reload()
    {
        using var fixture = GeneratorConsumerFixture.Create();
        await using var host = LifecycleHostClient.Start();
        await LifecycleHostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(load.Ok, load.Error);

        var before = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Assert.True(before.Ok, before.Error);

        var consumerDir = Path.GetDirectoryName(fixture.ConsumerSourcePath);
        Assert.False(string.IsNullOrWhiteSpace(consumerDir));
        var newPath = Path.Combine(consumerDir!, "DiskSyncNewType.cs");
        File.WriteAllText(newPath, "public sealed class DiskSyncNewType {}");

        var wait = await host.SendAsync(
            new HostCommand { Op = "waitDirty", Path = newPath, TimeoutMs = 15_000 });
        AssertDirtyDelivered(host, wait);

        var flushedKnown = await host.SendAsync(
            new HostCommand { Op = "flushFind", Path = fixture.ConsumerSourcePath });
        Assert.True(flushedKnown.Ok, flushedKnown.Error);
        AssertCompositionHintOnly(flushedKnown);

        var afterFlush = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        LifecycleHostOps.AssertProjectFilesUnchanged(fixture, afterFlush);
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

        var reload = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(reload.Ok, reload.Error);
        Assert.True(reload.ReopenedGraph, "Stale composition must skip the load cache.");
        Assert.False(reload.CacheHit);

        var found = await host.SendAsync(new HostCommand { Op = "flushFind", Path = newPath });
        Assert.True(found.Ok, found.Error);
        Assert.Contains("DiskSyncNewType", found.DocumentText ?? string.Empty, StringComparison.Ordinal);
        _output.WriteLine("reload CacheHit={0} ReopenedGraph={1}", reload.CacheHit, reload.ReopenedGraph);
    }

    [AnalyzerLifecycleFact]
    public async Task Deleted_known_cs_stays_in_snapshot_and_updateDocument_does_not_recreate_file()
    {
        using var fixture = GeneratorConsumerFixture.Create();
        await using var host = LifecycleHostClient.Start();
        await LifecycleHostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(load.Ok, load.Error);

        var original = File.ReadAllText(fixture.ConsumerSourcePath);
        File.Delete(fixture.ConsumerSourcePath);

        var wait = await host.SendAsync(
            new HostCommand { Op = "waitDirty", Path = fixture.ConsumerSourcePath, TimeoutMs = 15_000 });
        AssertDirtyDelivered(host, wait);

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

    private static void AssertDirtyDelivered(LifecycleHostClient host, HostResponse wait)
    {
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
