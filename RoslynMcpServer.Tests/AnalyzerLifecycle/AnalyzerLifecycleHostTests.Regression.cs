using RoslynMcpServer.LifecycleTestHost;
using Xunit;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

public sealed partial class AnalyzerLifecycleHostTests
{
    [AnalyzerLifecycleFact]
    public async Task V3_R1_same_session_stale_candidate_is_rejected_before_any_write()
    {
        using var fixture = GeneratorConsumerFixture.Create();
        await using var host = LifecycleHostClient.Start();
        await LifecycleHostOps.BuildAsync(host, fixture.GeneratorProjectPath);

        var load = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(load.Ok, LifecycleHostAssert.Compact("R1 load", load));

        var textA = fixture.WithConsumerComment("v3-r1-held-A");
        var hold = await host.SendAsync(
            new HostCommand { Op = "holdOverlayEdit", Path = fixture.ConsumerSourcePath, Text = textA });
        Assert.True(hold.Ok, LifecycleHostAssert.Compact("R1 hold", hold));

        var textB = fixture.WithConsumerComment("v3-r1-written-B");
        Assert.NotEqual(textA, textB);
        var writeB = await host.SendAsync(
            new HostCommand { Op = "updateDocument", Path = fixture.ConsumerSourcePath, Text = textB });
        Assert.True(writeB.Ok, LifecycleHostAssert.Compact("R1 write-B", writeB));
        Assert.Equal(textB, fixture.ReadConsumerSource());

        var apply = await host.SendAsync(new HostCommand { Op = "applyHeld" });
        var published = await ReadPublishedDocumentAsync(host, fixture.ConsumerSourcePath);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        var disk = fixture.ReadConsumerSource();
        var publishedText = published.DocumentText;
        var csprojUnchanged = snapshot.CsprojSha256 is not null
            && GeneratorConsumerFixture.ProjectBytesUnchanged(fixture.ProjectFileBytes, snapshot.CsprojSha256);
        var compact = LifecycleHostAssert.Compact(
            "R1 apply",
            apply,
            disk: disk,
            publishedText: publishedText,
            expectedText: textB,
            extra: "csprojUnchanged=" + csprojUnchanged);

        _output.WriteLine(compact);
        Assert.False(apply.Ok, compact);
        Assert.True(
            string.Equals(apply.WriteStatus, "PreflightRejected", StringComparison.Ordinal),
            compact);
        Assert.True(apply.SavedPaths is null || apply.SavedPaths.Length == 0, compact);
        Assert.Equal(textB, disk);
        Assert.True(
            published.Ok,
            LifecycleHostAssert.Compact("R1 published", published, publishedText: publishedText, expectedText: textB));
        Assert.Equal(textB, publishedText);
        Assert.DoesNotContain("v3-r1-held-A", disk, StringComparison.Ordinal);
        Assert.DoesNotContain("v3-r1-held-A", publishedText ?? string.Empty, StringComparison.Ordinal);
        LifecycleHostOps.AssertProjectFilesUnchanged(fixture, snapshot);
    }

    [AnalyzerLifecycleFact]
    public async Task V3_R2_prepare_failure_stays_fail_closed_after_text_edit()
    {
        using var fixture = GeneratorConsumerFixture.Create();
        await using var host = LifecycleHostClient.Start();
        await LifecycleHostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var realOutput = LifecycleHostAssert.RequireGeneratorOutput(fixture);

        _ = await host.SendAsync(new HostCommand { Op = "injectPrepareFailure" });
        var load = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var loadCompact = LifecycleHostAssert.Compact("R2 load", load, realOutput);
        _output.WriteLine(loadCompact);
        Assert.True(load.Ok, loadCompact);
        Assert.True(load.PrepareAttempted, loadCompact);
        Assert.True(load.PrepareInjectedFailure, loadCompact);
        Assert.False(load.ShadowEnabled, loadCompact);
        Assert.Equal("LoadFailed", load.Execution?.Status);
        Assert.Null(load.OverlayAnalyzerPath);
        LifecycleHostAssert.AssertNoRealPublication(load, realOutput, loadCompact);

        var edit = await host.SendAsync(
            new HostCommand
            {
                Op = "updateDocument",
                Path = fixture.ConsumerSourcePath,
                Text = fixture.WithConsumerComment("v3-r2-after-prepare-failure"),
            });
        Assert.True(edit.Ok, LifecycleHostAssert.Compact("R2 edit", edit, realOutput));

        var afterEdit = await LifecycleHostOps.OracleAsync(host);
        var compact = LifecycleHostAssert.Compact("R2 after-edit oracle", afterEdit, realOutput);
        _output.WriteLine(compact);
        Assert.False(afterEdit.OracleSuccess, compact);
        LifecycleHostAssert.AssertNoRealPublication(afterEdit, realOutput, compact);
        LifecycleHostAssert.AssertNoExactMarker(afterEdit, compact);
    }

    [AnalyzerLifecycleFact]
    public async Task V3_R3_corrupt_capture_does_not_publish_or_execute_real_output()
    {
        using var fixture = GeneratorConsumerFixture.Create();
        await using var host = LifecycleHostClient.Start();
        await LifecycleHostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var realOutput = LifecycleHostAssert.RequireGeneratorOutput(fixture);

        var inject = await host.SendAsync(
            new HostCommand
            {
                Op = "injectCaptureFailure",
                CaptureFailureMode = "Corrupt",
            });
        Assert.True(inject.Ok, LifecycleHostAssert.Compact("R3 inject", inject, realOutput));

        var load = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var loadCompact = LifecycleHostAssert.Compact("R3 load", load, realOutput);
        _output.WriteLine(loadCompact);
        Assert.True(load.Ok, loadCompact);
        Assert.Equal("Failed", load.ProvenanceCaptureStatus);
        Assert.Equal(0, load.ProvenanceConfirmedBindingCount);
        Assert.False(load.ShadowEnabled, loadCompact);
        LifecycleHostAssert.AssertNoRealPublication(load, realOutput, loadCompact);

        var semantic = await LifecycleHostOps.OracleAsync(host);
        var compact = LifecycleHostAssert.Compact("R3 published oracle", semantic, realOutput);
        _output.WriteLine(compact);
        Assert.False(IsPublishedSemanticAvailable(semantic), compact);
        Assert.Equal("no-solution", semantic.Error);
        LifecycleHostAssert.AssertNoRealPublication(semantic, realOutput, compact);
        LifecycleHostAssert.AssertNoExactMarker(semantic, compact);
    }

    [AnalyzerLifecycleFact]
    public async Task Watcher_flush_after_prepare_failure_stays_fail_closed()
    {
        using var fixture = GeneratorConsumerFixture.Create();
        await using var host = LifecycleHostClient.Start();
        await LifecycleHostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var realOutput = LifecycleHostAssert.RequireGeneratorOutput(fixture);
        var afterBan = await BanOptInAsync(host, fixture.SolutionPath, realOutput);

        var text = fixture.WithConsumerComment("v3-s3-fsw");
        File.WriteAllText(fixture.ConsumerSourcePath, text);
        var wait = await host.SendAsync(
            new HostCommand { Op = "waitDirty", Path = fixture.ConsumerSourcePath, TimeoutMs = 15_000 });
        Assert.True(wait.DirtyDelivered, LifecycleHostAssert.Compact("fsw wait", wait, realOutput));

        var flushed = await host.SendAsync(
            new HostCommand { Op = "flushFind", Path = fixture.ConsumerSourcePath });
        Assert.True(flushed.Ok, LifecycleHostAssert.Compact("fsw flush", flushed, realOutput));
        Assert.Equal(text, flushed.DocumentText);
        LifecycleHostAssert.AssertNoPublicationIo(
            afterBan,
            flushed,
            LifecycleHostAssert.Compact("fsw io", flushed, realOutput));

        var oracle = await LifecycleHostOps.OracleAsync(host);
        var compact = LifecycleHostAssert.Compact("fsw oracle", oracle, realOutput);
        _output.WriteLine(compact);
        Assert.False(oracle.OracleSuccess, compact);
        LifecycleHostAssert.AssertNoRealPublication(oracle, realOutput, compact);
        LifecycleHostAssert.AssertNoExactMarker(oracle, compact);
        Assert.Equal("Banned", oracle.PublicationAdmission);
    }

    /// <summary>
    /// Fails the next opt-in prepare and asserts the published admission is banned with no real
    /// output anywhere in the response.
    /// </summary>
    private static async Task<HostResponse> BanOptInAsync(
        LifecycleHostClient host,
        string solutionPath,
        string realOutput)
    {
        _ = await host.SendAsync(new HostCommand { Op = "injectPrepareFailure" });
        var load = await LifecycleHostOps.LoadAsync(host, solutionPath, shadowCopy: true);
        var compact = LifecycleHostAssert.Compact("ban load", load, realOutput);
        Assert.True(load.Ok, compact);
        Assert.True(load.PrepareInjectedFailure, compact);
        Assert.False(load.ShadowEnabled, compact);
        Assert.Equal("Banned", load.PublicationAdmission);
        LifecycleHostAssert.AssertNoRealPublication(load, realOutput, compact);
        return load;
    }

    private static async Task<HostResponse> ReadPublishedDocumentAsync(LifecycleHostClient host, string path)
    {
        return await host.SendAsync(new HostCommand { Op = "publishedDocument", Path = path });
    }

    private static bool IsPublishedSemanticAvailable(HostResponse response) =>
        !string.Equals(response.Error, "no-solution", StringComparison.Ordinal)
        && (response.OracleSuccess
            || !string.IsNullOrWhiteSpace(response.OverlayAnalyzerPath)
            || !string.IsNullOrWhiteSpace(response.LoadedAnalyzerPath)
            || !string.IsNullOrWhiteSpace(response.Marker));
}
