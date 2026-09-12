using RoslynMcpServer.LifecycleTestHost;
using RoslynMcpServer.Services;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// S2 write-base freshness: stale candidates are rejected before any write.
/// R1 lives in <see cref="V3RegressionBaselineTests"/>; this suite covers the
/// remaining acceptance cases (reload, other-document write, watcher, unknown
/// context, fresh apply, under-lock update/flush).
/// </summary>
[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
[Trait("Category", "V3Regression")]
public sealed class V3WriteBaseFreshnessTests(ITestOutputHelper output)
{
    [AnalyzerLifecycleFact]
    public async Task Stale_after_reset_reload_is_rejected_before_any_write()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(load.Ok, Compact("reload load", load));

        var before = fixture.ReadConsumerSource();
        var heldText = fixture.WithConsumerComment("v3-s2-reload-held");
        var hold = await host.SendAsync(
            new HostCommand { Op = "holdOverlayEdit", Path = fixture.ConsumerSourcePath, Text = heldText });
        Assert.True(hold.Ok, Compact("reload hold", hold));

        _ = await host.SendAsync(new HostCommand { Op = "reset" });
        var reload = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(reload.Ok, Compact("reload", reload));

        var apply = await host.SendAsync(new HostCommand { Op = "applyHeld" });
        var published = await ReadPublishedAsync(host, fixture.ConsumerSourcePath);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        var compact = Compact(
            "reload apply",
            apply,
            disk: fixture.ReadConsumerSource(),
            publishedText: published.DocumentText,
            expectedText: before,
            extra: "csprojUnchanged=" + CsprojUnchanged(fixture, snapshot)
                + " saved=" + FormatSaved(apply.SavedPaths));
        output.WriteLine(compact);

        AssertRejectedBeforeWrite(apply, compact, "stale-session", "stale-base");
        Assert.Equal(before, fixture.ReadConsumerSource());
        Assert.Equal(before, published.DocumentText);
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
    }

    [AnalyzerLifecycleFact]
    public async Task Other_document_write_stales_held_candidate_without_merging()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(load.Ok, Compact("other-doc load", load));

        var consumerBefore = fixture.ReadConsumerSource();
        var heldText = fixture.WithConsumerComment("v3-s2-other-held");
        var hold = await host.SendAsync(
            new HostCommand { Op = "holdOverlayEdit", Path = fixture.ConsumerSourcePath, Text = heldText });
        Assert.True(hold.Ok, Compact("other-doc hold", hold));

        var generatorBefore = File.ReadAllText(fixture.GeneratorSourcePath);
        var generatorB = generatorBefore + Environment.NewLine + "// v3-s2-other-doc";
        var writeOther = await host.SendAsync(
            new HostCommand { Op = "updateDocument", Path = fixture.GeneratorSourcePath, Text = generatorB });
        Assert.True(writeOther.Ok, Compact("other-doc write", writeOther));
        Assert.Equal(generatorB, File.ReadAllText(fixture.GeneratorSourcePath));
        Assert.Equal(consumerBefore, fixture.ReadConsumerSource());

        var apply = await host.SendAsync(new HostCommand { Op = "applyHeld" });
        var published = await ReadPublishedAsync(host, fixture.ConsumerSourcePath);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        var compact = Compact(
            "other-doc apply",
            apply,
            disk: fixture.ReadConsumerSource(),
            publishedText: published.DocumentText,
            expectedText: consumerBefore,
            extra: "csprojUnchanged=" + CsprojUnchanged(fixture, snapshot)
                + " saved=" + FormatSaved(apply.SavedPaths)
                + " generatorKept=" + (File.ReadAllText(fixture.GeneratorSourcePath) == generatorB));
        output.WriteLine(compact);

        AssertRejectedBeforeWrite(apply, compact, WorkspaceWriteBoundary.ReasonStaleBase);
        Assert.Equal(consumerBefore, fixture.ReadConsumerSource());
        Assert.Equal(consumerBefore, published.DocumentText);
        Assert.Equal(generatorB, File.ReadAllText(fixture.GeneratorSourcePath));
        Assert.DoesNotContain("v3-s2-other-held", fixture.ReadConsumerSource(), StringComparison.Ordinal);
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
    }

    [AnalyzerLifecycleFact]
    public async Task Watcher_flush_stales_held_candidate_and_keeps_flushed_text()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(load.Ok, Compact("fsw load", load));

        var textA = fixture.WithConsumerComment("v3-s2-fsw-held-A");
        var hold = await host.SendAsync(
            new HostCommand { Op = "holdOverlayEdit", Path = fixture.ConsumerSourcePath, Text = textA });
        Assert.True(hold.Ok, Compact("fsw hold", hold));

        var textB = fixture.WithConsumerComment("v3-s2-fsw-written-B");
        Assert.NotEqual(textA, textB);
        File.WriteAllText(fixture.ConsumerSourcePath, textB);
        var wait = await host.SendAsync(
            new HostCommand { Op = "waitDirty", Path = fixture.ConsumerSourcePath, TimeoutMs = 15_000 });
        Assert.True(wait.DirtyDelivered, Compact("fsw wait", wait));

        var flushed = await host.SendAsync(
            new HostCommand { Op = "flushFind", Path = fixture.ConsumerSourcePath });
        Assert.True(flushed.Ok, Compact("fsw flush", flushed));
        Assert.Equal(textB, flushed.DocumentText);
        Assert.Equal(textB, fixture.ReadConsumerSource());

        var apply = await host.SendAsync(new HostCommand { Op = "applyHeld" });
        var published = await ReadPublishedAsync(host, fixture.ConsumerSourcePath);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        var compact = Compact(
            "fsw apply",
            apply,
            disk: fixture.ReadConsumerSource(),
            publishedText: published.DocumentText,
            expectedText: textB,
            extra: "csprojUnchanged=" + CsprojUnchanged(fixture, snapshot)
                + " saved=" + FormatSaved(apply.SavedPaths));
        output.WriteLine(compact);

        AssertRejectedBeforeWrite(apply, compact, WorkspaceWriteBoundary.ReasonStaleBase);
        Assert.Equal(textB, fixture.ReadConsumerSource());
        Assert.Equal(textB, published.DocumentText);
        Assert.DoesNotContain("v3-s2-fsw-held-A", fixture.ReadConsumerSource(), StringComparison.Ordinal);
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
    }

    [AnalyzerLifecycleFact]
    public async Task Unknown_operation_context_is_rejected_before_any_write()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(load.Ok, Compact("unknown load", load));

        var before = fixture.ReadConsumerSource();
        var apply = await host.SendAsync(
            new HostCommand
            {
                Op = "applyDetachedCandidate",
                Path = fixture.ConsumerSourcePath,
                Text = fixture.WithConsumerComment("v3-s2-detached"),
            });
        var published = await ReadPublishedAsync(host, fixture.ConsumerSourcePath);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        var compact = Compact(
            "unknown apply",
            apply,
            disk: fixture.ReadConsumerSource(),
            publishedText: published.DocumentText,
            expectedText: before,
            extra: "csprojUnchanged=" + CsprojUnchanged(fixture, snapshot)
                + " saved=" + FormatSaved(apply.SavedPaths));
        output.WriteLine(compact);

        AssertRejectedBeforeWrite(apply, compact, WorkspaceWriteBoundary.ReasonUnknownOperationContext);
        Assert.Equal(before, fixture.ReadConsumerSource());
        Assert.Equal(before, published.DocumentText);
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
    }

    [AnalyzerLifecycleFact]
    public async Task Fresh_candidate_applies_with_overlay_and_without()
    {
        using var overlayFixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using (var host = LifecycleHostClient.Start())
        {
            await Epoch1HostOps.BuildAsync(host, overlayFixture.GeneratorProjectPath);
            var load = await Epoch1HostOps.LoadAsync(host, overlayFixture.SolutionPath, shadowCopy: true);
            Assert.True(load.Ok, Compact("fresh-overlay load", load));
            var text = overlayFixture.WithConsumerComment("v3-s2-fresh-overlay");
            var apply = await host.SendAsync(
                new HostCommand { Op = "applyOverlayEdit", Path = overlayFixture.ConsumerSourcePath, Text = text });
            var compact = Compact("fresh-overlay", apply, disk: overlayFixture.ReadConsumerSource(), expectedText: text);
            output.WriteLine(compact);
            Assert.True(apply.Ok, compact);
            Assert.Equal("FullSuccess", apply.WriteStatus);
            Assert.Equal(text, overlayFixture.ReadConsumerSource());
        }

        using var plainFixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using (var host = LifecycleHostClient.Start())
        {
            await Epoch1HostOps.BuildAsync(host, plainFixture.SolutionPath);
            var load = await Epoch1HostOps.LoadAsync(host, plainFixture.SolutionPath, shadowCopy: false);
            Assert.True(load.Ok, Compact("fresh-plain load", load));
            var text = plainFixture.WithConsumerComment("v3-s2-fresh-plain");
            var update = await host.SendAsync(
                new HostCommand { Op = "updateDocument", Path = plainFixture.ConsumerSourcePath, Text = text });
            var compact = Compact("fresh-plain", update, disk: plainFixture.ReadConsumerSource(), expectedText: text);
            output.WriteLine(compact);
            Assert.True(update.Ok, compact);
            Assert.Equal("FullSuccess", update.WriteStatus);
            Assert.Equal(text, plainFixture.ReadConsumerSource());
        }
    }

    [AnalyzerLifecycleFact]
    public async Task Under_lock_update_is_not_false_stale()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(load.Ok, Compact("under-lock-update load", load));

        var updated = fixture.WithConsumerComment("v3-s2-under-lock-update");
        var write = await host.SendAsync(
            new HostCommand { Op = "updateDocument", Path = fixture.ConsumerSourcePath, Text = updated });
        var compact = Compact("under-lock update", write, disk: fixture.ReadConsumerSource(), expectedText: updated);
        output.WriteLine(compact);
        Assert.True(write.Ok, compact);
        Assert.Equal("FullSuccess", write.WriteStatus);
        Assert.DoesNotContain("stale-base", write.WriteReason ?? write.Error ?? "", StringComparison.Ordinal);
        Assert.Equal(updated, fixture.ReadConsumerSource());
    }

    [AnalyzerLifecycleFact]
    public async Task Under_lock_flush_is_not_false_stale()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(load.Ok, Compact("under-lock-flush load", load));

        var flushedText = fixture.WithConsumerComment("v3-s2-under-lock-flush");
        File.WriteAllText(fixture.ConsumerSourcePath, flushedText);
        var wait = await host.SendAsync(
            new HostCommand { Op = "waitDirty", Path = fixture.ConsumerSourcePath, TimeoutMs = 15_000 });
        Assert.True(wait.DirtyDelivered, Compact("under-lock-flush wait", wait));
        var flush = await host.SendAsync(
            new HostCommand { Op = "flushFind", Path = fixture.ConsumerSourcePath });
        var compact = Compact("under-lock flush", flush, disk: fixture.ReadConsumerSource(), expectedText: flushedText);
        output.WriteLine(compact);
        Assert.True(flush.Ok, compact);
        Assert.DoesNotContain("stale-base", flush.WriteReason ?? flush.Error ?? "", StringComparison.Ordinal);
        Assert.Equal(flushedText, flush.DocumentText);
        Assert.Equal(flushedText, fixture.ReadConsumerSource());
    }

    private static async Task<HostResponse> ReadPublishedAsync(LifecycleHostClient host, string path)
    {
        return await host.SendAsync(new HostCommand { Op = "publishedDocument", Path = path });
    }

    private static void AssertRejectedBeforeWrite(
        HostResponse apply,
        string compact,
        params string[] acceptedReasons)
    {
        Assert.False(apply.Ok, compact);
        Assert.True(
            string.Equals(apply.WriteStatus, "PreflightRejected", StringComparison.Ordinal),
            compact);
        Assert.True(apply.SavedPaths is null || apply.SavedPaths.Length == 0, compact);
        var reason = apply.WriteReason ?? apply.Error ?? "";
        Assert.True(
            acceptedReasons.Any(expected => reason.Contains(expected, StringComparison.Ordinal)),
            compact);
    }

    private static bool CsprojUnchanged(GeneratorConsumerFixture fixture, HostResponse snapshot) =>
        snapshot.CsprojSha256 is not null
        && GeneratorConsumerFixture.ProjectBytesUnchanged(fixture.ProjectFileBytes, snapshot.CsprojSha256);

    private static string Compact(
        string label,
        HostResponse response,
        string? disk = null,
        string? publishedText = null,
        string? expectedText = null,
        string? extra = null)
    {
        var parts = new List<string>
        {
            label,
            "ok=" + response.Ok,
            "err=" + response.Error,
            "status=" + response.WriteStatus,
            "reason=" + response.WriteReason,
            "saved=" + FormatSaved(response.SavedPaths),
        };

        if (expectedText is not null)
        {
            parts.Add("diskEqExpected=" + (disk == expectedText));
            parts.Add("publishedEqExpected=" + (publishedText == expectedText));
        }

        if (!string.IsNullOrWhiteSpace(extra))
        {
            parts.Add(extra);
        }

        return string.Join(" ", parts);
    }

    private static string FormatSaved(string[]? saved) =>
        saved is null || saved.Length == 0
            ? "0"
            : saved.Length.ToString() + ":" + string.Join(",", saved.Select(Path.GetFileName));
}
