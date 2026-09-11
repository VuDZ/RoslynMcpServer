using RoslynMcpServer.LifecycleTestHost;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
public sealed class Epoch4WorkspaceWriteTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        yield return new object[] { OutputPathMode.RedirectedMissingAnalyzerPath };
        yield return new object[] { OutputPathMode.SdkDefaultCorrectPath };
    }

    private readonly ITestOutputHelper _output;

    public Epoch4WorkspaceWriteTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AnalyzerLifecycleTheory]
    [MemberData(nameof(Fixtures))]
    public async Task Text_edit_full_success_keeps_marker_requested_text_and_csproj(OutputPathMode mode)
    {
        using var fixture = GeneratorConsumerFixture.Create(mode);
        await using var host = LifecycleHostClient.Start();
        var afterLoad = await PrepareAsync(host, fixture);
        var originalConsumer = File.ReadAllText(fixture.ConsumerSourcePath);
        var newText = fixture.WithConsumerComment("e4-text-" + mode);

        var update = await host.SendAsync(
            new HostCommand { Op = "updateDocument", Path = fixture.ConsumerSourcePath, Text = newText });
        Assert.True(update.Ok, update.Error);
        Assert.Equal("FullSuccess", update.WriteStatus);
        Assert.True(update.WorkspaceApplied);
        Assert.True(update.OverlayPublished);
        Assert.False(update.UnappliedProjectState);
        Assert.Equal(afterLoad.OverlayPrepareCount, update.OverlayPrepareCount);
        Assert.Equal(afterLoad.AnalyzerFileIoCount, update.AnalyzerFileIoCount);
        Assert.Equal(newText, File.ReadAllText(fixture.ConsumerSourcePath));
        Assert.NotEqual(originalConsumer, newText);

        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
        var rebuild = await Epoch1HostOps.AssertForcedGeneratorRebuildWritesBytesAsync(host, fixture);
        _output.WriteLine("e4-text {0} rebuild {1} {2}->{3}", mode, rebuild.Dll, rebuild.Before, rebuild.After);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
    }

    [AnalyzerLifecycleTheory]
    [MemberData(nameof(Fixtures))]
    public async Task Overlay_apply_full_success_keeps_marker_text_and_csproj(OutputPathMode mode)
    {
        using var fixture = GeneratorConsumerFixture.Create(mode);
        await using var host = LifecycleHostClient.Start();
        var afterLoad = await PrepareAsync(host, fixture);
        var newText = fixture.WithConsumerComment("e4-overlay-" + mode);

        var apply = await host.SendAsync(
            new HostCommand { Op = "applyOverlayEdit", Path = fixture.ConsumerSourcePath, Text = newText });
        Assert.True(apply.Ok, apply.Error);
        Assert.Equal("FullSuccess", apply.WriteStatus);
        Assert.Contains("e4-overlay-", File.ReadAllText(fixture.ConsumerSourcePath), StringComparison.Ordinal);
        Assert.Equal(afterLoad.OverlayPrepareCount, apply.OverlayPrepareCount);
        Assert.Equal(afterLoad.AnalyzerFileIoCount, apply.AnalyzerFileIoCount);

        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
        await Epoch1HostOps.AssertForcedGeneratorRebuildWritesBytesAsync(host, fixture);
    }

    [AnalyzerLifecycleTheory]
    [MemberData(nameof(Fixtures))]
    public async Task Watcher_flush_full_success_keeps_marker_text_and_csproj(OutputPathMode mode)
    {
        using var fixture = GeneratorConsumerFixture.Create(mode);
        await using var host = LifecycleHostClient.Start();
        var afterLoad = await PrepareAsync(host, fixture);
        var newText = fixture.WithConsumerComment("e4-fsw-" + mode);
        File.WriteAllText(fixture.ConsumerSourcePath, newText);

        var wait = await host.SendAsync(
            new HostCommand { Op = "waitDirty", Path = fixture.ConsumerSourcePath, TimeoutMs = 15_000 });
        Assert.True(wait.DirtyDelivered, "FSW not delivered: " + wait.Error);

        var flushed = await host.SendAsync(new HostCommand { Op = "flushFind", Path = fixture.ConsumerSourcePath });
        Assert.True(flushed.Ok, flushed.Error);
        Assert.Contains("e4-fsw-", flushed.DocumentText ?? "", StringComparison.Ordinal);
        Assert.Equal(afterLoad.OverlayPrepareCount, flushed.OverlayPrepareCount);
        Assert.Equal(afterLoad.AnalyzerFileIoCount, flushed.AnalyzerFileIoCount);

        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
        await Epoch1HostOps.AssertForcedGeneratorRebuildWritesBytesAsync(host, fixture);
    }

    [AnalyzerLifecycleFact]
    public async Task Unknown_analyzer_diff_with_rename_text_is_rejected_before_any_write()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await PrepareAsync(host, fixture);
        var before = File.ReadAllText(fixture.ConsumerSourcePath);
        var beforeBytes = fixture.ProjectFileBytes;

        var apply = await host.SendAsync(
            new HostCommand
            {
                Op = "applyUnknownAnalyzerDiff",
                Path = fixture.ConsumerSourcePath,
                Text = fixture.WithConsumerComment("must-not-be-written"),
            });
        Assert.False(apply.Ok);
        Assert.Equal("PreflightRejected", apply.WriteStatus);
        Assert.Contains("unknown-analyzer-diff", apply.WriteReason ?? apply.Error ?? "", StringComparison.Ordinal);
        Assert.True(apply.SavedPaths is null || apply.SavedPaths.Length == 0);
        Assert.False(apply.OverlayPublished);
        Assert.Equal(before, File.ReadAllText(fixture.ConsumerSourcePath));
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
        Assert.True(GeneratorConsumerFixture.ProjectBytesUnchanged(beforeBytes, snapshot.CsprojSha256!));
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
    }

    [AnalyzerLifecycleFact]
    public async Task Stale_candidate_after_reload_is_rejected_before_any_write()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await PrepareAsync(host, fixture);
        var before = File.ReadAllText(fixture.ConsumerSourcePath);
        var heldText = fixture.WithConsumerComment("stale-after-reload");

        var hold = await host.SendAsync(
            new HostCommand { Op = "holdOverlayEdit", Path = fixture.ConsumerSourcePath, Text = heldText });
        Assert.True(hold.Ok, hold.Error);

        _ = await host.SendAsync(new HostCommand { Op = "reset" });
        var reload = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(reload.Ok, reload.Error);

        var apply = await host.SendAsync(new HostCommand { Op = "applyHeld" });
        Assert.False(apply.Ok);
        Assert.Equal("PreflightRejected", apply.WriteStatus);
        Assert.Contains("stale-session", apply.WriteReason ?? apply.Error ?? "", StringComparison.Ordinal);
        Assert.True(apply.SavedPaths is null || apply.SavedPaths.Length == 0);
        Assert.Equal(before, File.ReadAllText(fixture.ConsumerSourcePath));
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
    }

    [AnalyzerLifecycleFact]
    public async Task Rejected_apply_reconciles_saved_text_without_full_success()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        var afterLoad = await PrepareAsync(host, fixture);
        var newText = fixture.WithConsumerComment("e4-rejected-apply");

        _ = await host.SendAsync(new HostCommand { Op = "injectApplyFailure" });
        var apply = await host.SendAsync(
            new HostCommand { Op = "applyOverlayEdit", Path = fixture.ConsumerSourcePath, Text = newText });
        Assert.False(apply.Ok);
        Assert.Equal("ReconciliationSucceeded", apply.WriteStatus);
        Assert.Equal("try-apply-rejected", apply.WriteReason);
        Assert.True(apply.UnappliedProjectState);
        Assert.True(apply.OverlayPublished);
        Assert.False(apply.WorkspaceApplied);
        Assert.Contains("e4-rejected-apply", File.ReadAllText(fixture.ConsumerSourcePath), StringComparison.Ordinal);
        Assert.Equal(afterLoad.OverlayPrepareCount, apply.OverlayPrepareCount);
        Assert.Equal(afterLoad.AnalyzerFileIoCount, apply.AnalyzerFileIoCount);

        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
    }

    [AnalyzerLifecycleFact]
    public async Task File_write_error_does_not_write_or_claim_full_success()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await PrepareAsync(host, fixture);
        var before = File.ReadAllText(fixture.ConsumerSourcePath);

        _ = await host.SendAsync(
            new HostCommand { Op = "injectFileWriteFailure", Path = fixture.ConsumerSourcePath });
        var apply = await host.SendAsync(
            new HostCommand
            {
                Op = "applyOverlayEdit",
                Path = fixture.ConsumerSourcePath,
                Text = fixture.WithConsumerComment("e4-io-fail"),
            });
        Assert.False(apply.Ok);
        Assert.Equal("PartialPersistence", apply.WriteStatus);
        Assert.Contains("injected-file-write-failure", apply.WriteReason ?? "", StringComparison.Ordinal);
        Assert.True(apply.SavedPaths is null || apply.SavedPaths.Length == 0);
        Assert.False(apply.OverlayPublished);
        Assert.Equal(before, File.ReadAllText(fixture.ConsumerSourcePath));
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
    }

    [AnalyzerLifecycleFact]
    public async Task Cancel_after_partial_write_reconciles_saved_text_only()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        var afterLoad = await PrepareAsync(host, fixture);
        var newText = fixture.WithConsumerComment("e4-cancel");

        _ = await host.SendAsync(new HostCommand { Op = "injectCancelAfterWrites", CancelAfterWrites = 1 });
        var apply = await host.SendAsync(
            new HostCommand { Op = "applyOverlayEdit", Path = fixture.ConsumerSourcePath, Text = newText });
        Assert.False(apply.Ok);
        Assert.Equal("ReconciliationSucceeded", apply.WriteStatus);
        Assert.Equal("cancelled", apply.WriteReason);
        Assert.True(apply.UnappliedProjectState);
        Assert.Contains("e4-cancel", File.ReadAllText(fixture.ConsumerSourcePath), StringComparison.Ordinal);
        Assert.Equal(afterLoad.AnalyzerFileIoCount, apply.AnalyzerFileIoCount);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
    }

    [AnalyzerLifecycleFact]
    public async Task Failed_reconciliation_does_not_publish_candidate_or_promise_freshness()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await PrepareAsync(host, fixture);
        var newText = fixture.WithConsumerComment("e4-recon-fail");

        _ = await host.SendAsync(new HostCommand { Op = "injectApplyFailure" });
        _ = await host.SendAsync(new HostCommand { Op = "injectReconciliationFailure" });
        var apply = await host.SendAsync(
            new HostCommand { Op = "applyOverlayEdit", Path = fixture.ConsumerSourcePath, Text = newText });
        Assert.False(apply.Ok);
        Assert.Equal("ReconciliationFailed", apply.WriteStatus);
        Assert.Contains("reconciliation-failed", apply.WriteReason ?? "", StringComparison.Ordinal);
        Assert.True(apply.UnappliedProjectState);
        Assert.False(apply.OverlayPublished);
        Assert.Contains("e4-recon-fail", File.ReadAllText(fixture.ConsumerSourcePath), StringComparison.Ordinal);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
    }

    [AnalyzerLifecycleFact]
    public async Task No_overlay_document_edit_still_succeeds()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(load.Ok, load.Error);
        var newText = fixture.WithConsumerComment("e4-no-overlay");
        var update = await host.SendAsync(
            new HostCommand { Op = "updateDocument", Path = fixture.ConsumerSourcePath, Text = newText });
        Assert.True(update.Ok, update.Error);
        Assert.Equal("FullSuccess", update.WriteStatus);
        Assert.Contains("e4-no-overlay", File.ReadAllText(fixture.ConsumerSourcePath), StringComparison.Ordinal);
    }

    private static async Task<HostResponse> PrepareAsync(LifecycleHostClient host, GeneratorConsumerFixture fixture)
    {
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(load.Ok, load.Error);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        return await host.SendAsync(new HostCommand { Op = "inspect" });
    }
}
