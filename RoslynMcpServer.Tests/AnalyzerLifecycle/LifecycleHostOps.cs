using RoslynMcpServer.LifecycleTestHost;
using Xunit;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// Host operations shared by the lifecycle suite: build, load, marker oracle, and the
/// forced-rebuild check.
/// </summary>
internal static class LifecycleHostOps
{
    public static async Task<HostResponse> BuildAsync(
        LifecycleHostClient host,
        string path,
        bool noIncremental = true,
        CancellationToken cancellationToken = default)
    {
        var response = await host.SendAsync(
            new HostCommand
            {
                Op = "build",
                Path = path,
                NoIncremental = noIncremental,
                TimeoutMs = 120_000,
            },
            cancellationToken).ConfigureAwait(false);
        Assert.True(
            response.Ok && response.BuildExitCode == 0,
            "dotnet build failed: " + response.Error + Environment.NewLine + response.BuildOutput);
        return response;
    }

    public static async Task<HostResponse> LoadAsync(
        LifecycleHostClient host,
        string path,
        bool? shadowCopy,
        CancellationToken cancellationToken = default)
    {
        return await host.SendAsync(
            new HostCommand
            {
                Op = "load",
                Path = path,
                ShadowCopy = shadowCopy,
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<HostResponse> OracleAsync(
        LifecycleHostClient host,
        string project = "Consumer",
        string? oracleSource = null,
        CancellationToken cancellationToken = default,
        string? generatedType = null)
    {
        return await host.SendAsync(
            new HostCommand
            {
                Op = "oracle",
                Project = project,
                OracleSource = oracleSource,
                Symbol = generatedType,
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<HostResponse> RequireMarkerAsync(
        LifecycleHostClient host,
        string expectedMarker,
        string project = "Consumer",
        CancellationToken cancellationToken = default)
    {
        var oracle = await OracleAsync(host, project, cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.True(
            oracle.OracleSuccess,
            "Oracle failed (empty diagnostics are not a version oracle). failure="
            + oracle.OracleFailure
            + " marker="
            + oracle.Marker
            + " generated="
            + oracle.GeneratedText);
        Assert.Equal(expectedMarker, oracle.Marker);
        return oracle;
    }

    /// <summary>
    /// Asserts that the identity gate refuses to execute either generator version: the refresh needs
    /// a process restart, and the oracle must not observe a stale or a new marker.
    /// </summary>
    public static void AssertRestartRequired(HostResponse loadOrInspect, HostResponse? oracle = null)
    {
        var execution = loadOrInspect.Execution ?? oracle?.Execution;
        Assert.NotNull(execution);
        Assert.True(
            string.Equals(execution!.Status, "RestartRequired", StringComparison.Ordinal)
            || string.Equals(execution.Status, "IdentityCollision", StringComparison.Ordinal),
            "Expected restart-required identity gate, got status="
            + execution.Status
            + " reason="
            + execution.Reason);
        Assert.Contains("restart", execution.Action ?? execution.Reason ?? "", StringComparison.OrdinalIgnoreCase);
        if (oracle is not null)
        {
            Assert.False(oracle.OracleSuccess, "Unsupported refresh must not execute V1 or V2. failure=" + oracle.OracleFailure);
            Assert.True(
                string.IsNullOrEmpty(oracle.Marker),
                "Stale or new marker must be absent. marker=" + oracle.Marker);
            Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, oracle.Marker);
            Assert.NotEqual(GeneratorConsumerFixture.MarkerV2, oracle.Marker);
        }
    }

    public static void AssertProjectFilesUnchanged(GeneratorConsumerFixture fixture, HostResponse snapshot)
    {
        Assert.NotNull(snapshot.CsprojSha256);
        Assert.True(
            GeneratorConsumerFixture.ProjectBytesUnchanged(fixture.ProjectFileBytes, snapshot.CsprojSha256!),
            "Project file bytes changed; overlay leaked into .csproj. includes="
            + string.Join("; ", snapshot.TemporaryAnalyzerIncludes ?? Array.Empty<string>()));
        Assert.True(
            snapshot.TemporaryAnalyzerIncludes is null || snapshot.TemporaryAnalyzerIncludes.Length == 0,
            "Temporary <Analyzer Include> present: " + string.Join("; ", snapshot.TemporaryAnalyzerIncludes ?? Array.Empty<string>()));
    }

    /// <summary>
    /// Changes the generated code, rebuilds without incrementality, and asserts the published build
    /// output really changed, so a stale DLL cannot pass a later marker check by accident.
    /// </summary>
    public static async Task<(string Dll, string Before, string After)> AssertForcedGeneratorRebuildWritesBytesAsync(
        LifecycleHostClient host,
        GeneratorConsumerFixture fixture,
        CancellationToken cancellationToken = default)
    {
        var dll = fixture.FindGeneratorOutputDll();
        Assert.True(dll is not null, "generator output missing under " + fixture.Root);
        var before = await host.SendAsync(new HostCommand { Op = "hashFile", Path = dll }, cancellationToken)
            .ConfigureAwait(false);
        Assert.False(string.IsNullOrWhiteSpace(before.FileSha256));

        var token = fixture.ForceGeneratorOutputChange();
        await BuildAsync(host, fixture.GeneratorProjectPath, noIncremental: true, cancellationToken).ConfigureAwait(false);

        // The redirect can move the output between builds, so re-resolve the path instead of reusing
        // the one hashed before the rebuild.
        var rebuilt = fixture.FindGeneratorOutputDll();
        Assert.True(
            !string.IsNullOrWhiteSpace(rebuilt) && File.Exists(rebuilt),
            "generator output missing after forced rebuild under " + fixture.Root);
        var after = await host.SendAsync(new HostCommand { Op = "hashFile", Path = rebuilt }, cancellationToken)
            .ConfigureAwait(false);
        Assert.False(
            string.IsNullOrWhiteSpace(after.FileSha256),
            "hashFile failed after forced rebuild: " + after.Error + " path=" + rebuilt);
        Assert.NotEqual(before.FileSha256, after.FileSha256);
        Assert.Contains(token, File.ReadAllText(fixture.GeneratorSourcePath), StringComparison.Ordinal);
        return (rebuilt!, before.FileSha256!, after.FileSha256!);
    }
}
