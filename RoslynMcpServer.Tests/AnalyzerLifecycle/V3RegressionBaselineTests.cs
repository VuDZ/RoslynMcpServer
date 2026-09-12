using RoslynMcpServer.LifecycleTestHost;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// Permanent V3-R1/R2/R3 reproductions. Assertions describe the safe contract,
/// not the defective v1.3.15 outcome. Each case uses its own host process and
/// the production published accessor, never <c>oracleSource=workspace</c>.
/// </summary>
[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
[Trait("Category", "V3Regression")]
public sealed class V3RegressionBaselineTests(ITestOutputHelper output)
{
    [AnalyzerLifecycleFact]
    public async Task V3_R1_same_session_stale_candidate_is_rejected_before_any_write()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);

        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(load.Ok, Compact("R1 load", load));

        var textA = fixture.WithConsumerComment("v3-r1-held-A");
        var hold = await host.SendAsync(
            new HostCommand { Op = "holdOverlayEdit", Path = fixture.ConsumerSourcePath, Text = textA });
        Assert.True(hold.Ok, Compact("R1 hold", hold));

        var textB = fixture.WithConsumerComment("v3-r1-written-B");
        Assert.NotEqual(textA, textB);
        var writeB = await host.SendAsync(
            new HostCommand { Op = "updateDocument", Path = fixture.ConsumerSourcePath, Text = textB });
        Assert.True(writeB.Ok, Compact("R1 write-B", writeB));
        Assert.Equal(textB, fixture.ReadConsumerSource());

        var apply = await host.SendAsync(new HostCommand { Op = "applyHeld" });
        var published = await ReadPublishedDocumentAsync(host, fixture.ConsumerSourcePath);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        var disk = fixture.ReadConsumerSource();
        var publishedText = published.DocumentText;
        var csprojUnchanged = snapshot.CsprojSha256 is not null
            && GeneratorConsumerFixture.ProjectBytesUnchanged(fixture.ProjectFileBytes, snapshot.CsprojSha256);
        var compact = Compact(
            "R1 apply",
            apply,
            disk: disk,
            publishedText: publishedText,
            expectedText: textB,
            extra: "csprojUnchanged=" + csprojUnchanged + " saved=" + FormatSaved(apply.SavedPaths));

        output.WriteLine(compact);
        Assert.False(apply.Ok, compact);
        Assert.True(
            string.Equals(apply.WriteStatus, "PreflightRejected", StringComparison.Ordinal),
            compact);
        Assert.True(apply.SavedPaths is null || apply.SavedPaths.Length == 0, compact);
        Assert.Equal(textB, disk);
        Assert.True(published.Ok, Compact("R1 published", published, publishedText: publishedText, expectedText: textB));
        Assert.Equal(textB, publishedText);
        Assert.DoesNotContain("v3-r1-held-A", disk, StringComparison.Ordinal);
        Assert.DoesNotContain("v3-r1-held-A", publishedText ?? string.Empty, StringComparison.Ordinal);
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
    }

    [AnalyzerLifecycleFact]
    public async Task V3_R2_prepare_failure_stays_fail_closed_after_text_edit()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var realOutput = RequireGeneratorOutput(fixture);

        _ = await host.SendAsync(new HostCommand { Op = "injectPrepareFailure" });
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var loadCompact = Compact("R2 load", load, realOutput);
        output.WriteLine(loadCompact);
        Assert.True(load.Ok, loadCompact);
        Assert.True(load.PrepareAttempted, loadCompact);
        Assert.True(load.PrepareInjectedFailure, loadCompact);
        Assert.False(load.ShadowEnabled, loadCompact);
        Assert.Equal("LoadFailed", load.Execution?.Status);
        Assert.Null(load.OverlayAnalyzerPath);
        AssertNoRealPublication(load, realOutput, loadCompact);

        var edit = await host.SendAsync(
            new HostCommand
            {
                Op = "updateDocument",
                Path = fixture.ConsumerSourcePath,
                Text = fixture.WithConsumerComment("v3-r2-after-prepare-failure"),
            });
        Assert.True(edit.Ok, Compact("R2 edit", edit, realOutput));

        var afterEdit = await Epoch1HostOps.OracleAsync(host);
        var compact = Compact("R2 after-edit oracle", afterEdit, realOutput);
        output.WriteLine(compact);
        AssertPublishedSemanticSafe(afterEdit, realOutput, compact);
        AssertNoRealPublication(afterEdit, realOutput, compact);
        AssertNoExactMarker(afterEdit, compact);
    }

    [AnalyzerLifecycleFact]
    public async Task V3_R3_corrupt_capture_does_not_publish_or_execute_real_output()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        var realOutput = RequireGeneratorOutput(fixture);

        var inject = await host.SendAsync(
            new HostCommand
            {
                Op = "injectCaptureFailure",
                CaptureFailureMode = "Corrupt",
            });
        Assert.True(inject.Ok, Compact("R3 inject", inject, realOutput));

        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var loadCompact = Compact("R3 load", load, realOutput);
        output.WriteLine(loadCompact);
        Assert.True(load.Ok, loadCompact);
        Assert.Equal("Failed", load.ProvenanceCaptureStatus);
        Assert.Equal(0, load.ProvenanceConfirmedBindingCount);
        Assert.False(load.ShadowEnabled, loadCompact);
        AssertNoRealPublication(load, realOutput, loadCompact);

        var semantic = await Epoch1HostOps.OracleAsync(host);
        var compact = Compact("R3 published oracle", semantic, realOutput);
        output.WriteLine(compact);
        Assert.False(IsPublishedSemanticAvailable(semantic), compact);
        Assert.Equal("no-solution", semantic.Error);
        AssertNoRealPublication(semantic, realOutput, compact);
        AssertNoExactMarker(semantic, compact);
    }

    private static async Task<HostResponse> ReadPublishedDocumentAsync(LifecycleHostClient host, string path)
    {
        return await host.SendAsync(new HostCommand { Op = "publishedDocument", Path = path });
    }

    private static void AssertPublishedSemanticSafe(HostResponse response, string realOutput, string compact)
    {
        Assert.False(response.OracleSuccess, compact);
        AssertNoRealPublication(response, realOutput, compact);
        AssertNoExactMarker(response, compact);
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

    private static void AssertNoExactMarker(HostResponse response, string compact)
    {
        Assert.True(string.IsNullOrEmpty(response.Marker), compact);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, response.Marker);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV2, response.Marker);
    }

    private static bool IsPublishedSemanticAvailable(HostResponse response) =>
        !string.Equals(response.Error, "no-solution", StringComparison.Ordinal)
        && (response.OracleSuccess
            || !string.IsNullOrWhiteSpace(response.OverlayAnalyzerPath)
            || !string.IsNullOrWhiteSpace(response.LoadedAnalyzerPath)
            || !string.IsNullOrWhiteSpace(response.Marker));

    private static string RequireGeneratorOutput(GeneratorConsumerFixture fixture)
    {
        var path = fixture.FindGeneratorOutputDll();
        Assert.False(string.IsNullOrWhiteSpace(path), "generator output missing after build");
        Assert.True(File.Exists(path), "generator output missing after build: " + path);
        return Path.GetFullPath(path);
    }

    private static string Compact(
        string label,
        HostResponse response,
        string? realOutput = null,
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
            "exec=" + response.Execution?.Status,
            "execReason=" + response.Execution?.Reason,
            "capture=" + response.ProvenanceCaptureStatus,
            "shadow=" + response.ShadowEnabled,
            "publishedRef=" + ShortPath(response.OverlayAnalyzerPath),
            "loaded=" + ShortPath(response.LoadedAnalyzerPath),
            "execLoaded=" + ShortPath(response.Execution?.LoadedPath),
            "marker=" + response.Marker,
            "oracle=" + response.OracleSuccess + "/" + response.OracleFailure,
        };

        if (realOutput is not null)
        {
            parts.Add("real=" + ShortPath(realOutput));
            parts.Add("realPublished=" + PathsEqual(response.OverlayAnalyzerPath, realOutput));
            parts.Add("realLoaded=" + PathsEqual(response.LoadedAnalyzerPath, realOutput));
            parts.Add("realExec=" + PathsEqual(response.Execution?.LoadedPath, realOutput));
            parts.Add("realProcess=" + HasPath(response.ProcessAnalyzerAssemblies, realOutput));
            parts.Add("realLoader=" + HasPath(response.LoadedAssemblies, realOutput));
        }

        if (expectedText is not null)
        {
            parts.Add("diskEqExpected=" + (disk == expectedText));
            parts.Add("publishedEqExpected=" + (publishedText == expectedText));
            parts.Add("diskHasHeldA=" + (disk?.Contains("v3-r1-held-A", StringComparison.Ordinal) == true));
            parts.Add("publishedHasHeldA=" + (publishedText?.Contains("v3-r1-held-A", StringComparison.Ordinal) == true));
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
            : saved.Length.ToString() + ":" + string.Join(",", saved.Select(ShortPath));

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
