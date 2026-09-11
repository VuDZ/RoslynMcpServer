using RoslynMcpServer.LifecycleTestHost;
using Xunit;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

internal static class Epoch1HostOps
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
        CancellationToken cancellationToken = default)
    {
        return await host.SendAsync(
            new HostCommand
            {
                Op = "oracle",
                Project = project,
                OracleSource = oracleSource,
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

    public static async Task<(string Dll, string Before, string After)> AssertForcedGeneratorRebuildWritesBytesAsync(
        LifecycleHostClient host,
        GeneratorConsumerFixture fixture,
        CancellationToken cancellationToken = default)
    {
        var dll = fixture.FindGeneratorOutputDll();
        Assert.NotNull(dll);
        var before = await host.SendAsync(new HostCommand { Op = "hashFile", Path = dll }, cancellationToken)
            .ConfigureAwait(false);
        Assert.False(string.IsNullOrWhiteSpace(before.FileSha256));

        File.AppendAllText(fixture.GeneratorSourcePath, Environment.NewLine + "// rebuild-bump " + Guid.NewGuid().ToString("N"));
        await BuildAsync(host, fixture.GeneratorProjectPath, noIncremental: true, cancellationToken).ConfigureAwait(false);

        var after = await host.SendAsync(new HostCommand { Op = "hashFile", Path = dll }, cancellationToken)
            .ConfigureAwait(false);
        Assert.False(string.IsNullOrWhiteSpace(after.FileSha256));
        Assert.NotEqual(before.FileSha256, after.FileSha256);
        return (dll!, before.FileSha256!, after.FileSha256!);
    }
}
