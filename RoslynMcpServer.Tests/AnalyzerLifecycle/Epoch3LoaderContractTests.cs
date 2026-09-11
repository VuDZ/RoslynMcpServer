using RoslynMcpServer.LifecycleTestHost;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
public sealed class Epoch3LoaderContractTests
{
    private readonly ITestOutputHelper _output;

    public Epoch3LoaderContractTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AnalyzerLifecycleFact]
    public async Task V1_to_V2_cached_and_reset_refuse_execution_process_restart_runs_V2()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        _ = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var v1 = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        Assert.Equal("ExecutionObserved", v1.Execution?.Status);

        fixture.SetGeneratorMarker(GeneratorConsumerFixture.MarkerV2);
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var cached = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var cachedOracle = await Epoch1HostOps.OracleAsync(host);
        Dump("cached", cached);
        Dump("cached-oracle", cachedOracle);
        Epoch1HostOps.AssertRestartRequired(cached, cachedOracle);

        _ = await host.SendAsync(new HostCommand { Op = "reset" });
        var reset = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var resetOracle = await Epoch1HostOps.OracleAsync(host);
        Dump("reset", reset);
        Epoch1HostOps.AssertRestartRequired(reset, resetOracle);
    }

    [AnalyzerLifecycleFact]
    public async Task V1_to_V2_process_restart_executes_exact_V2()
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
        Assert.True(load.Rewrite?.Any(r => r.Applied) == true, load.Execution?.Reason);
        var oracle = await Epoch1HostOps.RequireMarkerAsync(host2, GeneratorConsumerFixture.MarkerV2);
        Dump("restart-v2", oracle);
        Assert.Equal("ExecutionObserved", oracle.Execution?.Status);
        Assert.False(string.IsNullOrWhiteSpace(oracle.LoadedAnalyzerPath ?? oracle.OverlayAnalyzerPath));
        await Epoch1HostOps.AssertForcedGeneratorRebuildWritesBytesAsync(host2, fixture);
    }

    [AnalyzerLifecycleFact]
    public async Task Solution_A_then_B_same_identity_is_refused_not_marker_A()
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
        var oracleB = await Epoch1HostOps.OracleAsync(host);
        Dump("A-then-B", loadB);
        Dump("A-then-B-oracle", oracleB);
        Epoch1HostOps.AssertRestartRequired(loadB, oracleB);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerA, oracleB.Marker);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerB, oracleB.Marker);
        Assert.False(oracleB.OracleSuccess);
    }

    [AnalyzerLifecycleFact]
    public async Task Private_helper_is_refused_and_helper_only_change_does_not_become_main_only_success()
    {
        using var fixture = GeneratorConsumerFixture.Create(
            OutputPathMode.RedirectedMissingAnalyzerPath,
            privateHelper: true,
            helperVersion: "1.0.0.0");
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("helper-load", load);
        Assert.Equal("DependencyUnsupported", load.Execution?.Status);
        Assert.Contains("Generator.Helpers", load.Execution?.Dependency ?? load.Execution?.Reason ?? "");
        Assert.DoesNotContain("restart", load.Execution?.Action ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("main-only", load.Execution?.Action ?? load.Execution?.Reason ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(load.Rewrite ?? new List<RewriteDto>(), r => r.Applied);
        var oracle = await Epoch1HostOps.OracleAsync(host);
        Assert.False(oracle.OracleSuccess);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, oracle.Marker);

        var helperBefore = fixture.FindHelperOutputDll();
        Assert.False(string.IsNullOrWhiteSpace(helperBefore));
        var beforeHash = await host.SendAsync(new HostCommand { Op = "hashFile", Path = helperBefore });
        fixture.SetHelperVersionComment("helper-only-" + Guid.NewGuid().ToString("N"));
        await Epoch1HostOps.BuildAsync(host, fixture.HelperProjectPath!);
        var helperAfter = fixture.FindHelperOutputDll() ?? helperBefore;
        var afterHash = await host.SendAsync(new HostCommand { Op = "hashFile", Path = helperAfter });
        Assert.NotEqual(beforeHash.FileSha256, afterHash.FileSha256);

        _ = await host.SendAsync(new HostCommand { Op = "reset" });
        var afterHelper = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("helper-only-refresh", afterHelper);
        Assert.Equal("DependencyUnsupported", afterHelper.Execution?.Status);
        Assert.DoesNotContain(afterHelper.Rewrite ?? new List<RewriteDto>(), r => r.Applied);
        var afterOracle = await Epoch1HostOps.OracleAsync(host);
        Assert.False(afterOracle.OracleSuccess);
    }

    [AnalyzerLifecycleFact]
    public async Task Two_generators_with_conflicting_helper_versions_are_refused()
    {
        using var first = GeneratorConsumerFixture.Create(
            OutputPathMode.RedirectedMissingAnalyzerPath,
            privateHelper: true,
            helperVersion: "1.0.0.0");
        using var second = GeneratorConsumerFixture.Create(
            OutputPathMode.RedirectedMissingAnalyzerPath,
            privateHelper: true,
            helperVersion: "2.0.0.0");
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, first.SolutionPath);
        var loadFirst = await Epoch1HostOps.LoadAsync(host, first.SolutionPath, shadowCopy: true);
        Assert.Equal("DependencyUnsupported", loadFirst.Execution?.Status);

        await Epoch1HostOps.BuildAsync(host, second.SolutionPath);
        var loadSecond = await Epoch1HostOps.LoadAsync(host, second.SolutionPath, shadowCopy: true);
        Dump("conflicting-helpers", loadSecond);
        Assert.Equal("DependencyUnsupported", loadSecond.Execution?.Status);
        Assert.Equal("DependencyUnsupported", loadFirst.Execution?.Status);
        Assert.DoesNotContain("restart", loadSecond.Execution?.Action ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.False((await Epoch1HostOps.OracleAsync(host)).OracleSuccess);
    }

    [AnalyzerLifecycleFact]
    public async Task First_use_missing_prepared_file_is_load_failed_not_execution_success()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Dump("missing-output", load);
        Assert.True(load.PrepareAttempted);
        Assert.Equal("LoadFailed", load.Execution?.Status);
        Assert.DoesNotContain(load.Rewrite ?? new List<RewriteDto>(), r => r.Applied);
        var oracle = await Epoch1HostOps.OracleAsync(host);
        Assert.False(oracle.OracleSuccess);
        Assert.NotEqual(GeneratorConsumerFixture.MarkerV1, oracle.Marker);
    }

    [AnalyzerLifecycleFact]
    public async Task Repeated_supported_loads_record_memory_and_disk_without_inventing_sla()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        long? firstWorkingSet = null;
        long? lastWorkingSet = null;
        long? firstRoot = null;
        long? lastRoot = null;
        for (var i = 0; i < 3; i++)
        {
            if (i > 0)
            {
                _ = await host.SendAsync(new HostCommand { Op = "reset" });
            }

            var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
            await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
            var inspect = await host.SendAsync(new HostCommand { Op = "inspect" });
            lastRoot = inspect.ShadowRootBytes > 0
                ? inspect.ShadowRootBytes
                : MeasureRoot(inspect.ShadowRoot);
            firstRoot ??= lastRoot;
            var env = await host.SendAsync(new HostCommand { Op = "env" });
            Assert.True(env.Environment?.WorkingSetBytes > 0, "host env must report process working set");
            firstWorkingSet ??= env.Environment!.WorkingSetBytes;
            lastWorkingSet = env.Environment.WorkingSetBytes;
            Dump("repeat-" + i, load);
        }

        _output.WriteLine(
            "E3-S5 resources workingSetFirst={0} workingSetLast={1} shadowRootFirst={2} shadowRootLast={3} mode=restart-required dependency=main-only",
            firstWorkingSet,
            lastWorkingSet,
            firstRoot,
            lastRoot);
        Assert.True(firstRoot >= 0);
        Assert.True(lastRoot >= firstRoot);
    }

    private static long MeasureRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return 0;
        }

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
    }

    private void Dump(string label, HostResponse response)
    {
        _output.WriteLine(
            "[{0}] ok={1} err={2} status={3} reason={4} action={5} gen={6} marker={7} overlay={8} loaded={9} dependency={10}",
            label,
            response.Ok,
            response.Error,
            response.Execution?.Status,
            response.Execution?.Reason,
            response.Execution?.Action,
            response.Rewrite?.FirstOrDefault()?.Generation,
            response.Marker,
            response.OverlayAnalyzerPath,
            response.LoadedAnalyzerPath,
            response.Execution?.Dependency);
    }
}
