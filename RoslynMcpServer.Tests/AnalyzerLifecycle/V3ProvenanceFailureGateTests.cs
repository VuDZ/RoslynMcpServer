using RoslynMcpServer.LifecycleTestHost;
using RoslynMcpServer.Services;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// S4: unsuitable provenance capture withholds the entire semantic snapshot.
/// Missing, corrupt, mixed, Incomplete/null, and session-mismatch never publish raw.
/// </summary>
[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
[Trait("Category", "V3Regression")]
public sealed class V3ProvenanceFailureGateTests(ITestOutputHelper output)
{
    [AnalyzerLifecycleFact]
    public async Task Missing_capture_withholds_semantics_and_cleans_temp_binlogs()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var realOutput = RequireGeneratorOutput(fixture);

        _ = await host.SendAsync(new HostCommand
        {
            Op = "injectCaptureFailure",
            CaptureFailureMode = "Missing",
        });
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var compact = Compact("missing load", load, realOutput);
        output.WriteLine(compact);
        AssertWithheldOptIn(load, "Failed", AnalyzerProvenanceCaptureGate.ReasonFailed, realOutput, compact);
        Assert.Equal(0, load.ProvenanceTempDirectoryCount);
        AssertNoSemanticSnapshot(await Epoch1HostOps.OracleAsync(host), realOutput);
    }

    [AnalyzerLifecycleFact]
    public async Task Mixed_valid_and_corrupt_binlogs_withhold_incomplete_capture()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var realOutput = RequireGeneratorOutput(fixture);

        _ = await host.SendAsync(new HostCommand
        {
            Op = "injectCaptureFailure",
            CaptureFailureMode = "MixedValidAndCorrupt",
        });
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var compact = Compact("mixed load", load, realOutput);
        output.WriteLine(compact);
        AssertWithheldOptIn(load, "Incomplete", AnalyzerProvenanceCaptureGate.ReasonIncomplete, realOutput, compact);
        Assert.True(load.ProvenanceAnalyzerItemCount > 0, compact);
        AssertNoSemanticSnapshot(await Epoch1HostOps.OracleAsync(host), realOutput);
    }

    [AnalyzerLifecycleFact]
    public async Task Null_snapshot_and_foreign_session_withhold_semantics()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var realOutput = RequireGeneratorOutput(fixture);

        _ = await host.SendAsync(new HostCommand { Op = "injectNullCapture" });
        var missing = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var missingCompact = Compact("null snapshot", missing, realOutput);
        output.WriteLine(missingCompact);
        AssertWithheldOptIn(missing, null, AnalyzerProvenanceCaptureGate.ReasonMissing, realOutput, missingCompact);
        Assert.False(missing.ProvenanceSnapshotPresent, missingCompact);
        AssertNoSemanticSnapshot(await Epoch1HostOps.OracleAsync(host), realOutput);

        _ = await host.SendAsync(new HostCommand { Op = "reset" });
        _ = await host.SendAsync(new HostCommand { Op = "injectForeignCaptureSession" });
        var foreign = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var foreignCompact = Compact("foreign session", foreign, realOutput);
        output.WriteLine(foreignCompact);
        AssertWithheldOptIn(
            foreign,
            "Complete",
            AnalyzerProvenanceCaptureGate.ReasonSessionMismatch,
            realOutput,
            foreignCompact);
        Assert.True(foreign.ProvenanceSnapshotPresent, foreignCompact);
        AssertNoSemanticSnapshot(await Epoch1HostOps.OracleAsync(host), realOutput);
    }

    [AnalyzerLifecycleFact]
    public async Task After_failed_capture_edit_flush_and_cached_false_do_not_open_semantics()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var realOutput = RequireGeneratorOutput(fixture);

        _ = await host.SendAsync(new HostCommand
        {
            Op = "injectCaptureFailure",
            CaptureFailureMode = "Corrupt",
        });
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        AssertWithheldOptIn(load, "Failed", AnalyzerProvenanceCaptureGate.ReasonFailed, realOutput, Compact("ban", load, realOutput));
        var sessionAfterBan = load.LoadSessionId;

        var edit = await host.SendAsync(new HostCommand
        {
            Op = "updateDocument",
            Path = fixture.ConsumerSourcePath,
            Text = fixture.WithConsumerComment("v3-s4-edit"),
        });
        output.WriteLine(Compact("edit", edit, realOutput));
        Assert.Equal("Unavailable", edit.PublicationAdmission);
        Assert.False(edit.PublishedSnapshotPresent);
        AssertNoRealPublication(edit, realOutput, Compact("edit paths", edit, realOutput));

        File.WriteAllText(fixture.ConsumerSourcePath, fixture.WithConsumerComment("v3-s4-fsw"));
        var flushed = await host.SendAsync(new HostCommand { Op = "flushGetter" });
        output.WriteLine(Compact("flush", flushed, realOutput));
        Assert.Equal("flush-getter-no-solution", flushed.Error);
        Assert.False(flushed.PublishedSnapshotPresent);
        Assert.Equal("Unavailable", flushed.PublicationAdmission);
        AssertNoRealPublication(flushed, realOutput, Compact("flush paths", flushed, realOutput));

        var cachedFalse = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        var falseCompact = Compact("cached false", cachedFalse, realOutput);
        output.WriteLine(falseCompact);
        Assert.True(cachedFalse.CacheHit, falseCompact);
        AssertWithheldOptIn(cachedFalse, "Failed", AnalyzerProvenanceCaptureGate.ReasonFailed, realOutput, falseCompact);

        var omitted = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: null);
        Assert.True(omitted.CacheHit, Compact("omitted", omitted, realOutput));
        AssertWithheldOptIn(omitted, "Failed", AnalyzerProvenanceCaptureGate.ReasonFailed, realOutput, Compact("omitted", omitted, realOutput));

        var cachedTrue = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var trueCompact = Compact("cached true", cachedTrue, realOutput);
        output.WriteLine(trueCompact);
        Assert.True(cachedTrue.CacheHit, trueCompact);
        Assert.False(cachedTrue.ReopenedGraph, trueCompact);
        Assert.Equal(sessionAfterBan, cachedTrue.LoadSessionId);
        AssertWithheldOptIn(cachedTrue, "Failed", AnalyzerProvenanceCaptureGate.ReasonFailed, realOutput, trueCompact);
        Assert.Equal("Failed", cachedTrue.ProvenanceCaptureStatus);
        AssertNoSemanticSnapshot(await Epoch1HostOps.OracleAsync(host), realOutput);
    }

    [AnalyzerLifecycleFact]
    public async Task Reset_and_reopen_with_successful_capture_executes_shadow_v1_on_new_session()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var realOutput = RequireGeneratorOutput(fixture);

        _ = await host.SendAsync(new HostCommand
        {
            Op = "injectCaptureFailure",
            CaptureFailureMode = "Corrupt",
        });
        var failed = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        AssertWithheldOptIn(failed, "Failed", AnalyzerProvenanceCaptureGate.ReasonFailed, realOutput, Compact("failed", failed, realOutput));
        var failedSession = failed.LoadSessionId;

        _ = await host.SendAsync(new HostCommand { Op = "reset" });
        var restored = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var restoreCompact = Compact("reset reload", restored, realOutput);
        output.WriteLine(restoreCompact);
        Assert.True(restored.Ok, restoreCompact);
        Assert.False(restored.CacheHit, restoreCompact);
        Assert.True(restored.ReopenedGraph, restoreCompact);
        Assert.NotEqual(failedSession, restored.LoadSessionId);
        Assert.Equal("Complete", restored.ProvenanceCaptureStatus);
        Assert.Equal("AllowedMapping", restored.PublicationAdmission);
        Assert.True(restored.PublishedSnapshotPresent, restoreCompact);
        Assert.True(restored.ShadowEnabled, restoreCompact);
        Assert.False(PathsEqual(restored.OverlayAnalyzerPath, realOutput), restoreCompact);

        var oracle = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        Assert.False(PathsEqual(oracle.OverlayAnalyzerPath, realOutput), Compact("reset oracle", oracle, realOutput));
        AssertPathEqual(oracle.OverlayAnalyzerPath, oracle.LoadedAnalyzerPath);

        await using var fresh = LifecycleHostClient.Start();
        var freshLoad = await Epoch1HostOps.LoadAsync(fresh, fixture.SolutionPath, shadowCopy: true);
        Assert.True(freshLoad.ReopenedGraph, Compact("fresh load", freshLoad, realOutput));
        Assert.NotEqual(failedSession, freshLoad.LoadSessionId);
        var freshOracle = await Epoch1HostOps.RequireMarkerAsync(fresh, GeneratorConsumerFixture.MarkerV1);
        Assert.False(PathsEqual(freshOracle.OverlayAnalyzerPath, realOutput), Compact("fresh oracle", freshOracle, realOutput));
    }

    [AnalyzerLifecycleFact]
    public async Task Complete_foreign_fixture_is_not_treated_as_failed_capture()
    {
        using var fixture = GeneratorConsumerFixture.Create(
            OutputPathMode.SdkDefaultCorrectPath,
            foreignAnalyzer: true,
            includeAnalyzerProjectReference: false);
        await using var host = LifecycleHostClient.Start();
        var foreignProject = Path.Combine(fixture.Root, "ForeignGenerator", "ForeignGenerator.csproj");
        await Epoch1HostOps.BuildAsync(host, foreignProject);
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        await Epoch1HostOps.BuildAsync(host, fixture.ConsumerProjectPath);

        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var compact = Compact("foreign complete", load);
        output.WriteLine(compact);
        Assert.True(load.Ok, compact);
        Assert.Equal("Complete", load.ProvenanceCaptureStatus);
        Assert.NotEqual("Unavailable", load.PublicationAdmission);
        Assert.True(load.PublishedSnapshotPresent, compact);
        Assert.Contains(
            load.OverlayAnalyzerPaths ?? [],
            path => PathsEqual(path, fixture.ForeignDllPath));

        var oracle = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerForeign);
        Assert.Equal(GeneratorConsumerFixture.MarkerForeign, oracle.Marker);
    }

    [AnalyzerLifecycleFact]
    public async Task No_overlay_load_does_not_require_suitable_capture()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var realOutput = RequireGeneratorOutput(fixture);

        _ = await host.SendAsync(new HostCommand
        {
            Op = "injectCaptureFailure",
            CaptureFailureMode = "Corrupt",
        });
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        var compact = Compact("no-overlay failed capture", load, realOutput);
        output.WriteLine(compact);
        Assert.True(load.Ok, compact);
        Assert.Equal("Failed", load.ProvenanceCaptureStatus);
        Assert.Equal("NoOverlay", load.PublicationAdmission);
        Assert.True(load.PublishedSnapshotPresent, compact);
        Assert.True(PathsEqual(load.OverlayAnalyzerPath, realOutput), compact);

        var oracle = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        Assert.True(PathsEqual(oracle.OverlayAnalyzerPath, realOutput), Compact("no-overlay oracle", oracle, realOutput));
    }

    private static void AssertWithheldOptIn(
        HostResponse response,
        string? captureStatus,
        string reason,
        string realOutput,
        string compact)
    {
        Assert.True(response.Ok, compact);
        Assert.Equal(captureStatus, response.ProvenanceCaptureStatus);
        Assert.Equal("Unavailable", response.PublicationAdmission);
        Assert.Equal(reason, response.PublicationBanReason);
        Assert.False(response.PublishedSnapshotPresent, compact);
        Assert.False(response.ShadowEnabled, compact);
        Assert.False(response.PrepareAttempted, compact);
        Assert.Empty(response.Rewrite ?? []);
        AssertNoRealPublication(response, realOutput, compact);
    }

    private static void AssertNoSemanticSnapshot(HostResponse response, string realOutput)
    {
        var compact = Compact("oracle", response, realOutput);
        Assert.False(IsPublishedSemanticAvailable(response), compact);
        Assert.Equal("no-solution", response.Error);
        AssertNoRealPublication(response, realOutput, compact);
        Assert.True(string.IsNullOrEmpty(response.Marker), compact);
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

    private static bool IsPublishedSemanticAvailable(HostResponse response) =>
        !string.Equals(response.Error, "no-solution", StringComparison.Ordinal)
        && (response.OracleSuccess
            || response.PublishedSnapshotPresent
            || !string.IsNullOrWhiteSpace(response.OverlayAnalyzerPath)
            || !string.IsNullOrWhiteSpace(response.LoadedAnalyzerPath)
            || !string.IsNullOrWhiteSpace(response.Marker));

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
            "admit=" + response.PublicationAdmission,
            "ban=" + response.PublicationBanReason,
            "published=" + response.PublishedSnapshotPresent,
            "capture=" + response.ProvenanceCaptureStatus,
            "shadow=" + response.ShadowEnabled,
            "cache=" + response.CacheHit,
            "reopen=" + response.ReopenedGraph,
            "session=" + response.LoadSessionId,
            "prepare=" + response.PrepareAttempted,
            "publishedRef=" + ShortPath(response.OverlayAnalyzerPath),
            "marker=" + response.Marker,
            "oracle=" + response.OracleSuccess + "/" + response.OracleFailure,
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
