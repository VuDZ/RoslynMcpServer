using RoslynMcpServer.LifecycleTestHost;
using RoslynMcpServer.Services;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
[Trait("Category", "E5S4Acceptance")]
public sealed class Epoch5S4AcceptanceTests
{
    private readonly ITestOutputHelper _output;

    public Epoch5S4AcceptanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AnalyzerLifecycleFact]
    public async Task Redirected_missing_path_rewrites_and_executes_exact_V1()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("missing-path-load", load);
        Assert.True(load.Ok, load.Error);
        Assert.Equal("Complete", load.ProvenanceCaptureStatus);
        Assert.True(load.ProvenanceConfirmedBindingCount > 0);

        var rewrite = Assert.Single(load.Rewrite ?? []);
        Assert.True(rewrite.Applied, rewrite.SkipReason);
        Assert.Equal(AnalyzerReferenceReasonCodes.ReferenceRewritten, rewrite.ReasonCode);
        Assert.Equal(nameof(AnalyzerReferencePathState.Missing), rewrite.OriginalPathState);
        Assert.Equal(nameof(AnalyzerReferencePathState.Exists), rewrite.SelectedSourcePathState);
        Assert.Equal(nameof(AnalyzerReferenceSelectionBasis.LoadSessionProvenanceExactOutput), rewrite.SelectionBasis);
        Assert.False(string.IsNullOrWhiteSpace(rewrite.SelectedSourcePath));
        Assert.True(File.Exists(rewrite.SelectedSourcePath));
        Assert.False(string.IsNullOrWhiteSpace(rewrite.Generation));

        var oracle = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        Dump("missing-path-oracle", oracle);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerForeign, oracle.Marker);
    }

    [AnalyzerLifecycleFact]
    public async Task Foreign_existing_same_name_keeps_path_and_executes_exact_FOREIGN()
    {
        using var fixture = GeneratorConsumerFixture.Create(
            OutputPathMode.SdkDefaultCorrectPath,
            foreignAnalyzer: true,
            includeAnalyzerProjectReference: false);
        await using var host = LifecycleHostClient.Start();

        var foreignProject = Path.Combine(fixture.Root, "ForeignGenerator", "ForeignGenerator.csproj");
        await Epoch1HostOps.BuildAsync(host, foreignProject);
        Assert.True(File.Exists(fixture.ForeignDllPath), "foreign Generator.dll must exist");
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        await Epoch1HostOps.BuildAsync(host, fixture.ConsumerProjectPath);

        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("foreign-existing-load", load);
        Assert.Equal("Complete", load.ProvenanceCaptureStatus);
        Assert.Equal(0, load.ProvenanceConfirmedBindingCount);
        Assert.NotEmpty(load.Rewrite ?? []);
        Assert.All(
            load.Rewrite ?? [],
            rewrite =>
            {
                Assert.False(rewrite.Applied);
                Assert.Equal(AnalyzerReferenceReasonCodes.ProvenanceUnconfirmed, rewrite.ReasonCode);
                Assert.NotEqual(AnalyzerReferenceReasonCodes.ProvenForeignPath, rewrite.ReasonCode);
                Assert.NotEqual(AnalyzerReferenceReasonCodes.ReferenceRewritten, rewrite.ReasonCode);
            });
        Assert.Contains(
            load.Rewrite ?? [],
            rewrite => PathsEqual(rewrite.OriginalFullPath, fixture.ForeignDllPath)
                && rewrite.OriginalPathState == nameof(AnalyzerReferencePathState.Exists));
        Assert.Contains(
            load.OverlayAnalyzerPaths ?? [],
            path => PathsEqual(path, fixture.ForeignDllPath));

        var oracle = await Epoch1HostOps.OracleAsync(host);
        Dump("foreign-existing-oracle", oracle);
        Assert.True(oracle.OracleSuccess, oracle.OracleFailure);
        Assert.Equal(GeneratorConsumerFixture.MarkerForeign, oracle.Marker);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, oracle.Marker);
    }

    [AnalyzerLifecycleFact]
    public async Task Foreign_missing_same_name_is_not_replaced_and_does_not_execute_V1()
    {
        using var fixture = GeneratorConsumerFixture.Create(
            OutputPathMode.RedirectedMissingAnalyzerPath,
            missingForeignPath: true);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        Assert.False(File.Exists(fixture.MissingForeignPath));

        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("foreign-missing-load", load);
        Assert.Equal("Complete", load.ProvenanceCaptureStatus);
        Assert.True(load.ProvenanceConfirmedBindingCount > 0);

        var rewrites = load.Rewrite ?? [];
        var rewritten = Assert.Single(
            rewrites,
            candidate => candidate.Applied
                && candidate.ReasonCode == AnalyzerReferenceReasonCodes.ReferenceRewritten);
        var foreign = Assert.Single(
            rewrites,
            candidate => !candidate.Applied
                && candidate.ReasonCode == AnalyzerReferenceReasonCodes.ProvenanceUnconfirmed);
        Assert.NotEqual(AnalyzerReferenceReasonCodes.ProvenForeignPath, foreign.ReasonCode);
        Assert.Equal(nameof(AnalyzerReferencePathState.Missing), foreign.OriginalPathState);
        Assert.True(
            PathsEqualFromProject(foreign.OriginalFullPath, fixture.ConsumerProjectPath, fixture.MissingForeignPath)
            || PathsEqual(foreign.OriginalFullPath, fixture.MissingForeignPath));
        Assert.Contains(
            load.OverlayAnalyzerPaths ?? [],
            path => PathsEqualFromProject(path, fixture.ConsumerProjectPath, fixture.MissingForeignPath));
        Assert.Contains(
            load.OverlayAnalyzerPaths ?? [],
            path => PathsEqual(path, rewritten.ShadowCopyPath));

        var oracle = await Epoch1HostOps.OracleAsync(host);
        Dump("foreign-missing-oracle", oracle);
        Assert.False(oracle.OracleSuccess);
        Assert.Equal("no-constant", oracle.OracleFailure);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, oracle.Marker);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerForeign, oracle.Marker);
    }

    [AnalyzerLifecycleFact]
    public async Task Missing_source_output_reports_source_output_missing()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();

        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("source-output-missing-load", load);
        Assert.True(load.Ok, load.Error);
        Assert.Equal("Complete", load.ProvenanceCaptureStatus);
        Assert.DoesNotContain(load.Rewrite ?? [], rewrite => rewrite.Applied);

        var missing = Assert.Single(
            load.Rewrite ?? [],
            rewrite => rewrite.ReasonCode == AnalyzerReferenceReasonCodes.SourceOutputMissing);
        Assert.Equal(nameof(AnalyzerReferencePathState.Missing), missing.SelectedSourcePathState);
        Assert.NotEqual(AnalyzerReferenceReasonCodes.ProvenanceUnconfirmed, missing.ReasonCode);
        Assert.NotEqual(AnalyzerReferenceReasonCodes.AccessFailure, missing.ReasonCode);

        var oracle = await Epoch1HostOps.OracleAsync(host);
        Dump("source-output-missing-oracle", oracle);
        Assert.False(oracle.OracleSuccess);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, oracle.Marker);
    }

    [AnalyzerLifecycleFact]
    public async Task Injected_access_failure_reports_access_failure_not_missing()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        _ = await host.SendAsync(new HostCommand { Op = "forceAccessFailure" });
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("access-failure-load", load);
        Assert.True(load.Ok, load.Error);
        Assert.Equal("Complete", load.ProvenanceCaptureStatus);

        var access = Assert.Single(load.Rewrite ?? []);
        Assert.False(access.Applied);
        Assert.Equal(AnalyzerReferenceReasonCodes.AccessFailure, access.ReasonCode);
        Assert.Equal(nameof(AnalyzerReferencePathState.AccessFailure), access.SelectedSourcePathState);
        Assert.NotEqual(AnalyzerReferenceReasonCodes.SourceOutputMissing, access.ReasonCode);
        Assert.NotEqual(AnalyzerReferenceReasonCodes.ProvenanceUnconfirmed, access.ReasonCode);

        var oracle = await Epoch1HostOps.OracleAsync(host);
        Dump("access-failure-oracle", oracle);
        Assert.False(oracle.OracleSuccess);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, oracle.Marker);
    }

    [AnalyzerLifecycleFact]
    public async Task Failed_capture_reports_provenance_unconfirmed_without_rewrite()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        _ = await host.SendAsync(new HostCommand
        {
            Op = "injectCaptureFailure",
            CaptureFailureMode = "Missing",
        });
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("unconfirmed-capture-load", load);
        Assert.True(load.Ok, load.Error);
        Assert.Equal("Failed", load.ProvenanceCaptureStatus);
        Assert.Equal(0, load.ProvenanceConfirmedBindingCount);
        Assert.Equal("Unavailable", load.PublicationAdmission);
        Assert.Equal(AnalyzerProvenanceCaptureGate.ReasonFailed, load.PublicationBanReason);
        Assert.False(load.PublishedSnapshotPresent);
        Assert.False(load.ShadowEnabled);
        Assert.Empty(load.Rewrite ?? []);

        var oracle = await Epoch1HostOps.OracleAsync(host);
        Dump("unconfirmed-capture-oracle", oracle);
        Assert.False(IsPublishedSemanticAvailable(oracle));
        Assert.Equal("no-solution", oracle.Error);
        Assert.False(oracle.OracleSuccess);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, oracle.Marker);
    }

    private static bool IsPublishedSemanticAvailable(HostResponse response) =>
        !string.Equals(response.Error, "no-solution", StringComparison.Ordinal)
        && (response.OracleSuccess
            || response.PublishedSnapshotPresent
            || !string.IsNullOrWhiteSpace(response.OverlayAnalyzerPath)
            || !string.IsNullOrWhiteSpace(response.LoadedAnalyzerPath)
            || !string.IsNullOrWhiteSpace(response.Marker));

    private static bool PathsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool PathsEqualFromProject(string? path, string projectPath, string? expected)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        var resolved = Path.IsPathRooted(path)
            ? path
            : Path.Combine(Path.GetDirectoryName(projectPath)!, path);
        return PathsEqual(resolved, expected);
    }

    private void Dump(string label, HostResponse response)
    {
        _output.WriteLine(
            "[{0}] ok={1} err={2} capture={3} confirmed={4} marker={5} oracleFail={6} rewrite={7}",
            label,
            response.Ok,
            response.Error,
            response.ProvenanceCaptureStatus,
            response.ProvenanceConfirmedBindingCount,
            response.Marker,
            response.OracleFailure,
            string.Join(
                " | ",
                (response.Rewrite ?? []).Select(rewrite =>
                    rewrite.ProjectName
                    + " applied="
                    + rewrite.Applied
                    + " reason="
                    + rewrite.ReasonCode
                    + " originalState="
                    + rewrite.OriginalPathState
                    + " sourceState="
                    + rewrite.SelectedSourcePathState
                    + " basis="
                    + rewrite.SelectionBasis)));
    }
}
