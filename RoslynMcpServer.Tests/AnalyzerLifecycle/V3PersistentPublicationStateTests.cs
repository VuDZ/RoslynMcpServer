using RoslynMcpServer.LifecycleTestHost;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// S3 persistent publication admission: a failed opt-in stays fail-closed across
/// edit, flush, reconciliation, cancel recovery, and cached false/omitted.
/// </summary>
[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
[Trait("Category", "V3Regression")]
public sealed class V3PersistentPublicationStateTests(ITestOutputHelper output)
{
    [AnalyzerLifecycleFact]
    public async Task Watcher_flush_after_prepare_failure_stays_fail_closed()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var realOutput = RequireGeneratorOutput(fixture);
        var afterBan = await BanOptInAsync(host, fixture.SolutionPath, realOutput);

        var text = fixture.WithConsumerComment("v3-s3-fsw");
        File.WriteAllText(fixture.ConsumerSourcePath, text);
        var wait = await host.SendAsync(
            new HostCommand { Op = "waitDirty", Path = fixture.ConsumerSourcePath, TimeoutMs = 15_000 });
        Assert.True(wait.DirtyDelivered, Compact("fsw wait", wait, realOutput));

        var flushed = await host.SendAsync(
            new HostCommand { Op = "flushFind", Path = fixture.ConsumerSourcePath });
        Assert.True(flushed.Ok, Compact("fsw flush", flushed, realOutput));
        Assert.Equal(text, flushed.DocumentText);
        AssertNoPublicationIo(afterBan, flushed, Compact("fsw io", flushed, realOutput));

        var oracle = await Epoch1HostOps.OracleAsync(host);
        var compact = Compact("fsw oracle", oracle, realOutput);
        output.WriteLine(compact);
        AssertPublishedSemanticSafe(oracle, realOutput, compact);
        Assert.Equal("Banned", oracle.PublicationAdmission);
    }

    [AnalyzerLifecycleFact]
    public async Task Reconciliation_after_prepare_failure_stays_fail_closed()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var realOutput = RequireGeneratorOutput(fixture);
        var afterBan = await BanOptInAsync(host, fixture.SolutionPath, realOutput);

        _ = await host.SendAsync(new HostCommand { Op = "injectApplyFailure" });
        var text = fixture.WithConsumerComment("v3-s3-recon");
        var apply = await host.SendAsync(
            new HostCommand { Op = "updateDocument", Path = fixture.ConsumerSourcePath, Text = text });
        var applyCompact = Compact("recon apply", apply, realOutput);
        output.WriteLine(applyCompact);
        Assert.False(apply.Ok, applyCompact);
        Assert.Equal("ReconciliationSucceeded", apply.WriteStatus);
        Assert.Equal(text, fixture.ReadConsumerSource());
        AssertNoPublicationIo(afterBan, apply, applyCompact);

        var oracle = await Epoch1HostOps.OracleAsync(host);
        var compact = Compact("recon oracle", oracle, realOutput);
        output.WriteLine(compact);
        AssertPublishedSemanticSafe(oracle, realOutput, compact);
        Assert.Equal("Banned", oracle.PublicationAdmission);
    }

    [AnalyzerLifecycleFact]
    public async Task Cancelled_load_boundary_then_edit_stays_fail_closed()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var realOutput = RequireGeneratorOutput(fixture);

        var cancelled = await host.SendAsync(new HostCommand
        {
            Op = "atomicLoadSemantic",
            Path = fixture.SolutionPath,
            Arguments = "cancel",
        });
        var cancelCompact = Compact("cancel load", cancelled, realOutput);
        output.WriteLine(cancelCompact);
        Assert.True(cancelled.Ok, cancelCompact);
        Assert.True(cancelled.Concurrency!.LoadCancelled, cancelCompact);
        Assert.False(cancelled.PrepareAttempted, cancelCompact);
        Assert.Equal("Banned", cancelled.PublicationAdmission);
        AssertNoRealPublication(cancelled, realOutput, cancelCompact);

        var afterCancel = await host.SendAsync(new HostCommand { Op = "inspect" });
        var edit = await host.SendAsync(
            new HostCommand
            {
                Op = "updateDocument",
                Path = fixture.ConsumerSourcePath,
                Text = fixture.WithConsumerComment("v3-s3-cancel-edit"),
            });
        Assert.True(edit.Ok, Compact("cancel edit", edit, realOutput));
        AssertNoPublicationIo(afterCancel, edit, Compact("cancel io", edit, realOutput));

        var oracle = await Epoch1HostOps.OracleAsync(host);
        var compact = Compact("cancel oracle", oracle, realOutput);
        output.WriteLine(compact);
        AssertPublishedSemanticSafe(oracle, realOutput, compact);
        Assert.Equal("Banned", oracle.PublicationAdmission);
    }

    [AnalyzerLifecycleFact]
    public async Task Cached_false_and_omitted_do_not_lift_failed_opt_in_ban()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var realOutput = RequireGeneratorOutput(fixture);
        _ = await BanOptInAsync(host, fixture.SolutionPath, realOutput);

        var cachedFalse = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        var falseCompact = Compact("cached false", cachedFalse, realOutput);
        output.WriteLine(falseCompact);
        Assert.True(cachedFalse.Ok, falseCompact);
        Assert.True(cachedFalse.CacheHit, falseCompact);
        Assert.False(cachedFalse.PrepareAttempted, falseCompact);
        Assert.Equal("Banned", cachedFalse.PublicationAdmission);
        AssertNoRealPublication(cachedFalse, realOutput, falseCompact);

        var omitted = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: null);
        var omittedCompact = Compact("cached omitted", omitted, realOutput);
        output.WriteLine(omittedCompact);
        Assert.True(omitted.Ok, omittedCompact);
        Assert.True(omitted.CacheHit, omittedCompact);
        Assert.False(omitted.PrepareAttempted, omittedCompact);
        Assert.Equal("Banned", omitted.PublicationAdmission);
        AssertNoRealPublication(omitted, realOutput, omittedCompact);

        var edit = await host.SendAsync(
            new HostCommand
            {
                Op = "updateDocument",
                Path = fixture.ConsumerSourcePath,
                Text = fixture.WithConsumerComment("v3-s3-cached-false-edit"),
            });
        Assert.True(edit.Ok, Compact("cached-false edit", edit, realOutput));
        AssertNoPublicationIo(omitted, edit, Compact("cached-false io", edit, realOutput));

        var oracle = await Epoch1HostOps.OracleAsync(host);
        AssertPublishedSemanticSafe(oracle, realOutput, Compact("cached-false oracle", oracle, realOutput));
    }

    [AnalyzerLifecycleFact]
    public async Task Cached_true_successful_prepare_restores_shadow_without_disabling_identity_gate()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var realOutput = RequireGeneratorOutput(fixture);
        _ = await BanOptInAsync(host, fixture.SolutionPath, realOutput);

        var restored = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var restoreCompact = Compact("restore load", restored, realOutput);
        output.WriteLine(restoreCompact);
        Assert.True(restored.Ok, restoreCompact);
        Assert.True(restored.CacheHit, restoreCompact);
        Assert.True(restored.PrepareAttempted, restoreCompact);
        Assert.False(restored.PrepareInjectedFailure, restoreCompact);
        Assert.True(restored.ShadowEnabled, restoreCompact);
        Assert.Equal("AllowedMapping", restored.PublicationAdmission);
        Assert.False(PathsEqual(restored.OverlayAnalyzerPath, realOutput), restoreCompact);
        Assert.False(string.IsNullOrWhiteSpace(restored.OverlayAnalyzerPath), restoreCompact);
        Assert.Equal("ReferenceRewritten", restored.Execution?.Status);

        var oracle = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var compact = Compact("restore oracle", oracle, realOutput);
        output.WriteLine(compact);
        Assert.False(PathsEqual(oracle.OverlayAnalyzerPath, realOutput), compact);
        Assert.False(PathsEqual(oracle.LoadedAnalyzerPath, realOutput), compact);
        AssertPathEqual(oracle.OverlayAnalyzerPath, oracle.LoadedAnalyzerPath);
    }

    [AnalyzerLifecycleFact]
    public async Task No_overlay_load_still_allows_raw_references()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var realOutput = RequireGeneratorOutput(fixture);

        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        var loadCompact = Compact("no-overlay load", load, realOutput);
        output.WriteLine(loadCompact);
        Assert.True(load.Ok, loadCompact);
        Assert.Equal("NoOverlay", load.PublicationAdmission);
        Assert.False(load.ShadowEnabled, loadCompact);
        Assert.True(PathsEqual(load.OverlayAnalyzerPath, realOutput), loadCompact);

        var afterLoad = await host.SendAsync(new HostCommand { Op = "inspect" });
        var edit = await host.SendAsync(
            new HostCommand
            {
                Op = "updateDocument",
                Path = fixture.ConsumerSourcePath,
                Text = fixture.WithConsumerComment("v3-s3-no-overlay"),
            });
        Assert.True(edit.Ok, Compact("no-overlay edit", edit, realOutput));
        AssertNoPublicationIo(afterLoad, edit, Compact("no-overlay io", edit, realOutput));

        var oracle = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var compact = Compact("no-overlay oracle", oracle, realOutput);
        output.WriteLine(compact);
        Assert.True(PathsEqual(oracle.OverlayAnalyzerPath, realOutput), compact);
        Assert.True(PathsEqual(oracle.LoadedAnalyzerPath, realOutput), compact);
        Assert.Equal("NoOverlay", oracle.PublicationAdmission);
    }

    [AnalyzerLifecycleFact]
    public async Task Successful_opt_in_executes_exact_marker_from_shadow_path()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var realOutput = RequireGeneratorOutput(fixture);

        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var loadCompact = Compact("opt-in load", load, realOutput);
        output.WriteLine(loadCompact);
        Assert.True(load.Ok, loadCompact);
        Assert.True(load.ShadowEnabled, loadCompact);
        Assert.Equal("AllowedMapping", load.PublicationAdmission);
        Assert.False(PathsEqual(load.OverlayAnalyzerPath, realOutput), loadCompact);

        var beforeEdit = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        Assert.False(PathsEqual(beforeEdit.OverlayAnalyzerPath, realOutput), Compact("opt-in before-edit", beforeEdit, realOutput));
        AssertPathEqual(beforeEdit.OverlayAnalyzerPath, beforeEdit.LoadedAnalyzerPath);

        var afterLoad = await host.SendAsync(new HostCommand { Op = "inspect" });
        var edit = await host.SendAsync(
            new HostCommand
            {
                Op = "updateDocument",
                Path = fixture.ConsumerSourcePath,
                Text = fixture.WithConsumerComment("v3-s3-opt-in"),
            });
        Assert.True(edit.Ok, Compact("opt-in edit", edit, realOutput));
        AssertNoPublicationIo(afterLoad, edit, Compact("opt-in io", edit, realOutput));

        var oracle = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var compact = Compact("opt-in oracle", oracle, realOutput);
        output.WriteLine(compact);
        Assert.False(PathsEqual(oracle.OverlayAnalyzerPath, realOutput), compact);
        AssertPathEqual(oracle.OverlayAnalyzerPath, oracle.LoadedAnalyzerPath);
        Assert.Equal(GeneratorConsumerFixture.MarkerV1, oracle.Marker);
    }

    private static async Task<HostResponse> BanOptInAsync(
        LifecycleHostClient host,
        string solutionPath,
        string realOutput)
    {
        _ = await host.SendAsync(new HostCommand { Op = "injectPrepareFailure" });
        var load = await Epoch1HostOps.LoadAsync(host, solutionPath, shadowCopy: true);
        var compact = Compact("ban load", load, realOutput);
        Assert.True(load.Ok, compact);
        Assert.True(load.PrepareInjectedFailure, compact);
        Assert.False(load.ShadowEnabled, compact);
        Assert.Equal("Banned", load.PublicationAdmission);
        AssertNoRealPublication(load, realOutput, compact);
        return load;
    }

    private static void AssertPublishedSemanticSafe(HostResponse response, string realOutput, string compact)
    {
        Assert.False(response.OracleSuccess, compact);
        AssertNoRealPublication(response, realOutput, compact);
        Assert.True(string.IsNullOrEmpty(response.Marker), compact);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, response.Marker);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV2, response.Marker);
    }

    private static void AssertNoRealPublication(HostResponse response, string realOutput, string compact)
    {
        Assert.False(PathsEqual(response.OverlayAnalyzerPath, realOutput), compact);
        Assert.False(PathsEqual(response.LoadedAnalyzerPath, realOutput), compact);
        Assert.False(PathsEqual(response.Execution?.LoadedPath, realOutput), compact);
        Assert.DoesNotContain(
            response.OverlayAnalyzerPaths ?? [],
            path => PathsEqual(path, realOutput));
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
        Assert.True(string.IsNullOrEmpty(compact) || compact.Length > 0, compact);
    }

    private static void AssertPathEqual(string? left, string? right)
    {
        Assert.True(PathsEqual(left, right), "expected " + left + " == " + right);
    }

    private static string RequireGeneratorOutput(GeneratorConsumerFixture fixture)
    {
        var path = fixture.FindGeneratorOutputDll();
        Assert.False(string.IsNullOrWhiteSpace(path), "generator output missing after build");
        Assert.True(File.Exists(path), "generator output missing after build: " + path);
        return Path.GetFullPath(path);
    }

    private static string Compact(string label, HostResponse response, string? realOutput = null)
    {
        var parts = new List<string>
        {
            label,
            "ok=" + response.Ok,
            "err=" + response.Error,
            "status=" + response.WriteStatus,
            "reason=" + response.WriteReason,
            "admit=" + response.PublicationAdmission,
            "shadow=" + response.ShadowEnabled,
            "exec=" + response.Execution?.Status,
            "publishedRef=" + ShortPath(response.OverlayAnalyzerPath),
            "loaded=" + ShortPath(response.LoadedAnalyzerPath),
            "marker=" + response.Marker,
            "oracle=" + response.OracleSuccess + "/" + response.OracleFailure,
            "inspect=" + response.InspectorInspectCount,
            "probe=" + response.AnalyzerPathProbeCount,
            "eval=" + response.AnalyzerAssemblyEvaluationCount,
            "fileIo=" + response.AnalyzerFileIoCount,
            "prepare=" + response.OverlayPrepareCount,
        };

        if (realOutput is not null)
        {
            parts.Add("realPublished=" + PathsEqual(response.OverlayAnalyzerPath, realOutput));
            parts.Add("realProcess=" + HasPath(response.ProcessAnalyzerAssemblies, realOutput));
        }

        return string.Join(" ", parts);
    }

    private static bool HasPath(IReadOnlyCollection<LoadedAssemblyDto>? assemblies, string expected) =>
        (assemblies ?? []).Any(assembly =>
            PathsEqual(assembly.Location, expected) || PathsEqual(assembly.RequestedPath, expected));

    private static string ShortPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "-";
        }

        try
        {
            var file = Path.GetFileName(path);
            var parent = Path.GetFileName(Path.GetDirectoryName(path));
            return string.IsNullOrWhiteSpace(parent) ? file : parent + "/" + file;
        }
        catch
        {
            return path;
        }
    }

    private static bool PathsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
