using RoslynMcpServer.LifecycleTestHost;
using Xunit;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// Shared assertions of the lifecycle suite. The recurring contract is that the overlay publishes a
/// shadow path and the real build output is never loaded or executed.
/// </summary>
internal static class LifecycleHostAssert
{
    public static bool PathsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static bool HasPath(IReadOnlyCollection<LoadedAssemblyDto>? assemblies, string expected) =>
        (assemblies ?? []).Any(assembly =>
            PathsEqual(assembly.Location, expected) || PathsEqual(assembly.RequestedPath, expected));

    public static bool HasExactPath(IReadOnlyCollection<LoadedAssemblyDto>? assemblies, string expected) =>
        (assemblies ?? []).Any(assembly => PathsEqual(assembly.Location, expected));

    public static void AssertPathEqual(string? left, string? right)
    {
        Assert.True(PathsEqual(left, right), "expected " + left + " == " + right);
    }

    /// <summary>
    /// The overlay path, the loaded path, the execution path, the published reference list, and both
    /// assembly snapshots must all disagree with the real build output.
    /// </summary>
    public static void AssertNoRealPublication(HostResponse response, string realOutput, string compact)
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

    public static void AssertNoExactMarker(HostResponse response, string compact)
    {
        Assert.True(string.IsNullOrEmpty(response.Marker), compact);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, response.Marker);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV2, response.Marker);
    }

    /// <summary>
    /// No publication IO may happen: no overlay prepare, no analyzer file IO, no inspector pass,
    /// no path probe, and no assembly evaluation.
    /// </summary>
    public static void AssertNoPublicationIo(HostResponse before, HostResponse after, string compact)
    {
        Assert.Equal(before.OverlayPrepareCount, after.OverlayPrepareCount);
        Assert.Equal(before.AnalyzerFileIoCount, after.AnalyzerFileIoCount);
        Assert.Equal(before.InspectorInspectCount, after.InspectorInspectCount);
        Assert.Equal(before.AnalyzerPathProbeCount, after.AnalyzerPathProbeCount);
        Assert.Equal(before.AnalyzerAssemblyEvaluationCount, after.AnalyzerAssemblyEvaluationCount);
        Assert.False(string.IsNullOrWhiteSpace(compact), "compact diagnostic text is required");
    }

    public static string RequireGeneratorOutput(GeneratorConsumerFixture fixture)
    {
        var path = fixture.FindGeneratorOutputDll();
        Assert.False(string.IsNullOrWhiteSpace(path), "generator output missing after build");
        Assert.True(File.Exists(path), "generator output missing after build: " + path);
        return Path.GetFullPath(path);
    }

    public static string ShortPath(string? path)
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

    public static string FormatSaved(string[]? saved) =>
        saved is null || saved.Length == 0
            ? "0"
            : saved.Length.ToString() + ":" + string.Join(",", saved.Select(ShortPath));

    public static string Compact(
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
            "op=" + response.Op,
            "ok=" + response.Ok,
            "err=" + response.Error,
            "status=" + response.WriteStatus,
            "reason=" + response.WriteReason,
            "admit=" + response.PublicationAdmission,
            "capture=" + response.ProvenanceCaptureStatus,
            "shadow=" + response.ShadowEnabled,
            "exec=" + response.Execution?.Status,
            "execReason=" + response.Execution?.Reason,
            "publishedRef=" + ShortPath(response.OverlayAnalyzerPath),
            "loaded=" + ShortPath(response.LoadedAnalyzerPath),
            "execLoaded=" + ShortPath(response.Execution?.LoadedPath),
            "marker=" + response.Marker,
            "oracle=" + response.OracleSuccess + "/" + response.OracleFailure,
            "inspect=" + response.InspectorInspectCount,
            "probe=" + response.AnalyzerPathProbeCount,
            "eval=" + response.AnalyzerAssemblyEvaluationCount,
            "fileIo=" + response.AnalyzerFileIoCount,
            "prepare=" + response.OverlayPrepareCount,
            "saved=" + FormatSaved(response.SavedPaths),
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
        }

        if (!string.IsNullOrWhiteSpace(extra))
        {
            parts.Add(extra);
        }

        return string.Join(" ", parts);
    }
}
