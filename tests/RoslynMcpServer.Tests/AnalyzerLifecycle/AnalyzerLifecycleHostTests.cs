using RoslynMcpServer.LifecycleTestHost;
using RoslynMcpServer.Services;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// The whole hosted lifecycle suite lives in this one class: one MSBuild lifecycle host and one
/// shadow root per run, so xUnit must not parallelize it
/// (<see cref="AnalyzerLifecycleCollection"/>). Files of the class hold the cases by theme.
/// </summary>
[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
public sealed partial class AnalyzerLifecycleHostTests
{
    private readonly ITestOutputHelper _output;

    public AnalyzerLifecycleHostTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AnalyzerLifecycleFact]
    public async Task Successful_opt_in_executes_exact_marker_from_shadow_path()
    {
        using var fixture = GeneratorConsumerFixture.Create();
        await using var host = LifecycleHostClient.Start();
        await LifecycleHostOps.BuildAsync(host, fixture.SolutionPath);
        var realOutput = LifecycleHostAssert.RequireGeneratorOutput(fixture);

        var load = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var loadCompact = LifecycleHostAssert.Compact("opt-in load", load, realOutput);
        _output.WriteLine(loadCompact);
        Assert.True(load.Ok, loadCompact);
        Assert.True(load.ShadowEnabled, loadCompact);
        Assert.Equal("AllowedMapping", load.PublicationAdmission);
        Assert.False(LifecycleHostAssert.PathsEqual(load.OverlayAnalyzerPath, realOutput), loadCompact);

        var beforeEdit = await LifecycleHostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var beforeCompact = LifecycleHostAssert.Compact("opt-in before-edit", beforeEdit, realOutput);
        Assert.False(LifecycleHostAssert.PathsEqual(beforeEdit.OverlayAnalyzerPath, realOutput), beforeCompact);
        LifecycleHostAssert.AssertPathEqual(beforeEdit.WorkspaceAnalyzerPath, realOutput);
        Assert.False(LifecycleHostAssert.PathsEqual(beforeEdit.OverlayAnalyzerPath, beforeEdit.WorkspaceAnalyzerPath), beforeCompact);
        LifecycleHostAssert.AssertPathEqual(beforeEdit.OverlayAnalyzerPath, beforeEdit.LoadedAnalyzerPath);
        Assert.False(
            LifecycleHostAssert.PathsEqual(beforeEdit.LoadedAnalyzerPath, realOutput),
            "The loaded analyzer must be the shadow copy, not the real output. " + beforeCompact);
        Assert.True(
            LifecycleHostAssert.HasExactPath(beforeEdit.ProcessAnalyzerAssemblies, beforeEdit.OverlayAnalyzerPath!),
            "Shadow path must be loaded in the process. " + beforeCompact);
        Assert.False(
            LifecycleHostAssert.HasPath(beforeEdit.ProcessAnalyzerAssemblies, realOutput),
            "Real output must not be loaded in the process. " + beforeCompact);
        Assert.False(
            LifecycleHostAssert.HasPath(beforeEdit.LoadedAssemblies, realOutput),
            "Real output must not be in the loader snapshot. " + beforeCompact);

        var afterLoad = await host.SendAsync(new HostCommand { Op = "inspect" });
        var edit = await host.SendAsync(
            new HostCommand
            {
                Op = "updateDocument",
                Path = fixture.ConsumerSourcePath,
                Text = fixture.WithConsumerComment("v3-s3-opt-in"),
            });
        var editCompact = LifecycleHostAssert.Compact("opt-in edit", edit, realOutput);
        Assert.True(edit.Ok, editCompact);
        LifecycleHostAssert.AssertNoPublicationIo(afterLoad, edit, editCompact);

        var oracle = await LifecycleHostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var compact = LifecycleHostAssert.Compact("opt-in oracle", oracle, realOutput);
        _output.WriteLine(compact);
        Assert.False(LifecycleHostAssert.PathsEqual(oracle.OverlayAnalyzerPath, realOutput), compact);
        Assert.False(LifecycleHostAssert.PathsEqual(oracle.LoadedAnalyzerPath, realOutput), compact);
        LifecycleHostAssert.AssertPathEqual(oracle.OverlayAnalyzerPath, oracle.LoadedAnalyzerPath);
        Assert.Equal(GeneratorConsumerFixture.MarkerV1, oracle.Marker);
    }

    /// <summary>
    /// A generator rebuilt to a new version must not execute in the process that already loaded the old
    /// one. The cached load and a reset plus load both refuse; that a fresh process does execute the
    /// exact new version is checked by <see cref="V1_to_V2_process_restart_executes_exact_V2"/>.
    /// </summary>
    [AnalyzerLifecycleFact]
    public async Task V1_to_V2_cached_and_reset_refuse_execution_process_restart_runs_V2()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await LifecycleHostOps.BuildAsync(host, fixture.SolutionPath);
        _ = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var v1 = await LifecycleHostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        Assert.Equal("ExecutionObserved", v1.Execution?.Status);

        fixture.SetGeneratorMarker(GeneratorConsumerFixture.MarkerV2);
        await LifecycleHostOps.BuildAsync(host, fixture.SolutionPath);
        var cached = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        await AssertRestartRequiredAsync(host, "cached", cached);

        _ = await host.SendAsync(new HostCommand { Op = "reset" });
        var reset = await LifecycleHostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        await AssertRestartRequiredAsync(host, "reset", reset);
    }

    [AnalyzerLifecycleFact]
    public async Task V1_to_V2_process_restart_executes_exact_V2()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using (var host1 = LifecycleHostClient.Start())
        {
            await LifecycleHostOps.BuildAsync(host1, fixture.SolutionPath);
            _ = await LifecycleHostOps.LoadAsync(host1, fixture.SolutionPath, shadowCopy: true);
            await LifecycleHostOps.RequireMarkerAsync(host1, GeneratorConsumerFixture.MarkerV1);
        }

        fixture.SetGeneratorMarker(GeneratorConsumerFixture.MarkerV2);
        await using var restarted = LifecycleHostClient.Start();
        await LifecycleHostOps.BuildAsync(restarted, fixture.SolutionPath);
        var load = await LifecycleHostOps.LoadAsync(restarted, fixture.SolutionPath, shadowCopy: true);
        Assert.True(load.Rewrite?.Any(r => r.Applied) == true, load.Execution?.Reason);
        var oracle = await LifecycleHostOps.RequireMarkerAsync(restarted, GeneratorConsumerFixture.MarkerV2);
        Dump("restart-v2", oracle);
        Assert.Equal("ExecutionObserved", oracle.Execution?.Status);
        Assert.False(string.IsNullOrWhiteSpace(oracle.LoadedAnalyzerPath ?? oracle.OverlayAnalyzerPath));
        await LifecycleHostOps.AssertForcedGeneratorRebuildWritesBytesAsync(restarted, fixture);
    }

    [AnalyzerLifecycleFact]
    public async Task Two_processes_share_one_root_kill_mid_publish_then_survivor_reuses()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        var dll = Path.Combine(fixture.Root, "kill-mid-Generator.dll");
        File.WriteAllBytes(dll, Enumerable.Repeat((byte)0x5A, 1024).ToArray());
        var sharedRoot = Path.Combine(fixture.Root, "shared-shadow-root");
        Directory.CreateDirectory(sharedRoot);
        var gate = Path.Combine(fixture.Root, "publish-gate.txt");
        var policyRoot = Path.Combine(sharedRoot, AnalyzerShadowPolicy.LayoutSegment);

        await using var hostB = LifecycleHostClient.Start();
        HostResponse publishedB;
        Task<HostResponse> delayedA;
        await using (var hostA = LifecycleHostClient.Start())
        {
            delayedA = hostA.SendAsync(new HostCommand
            {
                Op = "publishGeneration",
                Path = dll,
                ShadowRoot = sharedRoot,
                GatePath = gate,
                TimeoutMs = 20_000,
            });

            var stagingSeen = await WaitForStagingAsync(policyRoot, TimeSpan.FromSeconds(10));
            Assert.True(stagingSeen, "publisher A never created staging before kill");

            publishedB = await hostB.SendAsync(new HostCommand
            {
                Op = "publishGeneration",
                Path = dll,
                ShadowRoot = sharedRoot,
            });
            Assert.True(publishedB.Ok, publishedB.Error);
            Assert.False(string.IsNullOrWhiteSpace(publishedB.Rewrite?.FirstOrDefault()?.Generation));
        }

        try
        {
            _ = await delayedA.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _output.WriteLine("publisher A after kill: {0}: {1}", ex.GetType().Name, ex.Message);
        }

        var survivor = await hostB.SendAsync(new HostCommand
        {
            Op = "publishGeneration",
            Path = dll,
            ShadowRoot = sharedRoot,
        });
        Assert.True(survivor.Ok, survivor.Error);
        Assert.True(survivor.ReusedExisting);
        Assert.Equal(
            publishedB.Rewrite?.FirstOrDefault()?.Generation,
            survivor.Rewrite?.FirstOrDefault()?.Generation);
        Assert.True(File.Exists(survivor.Rewrite?.FirstOrDefault()?.ShadowCopyPath));
        _output.WriteLine(
            "kill-mid-publish gen={0} generationBytes={1} rootBytes={2} reusedB={3} survivorReuse={4}",
            survivor.Rewrite?.FirstOrDefault()?.Generation,
            survivor.GenerationBytes,
            survivor.ShadowRootBytes,
            publishedB.ReusedExisting,
            survivor.ReusedExisting);
    }

    /// <summary>
    /// Runs the production oracle after a refused refresh and asserts both halves of the contract:
    /// the identity gate reports restart-required, and the oracle executes neither version.
    /// </summary>
    private async Task AssertRestartRequiredAsync(LifecycleHostClient host, string label, HostResponse load)
    {
        var oracle = await LifecycleHostOps.OracleAsync(host);
        Dump(label, load);
        Dump(label + "-oracle", oracle);
        LifecycleHostOps.AssertRestartRequired(load, oracle);
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

    private static async Task<bool> WaitForStagingAsync(string policyRoot, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(policyRoot))
            {
                foreach (var dir in Directory.GetDirectories(policyRoot))
                {
                    if (Path.GetFileName(dir).StartsWith(".staging-", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            await Task.Delay(50);
        }

        return false;
    }
}
