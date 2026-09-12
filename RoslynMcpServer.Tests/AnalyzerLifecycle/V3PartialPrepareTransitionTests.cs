using RoslynMcpServer.LifecycleTestHost;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// S5: mixed prepare publishes only a proven-safe snapshot. Two generators have
/// distinct assembly identities so a partial result is not an identity collision.
/// </summary>
[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
[Trait("Category", "V3Regression")]
public sealed class V3PartialPrepareTransitionTests(ITestOutputHelper output)
{
    [AnalyzerLifecycleFact]
    public async Task Mixed_prepare_excludes_failed_confirmed_real_output()
    {
        using var fixture = GeneratorConsumerFixture.CreateTwoGenerators(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var realA = RequireOutput(fixture.FindGeneratorOutputDll());
        var realB = RequireOutput(fixture.FindSecondGeneratorOutputDll());

        _ = await host.SendAsync(new HostCommand { Op = "forceCopyFailure" });
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var compact = Compact("mixed load", load, realA, realB);
        output.WriteLine(compact);
        Assert.True(load.Ok, compact);
        Assert.NotEqual("Unavailable", load.PublicationAdmission);
        Assert.True(load.PublishedSnapshotPresent, compact);
        Assert.Equal(1, load.PreparedCount);
        Assert.Equal(1, load.AppliedCount);
        Assert.Equal(1, load.BlockedCount);
        Assert.False(load.RefreshComplete, compact);
        Assert.Contains("Partial prepare", load.ShadowCopySummary ?? "", StringComparison.Ordinal);
        AssertNoRealPublication(load, realA, compact);
        AssertNoRealPublication(load, realB, compact);

        var applied = Assert.Single(load.Rewrite ?? [], item => item.Applied);
        var blocked = Assert.Single(load.Rewrite ?? [], item => !item.Applied && IsConfirmedRewrite(item));
        Assert.False(string.IsNullOrWhiteSpace(applied.ShadowCopyPath), compact);
        Assert.False(PathsEqual(applied.ShadowCopyPath, realA) || PathsEqual(applied.ShadowCopyPath, realB), compact);

        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);

        await AssertAllowedMarkerExactAsync(host, applied, blocked, compact);
        await AssertForcedRebuildOfAppliedAsync(host, fixture, applied, realA, realB);
    }

    [AnalyzerLifecycleFact]
    public async Task Edit_flush_and_reconciliation_keep_partial_admission_without_analyzer_io()
    {
        using var fixture = GeneratorConsumerFixture.CreateTwoGenerators(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var realA = RequireOutput(fixture.FindGeneratorOutputDll());
        var realB = RequireOutput(fixture.FindSecondGeneratorOutputDll());

        _ = await host.SendAsync(new HostCommand { Op = "forceCopyFailure" });
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.Equal(1, load.AppliedCount);
        Assert.Equal(1, load.BlockedCount);

        File.WriteAllText(fixture.ConsumerSourcePath, fixture.WithConsumerComment("v3-s5-fsw"));
        var wait = await host.SendAsync(
            new HostCommand { Op = "waitDirty", Path = fixture.ConsumerSourcePath, TimeoutMs = 15_000 });
        Assert.True(wait.DirtyDelivered, Compact("fsw wait", wait, realA, realB));
        var flushed = await host.SendAsync(
            new HostCommand { Op = "flushFind", Path = fixture.ConsumerSourcePath });
        Assert.True(flushed.Ok, Compact("flush", flushed, realA, realB));
        AssertNoPublicationIo(load, flushed, Compact("flush io", flushed, realA, realB));
        Assert.Equal(load.PublicationAdmission, flushed.PublicationAdmission);
        AssertNoRealPublication(flushed, realA, Compact("flush real A", flushed, realA, realB));
        AssertNoRealPublication(flushed, realB, Compact("flush real B", flushed, realA, realB));

        var edit = await host.SendAsync(
            new HostCommand
            {
                Op = "updateDocument",
                Path = fixture.ConsumerSourcePath,
                Text = fixture.WithConsumerComment("v3-s5-edit"),
            });
        Assert.True(edit.Ok, Compact("edit", edit, realA, realB));
        AssertNoPublicationIo(flushed, edit, Compact("edit io", edit, realA, realB));
        Assert.Equal(load.PublicationAdmission, edit.PublicationAdmission);
        Assert.Equal(load.AppliedCount, edit.AppliedCount);
        Assert.Equal(load.BlockedCount, edit.BlockedCount);
        AssertNoRealPublication(edit, realA, Compact("edit real A", edit, realA, realB));
        AssertNoRealPublication(edit, realB, Compact("edit real B", edit, realA, realB));

        _ = await host.SendAsync(new HostCommand { Op = "injectApplyFailure" });
        var recon = await host.SendAsync(
            new HostCommand
            {
                Op = "updateDocument",
                Path = fixture.ConsumerSourcePath,
                Text = fixture.WithConsumerComment("v3-s5-recon"),
            });
        Assert.Equal("ReconciliationSucceeded", recon.WriteStatus);
        AssertNoPublicationIo(edit, recon, Compact("recon io", recon, realA, realB));
        AssertNoRealPublication(recon, realA, Compact("recon real A", recon, realA, realB));
        AssertNoRealPublication(recon, realB, Compact("recon real B", recon, realA, realB));

        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
        Assert.True(
            snapshot.TemporaryAnalyzerIncludes is null || snapshot.TemporaryAnalyzerIncludes.Length == 0,
            "shadow includes leaked into a later build: "
            + string.Join("; ", snapshot.TemporaryAnalyzerIncludes ?? []));
    }

    [AnalyzerLifecycleFact]
    public async Task File_failed_refresh_keeps_stale_only_where_allowed()
    {
        using var fixture = GeneratorConsumerFixture.CreateTwoGenerators(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var realA = RequireOutput(fixture.FindGeneratorOutputDll());
        var realB = RequireOutput(fixture.FindSecondGeneratorOutputDll());

        var first = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var firstCompact = Compact("first", first, realA, realB);
        output.WriteLine(firstCompact);
        Assert.True(first.Ok, firstCompact);
        Assert.Equal("AllowedMapping", first.PublicationAdmission);
        Assert.Equal(2, first.AppliedCount);
        Assert.Equal(0, first.BlockedCount);
        Assert.True(first.RefreshComplete, firstCompact);

        _ = await host.SendAsync(new HostCommand { Op = "forceCopyFailure" });
        var refresh = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var compact = Compact("refresh", refresh, realA, realB);
        output.WriteLine(compact);
        Assert.True(refresh.CacheHit, compact);
        Assert.Equal("AllowedMapping", refresh.PublicationAdmission);
        Assert.True(refresh.LastRefreshStale, compact);
        Assert.Equal(1, refresh.StaleCount);
        Assert.True(refresh.AppliedCount >= 1, compact);
        AssertNoRealPublication(refresh, realA, compact);
        AssertNoRealPublication(refresh, realB, compact);

        var stale = Assert.Single(refresh.Rewrite ?? [], item => item.StaleGeneration);
        Assert.True(stale.Applied, compact);
        Assert.Contains("stale-generation", stale.SkipReason ?? "", StringComparison.OrdinalIgnoreCase);
        var fresh = Assert.Single(refresh.Rewrite ?? [], item => item.Applied && !item.StaleGeneration);
        Assert.False(string.IsNullOrWhiteSpace(fresh.ShadowCopyPath), compact);
    }

    [AnalyzerLifecycleFact]
    public async Task Restart_required_does_not_execute_stale_v1_or_v2_on_later_publication()
    {
        using var fixture = GeneratorConsumerFixture.CreateTwoGenerators(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var realA = RequireOutput(fixture.FindGeneratorOutputDll());
        var realB = RequireOutput(fixture.FindSecondGeneratorOutputDll());

        var first = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.Equal(2, first.AppliedCount);
        var before = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        Assert.Equal(GeneratorConsumerFixture.MarkerV1, before.Marker);

        fixture.SetGeneratorMarker(GeneratorConsumerFixture.MarkerV2);
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var refresh = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var compact = Compact("restart", refresh, realA, realB);
        output.WriteLine(compact);
        Assert.True(refresh.BlockedCount >= 1, compact);
        Assert.DoesNotContain(
            refresh.Rewrite ?? [],
            item => item.Applied && IsGeneratorA(item));

        var oracle = await Epoch1HostOps.OracleAsync(host);
        AssertNoRealPublication(oracle, realA, Compact("restart oracle A", oracle, realA, realB));
        AssertNoRealPublication(oracle, realB, Compact("restart oracle B", oracle, realA, realB));
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, oracle.Marker);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV2, oracle.Marker);

        var edit = await host.SendAsync(
            new HostCommand
            {
                Op = "updateDocument",
                Path = fixture.ConsumerSourcePath,
                Text = fixture.WithConsumerComment("v3-s5-restart-edit"),
            });
        Assert.True(edit.Ok, Compact("restart edit", edit, realA, realB));
        var afterEdit = await Epoch1HostOps.OracleAsync(host);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, afterEdit.Marker);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV2, afterEdit.Marker);
        AssertNoRealPublication(afterEdit, realA, Compact("after-edit A", afterEdit, realA, realB));

        var later = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(later.CacheHit, Compact("later", later, realA, realB));
        var laterOracle = await Epoch1HostOps.OracleAsync(host);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, laterOracle.Marker);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV2, laterOracle.Marker);
        AssertNoRealPublication(laterOracle, realA, Compact("later A", laterOracle, realA, realB));
    }

    [AnalyzerLifecycleFact]
    public async Task Load_summary_matches_snapshot_for_none_applied_and_full_ban()
    {
        using var fixture = GeneratorConsumerFixture.CreateTwoGenerators(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var realA = RequireOutput(fixture.FindGeneratorOutputDll());
        var realB = RequireOutput(fixture.FindSecondGeneratorOutputDll());

        _ = await host.SendAsync(new HostCommand { Op = "injectPrepareFailure" });
        var none = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var noneCompact = Compact("none-applied", none, realA, realB);
        output.WriteLine(noneCompact);
        Assert.Equal(0, none.AppliedCount);
        Assert.True(none.BlockedCount >= 1, noneCompact);
        Assert.Equal("Banned", none.PublicationAdmission);
        Assert.Contains("prepared files were not applied", none.ShadowCopySummary ?? "", StringComparison.OrdinalIgnoreCase);
        AssertNoRealPublication(none, realA, noneCompact);
        AssertNoRealPublication(none, realB, noneCompact);
        var noneOracle = await Epoch1HostOps.OracleAsync(host);
        Assert.True(string.IsNullOrEmpty(noneOracle.Marker), Compact("none oracle", noneOracle, realA, realB));

        await using var restartHost = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(restartHost, fixture.SolutionPath);
        var first = await Epoch1HostOps.LoadAsync(restartHost, fixture.SolutionPath, shadowCopy: true);
        Assert.Equal(2, first.AppliedCount);
        await Epoch1HostOps.RequireMarkerAsync(restartHost, GeneratorConsumerFixture.MarkerV1);
        fixture.SetGeneratorMarker(GeneratorConsumerFixture.MarkerV2);
        await Epoch1HostOps.BuildAsync(restartHost, fixture.GeneratorProjectPath);
        var banned = await Epoch1HostOps.LoadAsync(restartHost, fixture.SolutionPath, shadowCopy: true);
        var banCompact = Compact("full-ban", banned, realA, realB);
        output.WriteLine(banCompact);
        Assert.True(banned.BlockedCount >= 1, banCompact);
        Assert.DoesNotContain(
            banned.Rewrite ?? [],
            item => item.Applied && IsGeneratorA(item));
        Assert.False(banned.RefreshComplete, banCompact);
        Assert.Contains("Partial prepare", banned.ShadowCopySummary ?? "", StringComparison.Ordinal);
        var bannedOracle = await Epoch1HostOps.OracleAsync(restartHost);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, bannedOracle.Marker);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV2, bannedOracle.Marker);
    }

    private static async Task AssertAllowedMarkerExactAsync(
        LifecycleHostClient host,
        RewriteDto applied,
        RewriteDto blocked,
        string compact)
    {
        var appliedIsA = IsGeneratorA(applied);
        var allowedType = appliedIsA ? "GeneratedMarker" : "GeneratedMarkerB";
        var blockedType = appliedIsA ? "GeneratedMarkerB" : "GeneratedMarker";
        var allowedMarker = appliedIsA ? GeneratorConsumerFixture.MarkerV1 : GeneratorConsumerFixture.MarkerB;
        var blockedMarker = appliedIsA ? GeneratorConsumerFixture.MarkerB : GeneratorConsumerFixture.MarkerV1;

        var allowed = await Epoch1HostOps.OracleAsync(host, generatedType: allowedType);
        Assert.True(allowed.OracleSuccess, compact + " allowed=" + allowed.OracleFailure);
        Assert.Equal(allowedMarker, allowed.Marker);

        var denied = await Epoch1HostOps.OracleAsync(host, generatedType: blockedType);
        Assert.True(string.IsNullOrEmpty(denied.Marker) || denied.Marker != blockedMarker, compact);
        Assert.NotEqual(blockedMarker, denied.Marker);
        _ = blocked;
    }

    private static async Task AssertForcedRebuildOfAppliedAsync(
        LifecycleHostClient host,
        GeneratorConsumerFixture fixture,
        RewriteDto applied,
        string realA,
        string realB)
    {
        var appliedIsA = IsGeneratorA(applied);
        var project = appliedIsA ? fixture.GeneratorProjectPath : fixture.SecondGeneratorProjectPath;
        var source = appliedIsA ? fixture.GeneratorSourcePath : fixture.SecondGeneratorSourcePath;
        Assert.False(string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(source));
        var dll = appliedIsA ? realA : realB;
        var before = await host.SendAsync(new HostCommand { Op = "hashFile", Path = dll });
        Assert.False(string.IsNullOrWhiteSpace(before.FileSha256));

        File.AppendAllText(source!, Environment.NewLine + "// rebuild-bump " + Guid.NewGuid().ToString("N"));
        await Epoch1HostOps.BuildAsync(host, project!);
        var after = await host.SendAsync(new HostCommand { Op = "hashFile", Path = dll });
        Assert.False(string.IsNullOrWhiteSpace(after.FileSha256), after.Error);
        Assert.NotEqual(before.FileSha256, after.FileSha256);
    }

    private static bool IsConfirmedRewrite(RewriteDto item)
    {
        return !string.Equals(item.ReasonCode, "proven_foreign_path", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.ReasonCode, "provenance_unconfirmed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratorA(RewriteDto item)
    {
        return string.Equals(item.MatchedProjectName, "Generator", StringComparison.OrdinalIgnoreCase)
            || (item.OriginalFullPath is not null
                && Path.GetFileNameWithoutExtension(item.OriginalFullPath)
                    .Equals("Generator", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertNoRealPublication(HostResponse response, string realOutput, string compact)
    {
        Assert.DoesNotContain(
            response.OverlayAnalyzerPaths ?? [],
            path => PathsEqual(path, realOutput));
        Assert.False(PathsEqual(response.OverlayAnalyzerPath, realOutput), compact);
        Assert.False(PathsEqual(response.LoadedAnalyzerPath, realOutput), compact);
        Assert.DoesNotContain(
            response.LoadedAssemblies ?? [],
            assembly => PathsEqual(assembly.Location, realOutput)
                || PathsEqual(assembly.RequestedPath, realOutput));
        Assert.DoesNotContain(
            response.ProcessAnalyzerAssemblies ?? [],
            assembly => PathsEqual(assembly.Location, realOutput)
                || PathsEqual(assembly.RequestedPath, realOutput));
    }

    private static void AssertNoPublicationIo(HostResponse before, HostResponse after, string compact)
    {
        Assert.Equal(before.OverlayPrepareCount, after.OverlayPrepareCount);
        Assert.Equal(before.AnalyzerFileIoCount, after.AnalyzerFileIoCount);
        Assert.Equal(before.InspectorInspectCount, after.InspectorInspectCount);
        Assert.Equal(before.AnalyzerPathProbeCount, after.AnalyzerPathProbeCount);
        Assert.Equal(before.AnalyzerAssemblyEvaluationCount, after.AnalyzerAssemblyEvaluationCount);
        Assert.True(compact.Length > 0, compact);
    }

    private static string RequireOutput(string? path)
    {
        Assert.False(string.IsNullOrWhiteSpace(path), "generator output missing after build");
        Assert.True(File.Exists(path), "generator output missing after build: " + path);
        return Path.GetFullPath(path);
    }

    private static string Compact(string label, HostResponse response, string? realA = null, string? realB = null)
    {
        var parts = new List<string>
        {
            label,
            "ok=" + response.Ok,
            "err=" + response.Error,
            "admit=" + response.PublicationAdmission,
            "prepared=" + response.PreparedCount,
            "applied=" + response.AppliedCount,
            "stale=" + response.StaleCount,
            "blocked=" + response.BlockedCount,
            "refresh=" + response.RefreshComplete,
            "exec=" + response.Execution?.Status,
            "marker=" + response.Marker,
            "summary=" + response.ShadowCopySummary,
        };
        if (realA is not null)
        {
            parts.Add("realA=" + (response.OverlayAnalyzerPaths ?? []).Any(path => PathsEqual(path, realA)));
        }

        if (realB is not null)
        {
            parts.Add("realB=" + (response.OverlayAnalyzerPaths ?? []).Any(path => PathsEqual(path, realB)));
        }

        return string.Join(" ", parts);
    }

    private static bool PathsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
