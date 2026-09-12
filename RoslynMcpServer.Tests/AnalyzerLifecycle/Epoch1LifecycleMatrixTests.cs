using RoslynMcpServer.LifecycleTestHost;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
public sealed class Epoch1LifecycleMatrixTests
{
    private readonly ITestOutputHelper _output;

    public Epoch1LifecycleMatrixTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AnalyzerLifecycleFact]
    public async Task Basic_generation_opt_in_load_exact_V1_and_repeat_without_changes()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();

        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var csprojBefore = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, csprojBefore);

        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(load.Ok, load.Error);
        Assert.False(load.CacheHit);
        Assert.True(load.ReopenedGraph);
        Assert.True(load.PrepareAttempted);
        Assert.True(load.ShadowEnabled);
        Assert.Equal("Complete", load.ProvenanceCaptureStatus);
        Assert.True(load.ProvenanceConfirmedBindingCount > 0);
        var rewrite = Assert.Single(load.Rewrite ?? new List<RewriteDto>());
        Assert.True(rewrite.Applied, "prepare failed: " + rewrite.SkipReason);
        Assert.False(string.IsNullOrWhiteSpace(rewrite.ShadowCopyPath));
        Assert.False(string.IsNullOrWhiteSpace(rewrite.Generation));

        var first = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        Dump("basic-first", first);
        Assert.False(string.IsNullOrWhiteSpace(first.OverlayAnalyzerPath));
        Assert.StartsWith(
            Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            first.OverlayAnalyzerPath!,
            StringComparison.OrdinalIgnoreCase);

        var repeat = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        Dump("basic-repeat", repeat);
        Assert.Equal(first.Marker, repeat.Marker);

        var csprojAfter = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, csprojAfter);
    }

    [AnalyzerLifecycleFact]
    public async Task Negative_control_unavailable_generator_fails_oracle()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();

        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.True(load.Ok, load.Error);
        Assert.False(load.ShadowEnabled);

        var oracle = await Epoch1HostOps.OracleAsync(host);
        Dump("negative-control", oracle);
        Assert.False(oracle.OracleSuccess);
        Assert.Equal("no-type", oracle.OracleFailure);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, oracle.Marker);
    }

    [AnalyzerLifecycleFact]
    public async Task V1_to_V2_cached_load_records_graph_refresh_generation_identity_and_exact_marker()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();

        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var v1Load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.False(v1Load.CacheHit);
        var v1 = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var v1Generation = v1.Rewrite?.FirstOrDefault()?.Generation;

        fixture.SetGeneratorMarker(GeneratorConsumerFixture.MarkerV2);
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var cached = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("v1v2-cached-load", cached);
        Assert.True(cached.CacheHit, "cached load must reuse the graph (not a reopen).");
        Assert.False(cached.ReopenedGraph);
        Assert.True(cached.PrepareAttempted, "cached load with opt-in must attempt artifact refresh.");

        var oracle = await Epoch1HostOps.OracleAsync(host);
        Dump("v1v2-cached-oracle", oracle);
        Epoch1HostOps.AssertRestartRequired(cached, oracle);
        Assert.False(string.IsNullOrWhiteSpace(cached.Rewrite?.FirstOrDefault()?.Generation));
        Assert.NotEqual(v1Generation, cached.Rewrite?.FirstOrDefault()?.Generation);
    }

    [AnalyzerLifecycleFact]
    public async Task V1_to_V2_reset_plus_load_same_process_exact_marker()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();

        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        _ = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);

        fixture.SetGeneratorMarker(GeneratorConsumerFixture.MarkerV2);
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var reset = await host.SendAsync(new HostCommand { Op = "reset" });
        Assert.True(reset.Ok, reset.Error);
        Assert.False(reset.ShadowEnabled);

        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("v1v2-reset-load", load);
        Assert.False(load.CacheHit);
        Assert.True(load.ReopenedGraph);
        Assert.True(load.PrepareAttempted);
        var oracle = await Epoch1HostOps.OracleAsync(host);
        Dump("v1v2-reset-oracle", oracle);
        Epoch1HostOps.AssertRestartRequired(load, oracle);
    }

    [AnalyzerLifecycleFact]
    public async Task V1_to_V2_process_restart_exact_marker()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);

        await using (var host1 = LifecycleHostClient.Start())
        {
            await Epoch1HostOps.BuildAsync(host1, fixture.SolutionPath);
            _ = await Epoch1HostOps.LoadAsync(host1, fixture.SolutionPath, shadowCopy: true);
            await Epoch1HostOps.RequireMarkerAsync(host1, GeneratorConsumerFixture.MarkerV1);
        }

        fixture.SetGeneratorMarker(GeneratorConsumerFixture.MarkerV2);
        await using var host2 = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host2, fixture.SolutionPath);
        var load = await Epoch1HostOps.LoadAsync(host2, fixture.SolutionPath, shadowCopy: true);
        Dump("v1v2-restart-load", load);
        Assert.False(load.CacheHit);
        Assert.True(load.ReopenedGraph);
        var oracle = await Epoch1HostOps.RequireMarkerAsync(host2, GeneratorConsumerFixture.MarkerV2);
        Dump("v1v2-restart-oracle", oracle);
        Assert.False(string.IsNullOrWhiteSpace(oracle.AssemblyIdentity));
        Assert.False(string.IsNullOrWhiteSpace(oracle.LoadedAnalyzerPath ?? oracle.OverlayAnalyzerPath));
    }

    [AnalyzerLifecycleFact]
    public async Task Flag_true_to_false_and_omitted_is_sticky_until_reset()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        _ = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);

        var toFalse = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Dump("flag-true-false", toFalse);
        Assert.True(toFalse.CacheHit);
        Assert.True(toFalse.ShadowEnabled, "v1.3.5 sticky: cached false does not disable overlay (U-ARB-05).");
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);

        var omitted = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: null);
        Dump("flag-true-omitted", omitted);
        Assert.True(omitted.CacheHit);
        Assert.True(omitted.ShadowEnabled, "v1.3.5 sticky: omitted flag does not disable overlay (U-ARB-05).");
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);

        _ = await host.SendAsync(new HostCommand { Op = "reset" });
        var afterResetFalse = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.False(afterResetFalse.ShadowEnabled);
        var offOracle = await Epoch1HostOps.OracleAsync(host);
        Assert.False(offOracle.OracleSuccess);
        Assert.Equal("no-type", offOracle.OracleFailure);

        _ = await host.SendAsync(new HostCommand { Op = "reset" });
        var afterResetOmitted = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: null);
        Assert.False(afterResetOmitted.ShadowEnabled);
    }

    [AnalyzerLifecycleFact]
    public async Task Flag_false_to_true_enables_on_cached_load_and_after_reset()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var off = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: false);
        Assert.False(off.ShadowEnabled);
        var offOracle = await Epoch1HostOps.OracleAsync(host);
        Assert.False(offOracle.OracleSuccess);

        var on = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("flag-false-true", on);
        Assert.True(on.CacheHit);
        Assert.True(on.PrepareAttempted);
        Assert.True(on.ShadowEnabled);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);

        _ = await host.SendAsync(new HostCommand { Op = "reset" });
        var resetOn = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.False(resetOn.CacheHit);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
    }

    [AnalyzerLifecycleFact]
    public async Task Solution_A_opt_in_then_B_same_identity_overlay_off_uses_B_and_not_A()
    {
        using var solutionA = GeneratorConsumerFixture.Create(
            OutputPathMode.RedirectedMissingAnalyzerPath,
            marker: GeneratorConsumerFixture.MarkerA);
        using var solutionB = GeneratorConsumerFixture.Create(
            OutputPathMode.SdkDefaultCorrectPath,
            marker: GeneratorConsumerFixture.MarkerB);
        await using var host = LifecycleHostClient.Start();

        await Epoch1HostOps.BuildAsync(host, solutionA.SolutionPath);
        _ = await Epoch1HostOps.LoadAsync(host, solutionA.SolutionPath, shadowCopy: true);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerA);

        await Epoch1HostOps.BuildAsync(host, solutionB.SolutionPath);
        var loadB = await Epoch1HostOps.LoadAsync(host, solutionB.SolutionPath, shadowCopy: false);
        Dump("solution-B-off", loadB);
        Assert.False(loadB.CacheHit);
        Assert.False(loadB.ShadowEnabled);

        var oracleB = await Epoch1HostOps.OracleAsync(host);
        Dump("solution-B-oracle", oracleB);
        Epoch1HostOps.AssertRestartRequired(loadB, oracleB);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerA, oracleB.Marker);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerB, oracleB.Marker);
        Assert.False(oracleB.OracleSuccess);
    }

    [AnalyzerLifecycleFact]
    public async Task Solution_B_broken_path_flag_off_has_no_generation()
    {
        using var solutionA = GeneratorConsumerFixture.Create(
            OutputPathMode.RedirectedMissingAnalyzerPath,
            marker: GeneratorConsumerFixture.MarkerA);
        using var solutionB = GeneratorConsumerFixture.Create(
            OutputPathMode.RedirectedMissingAnalyzerPath,
            marker: GeneratorConsumerFixture.MarkerB);
        await using var host = LifecycleHostClient.Start();

        await Epoch1HostOps.BuildAsync(host, solutionA.SolutionPath);
        _ = await Epoch1HostOps.LoadAsync(host, solutionA.SolutionPath, shadowCopy: true);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerA);

        await Epoch1HostOps.BuildAsync(host, solutionB.SolutionPath);
        _ = await Epoch1HostOps.LoadAsync(host, solutionB.SolutionPath, shadowCopy: false);
        var oracle = await Epoch1HostOps.OracleAsync(host);
        Dump("solution-B-broken-off", oracle);
        Assert.False(oracle.OracleSuccess);
        Assert.Equal("no-type", oracle.OracleFailure);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerA, oracle.Marker);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerB, oracle.Marker);
    }

    [AnalyzerLifecycleFact]
    public async Task No_output_first_enable_fails_prepare_then_build_and_reload_executes()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();

        var beforeBuild = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("no-output-prepare", beforeBuild);
        Assert.True(beforeBuild.PrepareAttempted);
        Assert.False(beforeBuild.ShadowEnabled);
        Assert.Contains(beforeBuild.Rewrite ?? new List<RewriteDto>(), r => !r.Applied);
        var failedOracle = await Epoch1HostOps.OracleAsync(host);
        Assert.False(failedOracle.OracleSuccess);

        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        _ = await host.SendAsync(new HostCommand { Op = "reset" });
        var after = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("no-output-after-build", after);
        Assert.True(after.ShadowEnabled);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
    }

    [AnalyzerLifecycleFact]
    public async Task Multiple_consumers_share_generator_and_all_see_exact_marker()
    {
        using var fixture = GeneratorConsumerFixture.Create(
            OutputPathMode.RedirectedMissingAnalyzerPath,
            extraConsumers: 2);
        await using var host = LifecycleHostClient.Start();

        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("multi-consumer-load", load);
        Assert.True((load.Rewrite ?? new List<RewriteDto>()).Count >= 3, "each Consumer must be prepared");

        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1, "Consumer");
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1, "Consumer1");
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1, "Consumer2");

        var again = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(again.CacheHit);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1, "Consumer1");
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1, "Consumer2");
    }

    [AnalyzerLifecycleFact]
    public async Task Foreign_existing_same_filename_is_preserved_and_executes_its_own_marker()
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
        Assert.Equal("Complete", load.ProvenanceCaptureStatus);
        Assert.Equal(0, load.ProvenanceConfirmedBindingCount);
        Assert.NotEmpty(load.Rewrite ?? []);
        Assert.All(load.Rewrite ?? [], rewrite => Assert.False(rewrite.Applied));
        Assert.Contains(
            load.OverlayAnalyzerPaths ?? [],
            path => PathsEqual(path, fixture.ForeignDllPath));

        var oracle = await Epoch1HostOps.OracleAsync(host);
        Dump("foreign-existing", oracle);
        Assert.True(oracle.OracleSuccess, oracle.OracleFailure);
        Assert.Equal(GeneratorConsumerFixture.MarkerForeign, oracle.Marker);
    }

    [AnalyzerLifecycleFact]
    public async Task Foreign_missing_path_same_filename_is_not_replaced_by_in_solution_candidate()
    {
        using var fixture = GeneratorConsumerFixture.Create(
            OutputPathMode.RedirectedMissingAnalyzerPath,
            missingForeignPath: true);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.GeneratorProjectPath);
        Assert.False(File.Exists(fixture.MissingForeignPath));

        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.Equal("Complete", load.ProvenanceCaptureStatus);
        Assert.True(load.ProvenanceConfirmedBindingCount > 0);
        var rewrites = load.Rewrite ?? [];
        var rewrite = Assert.Single(rewrites, candidate => candidate.Applied);
        Assert.Contains(rewrites, candidate => !candidate.Applied);
        Assert.True(rewrite.Applied, rewrite.SkipReason);
        Assert.Contains(
            load.OverlayAnalyzerPaths ?? [],
            path => PathsEqualFromProject(path, fixture.ConsumerProjectPath, fixture.MissingForeignPath));
        Assert.Contains(
            load.OverlayAnalyzerPaths ?? [],
            path => PathsEqual(path, rewrite.ShadowCopyPath));

        var oracle = await Epoch1HostOps.OracleAsync(host);
        Dump("foreign-missing", oracle);
        Assert.False(oracle.OracleSuccess);
        Assert.Equal("no-constant", oracle.OracleFailure);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, oracle.Marker);
    }

    [AnalyzerLifecycleFact]
    public async Task Overlay_reapply_injected_prepare_failure_must_not_drop_active_shadow_or_marker()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var before = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var shadowBefore = before.OverlayAnalyzerPath;
        Assert.False(string.IsNullOrWhiteSpace(shadowBefore));
        var prepareCount = load.OverlayPrepareCount;
        var fileIo = load.AnalyzerFileIoCount;

        _ = await host.SendAsync(new HostCommand { Op = "injectPrepareFailure" });
        var edited = fixture.WithConsumerComment("after-prepare-failure");
        var update = await host.SendAsync(
            new HostCommand { Op = "updateDocument", Path = fixture.ConsumerSourcePath, Text = edited });
        Dump("reapply-failure-update", update);
        Assert.True(update.Ok, update.Error);
        Assert.Equal(prepareCount, update.OverlayPrepareCount);
        Assert.Equal(fileIo, update.AnalyzerFileIoCount);
        Assert.False(update.PrepareInjectedFailure, "edit must not consume prepare-failure; it is not a refresh.");

        var after = await Epoch1HostOps.OracleAsync(host);
        Dump("reapply-failure-oracle", after);
        Assert.True(after.OracleSuccess, after.OracleFailure);
        Assert.Equal(GeneratorConsumerFixture.MarkerV1, after.Marker);
        Assert.False(string.IsNullOrWhiteSpace(after.OverlayAnalyzerPath));
        Assert.Equal(shadowBefore, after.OverlayAnalyzerPath);
        Assert.False(
            string.Equals(after.OverlayAnalyzerPath, after.WorkspaceAnalyzerPath, StringComparison.OrdinalIgnoreCase),
            "Active overlay refs must not be silently replaced with broken original references.");
    }

    [AnalyzerLifecycleFact]
    public async Task Overlay_reapply_forced_copy_ioexception_must_not_drop_active_shadow_or_marker()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var before = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var shadowBefore = before.OverlayAnalyzerPath;
        Assert.False(string.IsNullOrWhiteSpace(shadowBefore));
        var prepareCount = load.OverlayPrepareCount;
        var fileIo = load.AnalyzerFileIoCount;

        _ = await host.SendAsync(new HostCommand { Op = "forceCopyFailure" });
        var edited = fixture.WithConsumerComment("after-copy-ioexception");
        var update = await host.SendAsync(
            new HostCommand { Op = "updateDocument", Path = fixture.ConsumerSourcePath, Text = edited });
        Dump("reapply-copy-fail-update", update);
        Assert.True(update.Ok, update.Error);
        Assert.Equal(prepareCount, update.OverlayPrepareCount);
        Assert.Equal(fileIo, update.AnalyzerFileIoCount);
        Assert.DoesNotContain(
            update.Rewrite ?? new List<RewriteDto>(),
            r => !r.Applied && (r.SkipReason ?? "").Contains("shadow copy failed", StringComparison.OrdinalIgnoreCase));

        var after = await Epoch1HostOps.OracleAsync(host);
        Dump("reapply-copy-fail-oracle", after);
        Assert.True(after.OracleSuccess, after.OracleFailure);
        Assert.Equal(GeneratorConsumerFixture.MarkerV1, after.Marker);
        Assert.False(string.IsNullOrWhiteSpace(after.OverlayAnalyzerPath));
        Assert.Equal(shadowBefore, after.OverlayAnalyzerPath);
        Assert.False(
            string.Equals(after.OverlayAnalyzerPath, after.WorkspaceAnalyzerPath, StringComparison.OrdinalIgnoreCase),
            "File.Copy IOException must not silently replace active overlay refs with broken original references.");
    }

    [AnalyzerLifecycleFact]
    public async Task Concurrent_load_enable_stays_on_one_session_and_oracle_is_definite()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);

        var concurrent = await host.SendAsync(
            new HostCommand
            {
                Op = "concurrentLoad",
                Path = fixture.SolutionPath,
                ShadowCopy = true,
                Project = "Consumer",
            });
        Dump("concurrent-load", concurrent);
        Assert.NotNull(concurrent.Concurrency);
        Assert.True(concurrent.Concurrency!.BothCompleted, concurrent.Concurrency.FirstError + " / " + concurrent.Concurrency.SecondError);
        Assert.True(concurrent.Concurrency.ShadowEnabledAfter);
        Assert.Equal(GeneratorConsumerFixture.MarkerV1, concurrent.Concurrency.MarkerAfter);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
    }

    private static void AssertOracleDefinite(HostResponse oracle)
    {
        if (oracle.OracleSuccess)
        {
            Assert.False(string.IsNullOrWhiteSpace(oracle.Marker), "definite oracle success requires a marker");
            return;
        }

        Assert.False(
            string.IsNullOrWhiteSpace(oracle.OracleFailure),
            "definite oracle failure requires a reason; empty diagnostics are not an oracle");
    }

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
            "[{0}] ok={1} err={2} cacheHit={3} reopen={4} prepare={5} gen={6} marker={7} oracleFail={8} overlay={9} loaded={10} identity={11} OverlayPrepareCount={12} AnalyzerFileIoCount={13}",
            label,
            response.Ok,
            response.Error,
            response.CacheHit,
            response.ReopenedGraph,
            response.PrepareAttempted,
            response.Rewrite?.FirstOrDefault()?.Generation,
            response.Marker,
            response.OracleFailure,
            response.OverlayAnalyzerPath,
            response.LoadedAnalyzerPath,
            response.AssemblyIdentity,
            response.OverlayPrepareCount,
            response.AnalyzerFileIoCount);
    }
}
