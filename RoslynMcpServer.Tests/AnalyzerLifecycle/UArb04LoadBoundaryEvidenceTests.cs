using RoslynMcpServer.LifecycleTestHost;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
public sealed class UArb04LoadBoundaryEvidenceTests(ITestOutputHelper output)
{
    [AnalyzerLifecycleTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_correct_path_physical_load_and_prepare_are_lazy_and_do_not_lock_output(
        bool enableOverlay)
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var realOutput = RequireGeneratorOutput(fixture);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, enableOverlay);

        Assert.True(load.Ok, load.Error);
        Assert.Empty(load.ProcessAnalyzerAssemblies ?? []);
        AssertPathEqual(realOutput, load.WorkspaceAnalyzerPath);
        if (enableOverlay)
        {
            Assert.False(PathsEqual(realOutput, load.OverlayAnalyzerPath));
        }
        else
        {
            AssertPathEqual(realOutput, load.OverlayAnalyzerPath);
        }

        var rebuild = await Epoch1HostOps.AssertForcedGeneratorRebuildWritesBytesAsync(host, fixture);
        output.WriteLine(
            "load-only overlay={0} real={1} workspace={2} overlayPath={3} hash={4}->{5}",
            enableOverlay,
            realOutput,
            load.WorkspaceAnalyzerPath,
            load.OverlayAnalyzerPath,
            rebuild.Before,
            rebuild.After);
    }

    [AnalyzerLifecycleFact]
    public async Task Existing_correct_path_published_semantic_before_enable_loads_real_output_and_measures_rebuild_lock()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var realOutput = RequireGeneratorOutput(fixture);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.Empty(load.ProcessAnalyzerAssemblies ?? []);

        var publishedOracle = await Epoch1HostOps.OracleAsync(host);
        Assert.True(publishedOracle.OracleSuccess, publishedOracle.OracleFailure);
        Assert.Equal(GeneratorConsumerFixture.MarkerV1, publishedOracle.Marker);
        AssertPathEqual(realOutput, publishedOracle.OverlayAnalyzerPath);
        AssertPathEqual(realOutput, publishedOracle.WorkspaceAnalyzerPath);
        AssertPathPresent(realOutput, publishedOracle.ProcessAnalyzerAssemblies);
        AssertPathEqual(realOutput, publishedOracle.LoadedAnalyzerPath);

        var enable = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(enable.CacheHit);
        Assert.True(enable.PrepareAttempted);
        Assert.False(enable.ShadowEnabled);
        Epoch1HostOps.AssertRestartRequired(enable);
        AssertPathEqual(realOutput, enable.Execution?.LoadedPath);

        var rebuild = await AttemptForcedGeneratorRebuildAsync(host, fixture);
        if (OperatingSystem.IsWindows())
        {
            Assert.False(rebuild.Ok);
            Assert.NotEqual(0, rebuild.BuildExitCode);
            Assert.Contains("MSB3021", rebuild.BuildOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(rebuild.Before, rebuild.After);
        }
        else
        {
            Assert.True(rebuild.Ok, rebuild.BuildOutput);
            Assert.Equal(0, rebuild.BuildExitCode);
            Assert.NotEqual(rebuild.Before, rebuild.After);
        }

        output.WriteLine(
            "published-no-overlay-semantic real={0} loaded={1} process={2} exit={3} hash={4}->{5}",
            realOutput,
            publishedOracle.LoadedAnalyzerPath,
            FormatPaths(publishedOracle.ProcessAnalyzerAssemblies),
            rebuild.BuildExitCode,
            rebuild.Before,
            rebuild.After);
    }

    [AnalyzerLifecycleFact]
    public async Task Existing_correct_path_overlay_semantic_loads_shadow_and_later_raw_reader_reuses_shadow()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var realOutput = RequireGeneratorOutput(fixture);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.Empty(load.ProcessAnalyzerAssemblies ?? []);
        AssertPathEqual(realOutput, load.WorkspaceAnalyzerPath);
        Assert.False(PathsEqual(realOutput, load.OverlayAnalyzerPath));

        var overlayOracle = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        AssertPathEqual(load.OverlayAnalyzerPath, overlayOracle.LoadedAnalyzerPath);
        AssertPathPresent(load.OverlayAnalyzerPath!, overlayOracle.ProcessAnalyzerAssemblies);
        AssertPathAbsent(realOutput, overlayOracle.ProcessAnalyzerAssemblies);

        var rawOracle = await Epoch1HostOps.OracleAsync(host, oracleSource: "workspace");
        Assert.True(rawOracle.OracleSuccess, rawOracle.OracleFailure);
        Assert.Equal(GeneratorConsumerFixture.MarkerV1, rawOracle.Marker);
        AssertPathPresent(load.OverlayAnalyzerPath!, rawOracle.ProcessAnalyzerAssemblies);
        AssertPathAbsent(realOutput, rawOracle.ProcessAnalyzerAssemblies);

        var rebuild = await Epoch1HostOps.AssertForcedGeneratorRebuildWritesBytesAsync(host, fixture);
        output.WriteLine(
            "overlay-then-raw real={0} shadow={1} process={2} hash={3}->{4}",
            realOutput,
            load.OverlayAnalyzerPath,
            FormatPaths(rawOracle.ProcessAnalyzerAssemblies),
            rebuild.Before,
            rebuild.After);
    }

    [AnalyzerLifecycleFact]
    public async Task Opt_in_load_entered_first_blocks_semantic_until_shadow_snapshot_is_published()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var realOutput = RequireGeneratorOutput(fixture);
        var concurrent = await host.SendAsync(new HostCommand
        {
            Op = "atomicLoadSemantic",
            Path = fixture.SolutionPath,
            ShadowCopy = true,
            Project = "Consumer",
        });

        Assert.True(concurrent.Ok, concurrent.Error);
        Assert.NotNull(concurrent.Concurrency);
        Assert.True(concurrent.Concurrency!.BothCompleted);
        Assert.False(concurrent.Concurrency.SemanticCompletedBeforePrepare);
        Assert.True(concurrent.Concurrency.PublishedSnapshotPresent);
        Assert.True(concurrent.ShadowEnabled);
        Assert.True(concurrent.OracleSuccess, concurrent.OracleFailure);
        Assert.Equal(GeneratorConsumerFixture.MarkerV1, concurrent.Marker);
        Assert.False(PathsEqual(realOutput, concurrent.OverlayAnalyzerPath));
        AssertPathEqual(concurrent.OverlayAnalyzerPath, concurrent.LoadedAnalyzerPath);
        AssertPathPresent(concurrent.OverlayAnalyzerPath!, concurrent.ProcessAnalyzerAssemblies);
        AssertPathAbsent(realOutput, concurrent.ProcessAnalyzerAssemblies);

        var projectSnapshot = await host.SendAsync(
            new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, projectSnapshot);
        _ = await Epoch1HostOps.AssertForcedGeneratorRebuildWritesBytesAsync(host, fixture);
    }

    [AnalyzerLifecycleFact]
    public async Task New_session_prepare_failure_publishes_only_fail_closed_snapshot()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var realOutput = RequireGeneratorOutput(fixture);
        _ = await host.SendAsync(new HostCommand { Op = "injectPrepareFailure" });
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);

        Assert.True(load.Ok, load.Error);
        Assert.True(load.PrepareAttempted);
        Assert.True(load.PrepareInjectedFailure);
        Assert.False(load.ShadowEnabled);
        Assert.Equal("LoadFailed", load.Execution?.Status);
        Assert.Null(load.OverlayAnalyzerPath);
        AssertPathAbsent(realOutput, load.ProcessAnalyzerAssemblies);

        var semantic = await Epoch1HostOps.OracleAsync(host);
        Assert.False(semantic.OracleSuccess);
        Assert.Null(semantic.OverlayAnalyzerPath);
        AssertPathAbsent(realOutput, semantic.ProcessAnalyzerAssemblies);
    }

    [AnalyzerLifecycleFact]
    public async Task Cancellation_after_physical_load_does_not_prepare_or_publish_raw_snapshot()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var realOutput = RequireGeneratorOutput(fixture);
        var cancelled = await host.SendAsync(new HostCommand
        {
            Op = "atomicLoadSemantic",
            Path = fixture.SolutionPath,
            Arguments = "cancel",
        });

        Assert.True(cancelled.Ok, cancelled.Error);
        Assert.NotNull(cancelled.Concurrency);
        Assert.True(cancelled.Concurrency!.LoadCancelled);
        Assert.True(cancelled.Concurrency.PublishedSnapshotPresent);
        Assert.False(cancelled.PrepareAttempted);
        Assert.False(cancelled.ShadowEnabled);
        Assert.Null(cancelled.OverlayAnalyzerPath);
        AssertPathAbsent(realOutput, cancelled.ProcessAnalyzerAssemblies);
    }

    [AnalyzerLifecycleFact]
    public async Task Zero_project_blocking_load_does_not_start_prepare_or_publish_semantic_snapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpServer.UArb04", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var solutionPath = Path.Combine(root, "Empty.sln");
            await File.WriteAllTextAsync(
                solutionPath,
                """
                Microsoft Visual Studio Solution File, Format Version 12.00
                # Visual Studio Version 17
                VisualStudioVersion = 17.0.31903.59
                MinimumVisualStudioVersion = 10.0.40219.1
                Global
                EndGlobal
                """);

            await using var host = LifecycleHostClient.Start();
            var load = await Epoch1HostOps.LoadAsync(host, solutionPath, shadowCopy: true);
            Assert.True(load.Ok, load.Error);
            Assert.False(load.PrepareAttempted);
            Assert.False(load.ShadowEnabled);

            var semantic = await Epoch1HostOps.OracleAsync(host);
            Assert.False(semantic.Ok);
            Assert.Equal("no-solution", semantic.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AnalyzerLifecycleFact]
    public async Task Graph_stale_reopen_with_opt_in_prepares_before_publishing_new_session()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var first = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(first.ShadowEnabled);
        var firstSessionPrepareCount = first.OverlayPrepareCount;
        await File.AppendAllTextAsync(
            fixture.ConsumerProjectPath,
            Environment.NewLine + "<!-- u-arb-04 graph stale -->" + Environment.NewLine);

        HostResponse? reopened = null;
        var started = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(10))
        {
            reopened = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
            if (reopened.ReopenedGraph)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), TimeProvider.System);
        }

        Assert.NotNull(reopened);
        Assert.True(reopened!.Ok, reopened.Error);
        Assert.True(reopened.ReopenedGraph);
        Assert.True(reopened.PrepareAttempted);
        Assert.True(reopened.ShadowEnabled);
        Assert.True(reopened.OverlayPrepareCount > firstSessionPrepareCount);

        var realOutput = RequireGeneratorOutput(fixture);
        var semantic = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        Assert.False(PathsEqual(realOutput, semantic.OverlayAnalyzerPath));
        AssertPathAbsent(realOutput, semantic.ProcessAnalyzerAssemblies);
    }

    private static async Task<(bool Ok, int BuildExitCode, string BuildOutput, string Before, string After)>
        AttemptForcedGeneratorRebuildAsync(
            LifecycleHostClient host,
            GeneratorConsumerFixture fixture)
    {
        var dll = RequireGeneratorOutput(fixture);
        var before = await host.SendAsync(new HostCommand { Op = "hashFile", Path = dll });
        Assert.False(string.IsNullOrWhiteSpace(before.FileSha256));

        File.AppendAllText(
            fixture.GeneratorSourcePath,
            Environment.NewLine + "// locked-rebuild-bump " + Guid.NewGuid().ToString("N"));
        var build = await host.SendAsync(
            new HostCommand
            {
                Op = "build",
                Path = fixture.GeneratorProjectPath,
                NoIncremental = true,
                Arguments = "-p:CopyRetryCount=0 -p:CopyRetryDelayMilliseconds=10",
            });

        var after = await host.SendAsync(new HostCommand { Op = "hashFile", Path = dll });
        Assert.False(string.IsNullOrWhiteSpace(after.FileSha256));
        return (
            build.Ok,
            build.BuildExitCode,
            build.BuildOutput ?? build.Error ?? string.Empty,
            before.FileSha256!,
            after.FileSha256!);
    }

    private static string RequireGeneratorOutput(GeneratorConsumerFixture fixture)
    {
        var path = fixture.FindGeneratorOutputDll();
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(File.Exists(path));
        return Path.GetFullPath(path);
    }

    private static void AssertPathPresent(string expected, IReadOnlyCollection<LoadedAssemblyDto>? assemblies) =>
        Assert.Contains(assemblies ?? [], assembly => PathsEqual(expected, assembly.Location));

    private static void AssertPathAbsent(string expected, IReadOnlyCollection<LoadedAssemblyDto>? assemblies) =>
        Assert.DoesNotContain(assemblies ?? [], assembly => PathsEqual(expected, assembly.Location));

    private static void AssertPathEqual(string? expected, string? actual) =>
        Assert.True(
            PathsEqual(expected, actual),
            $"Expected path '{expected}', actual '{actual}'.");

    private static bool PathsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string FormatPaths(IReadOnlyCollection<LoadedAssemblyDto>? assemblies) =>
        string.Join("; ", (assemblies ?? []).Select(assembly => assembly.Location));
}
