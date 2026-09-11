using RoslynMcpServer.LifecycleTestHost;
using RoslynMcpServer.Services;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
public sealed class Epoch2ImmutableShadowTests
{
    private readonly ITestOutputHelper _output;

    public Epoch2ImmutableShadowTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AnalyzerLifecycleFact]
    public async Task Cached_refresh_same_size_timestamp_gets_new_content_identity()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var first = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(first.Ok, first.Error);
        var firstGeneration = first.Rewrite?.FirstOrDefault()?.Generation;
        Assert.False(string.IsNullOrWhiteSpace(firstGeneration));

        var dlls = Directory.GetFiles(fixture.Root, "Generator.dll", SearchOption.AllDirectories)
            .Where(p => p.Contains($"{Path.DirectorySeparatorChar}v2-main-only{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) is false)
            .ToList();
        Assert.NotEmpty(dlls);
        foreach (var dll in dlls)
        {
            var original = File.ReadAllBytes(dll);
            var timestamp = File.GetLastWriteTimeUtc(dll);
            var mutated = (byte[])original.Clone();
            mutated[^1] ^= 0x5A;
            File.WriteAllBytes(dll, mutated);
            File.SetLastWriteTimeUtc(dll, timestamp);
            Assert.Equal(original.Length, mutated.Length);
        }

        var cached = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(cached.CacheHit);
        Assert.True(cached.PrepareAttempted);
        var secondGeneration = cached.Rewrite?.FirstOrDefault()?.Generation;
        Assert.False(string.IsNullOrWhiteSpace(secondGeneration));
        Assert.NotEqual(firstGeneration, secondGeneration);
        _output.WriteLine(
            "content-identity first={0} second={1} overlay={2}",
            firstGeneration,
            secondGeneration,
            cached.OverlayAnalyzerPath);
    }

    [AnalyzerLifecycleFact]
    public async Task Failed_refresh_keeps_stale_mapping_and_edit_does_not_clear_it()
    {
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.RedirectedMissingAnalyzerPath);
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        _ = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        var before = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var shadowBefore = before.OverlayAnalyzerPath;

        _ = await host.SendAsync(new HostCommand { Op = "injectPrepareFailure" });
        var refresh = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(refresh.CacheHit);
        Assert.True(refresh.PrepareInjectedFailure);
        Assert.True(refresh.LastRefreshStale);
        Assert.True(refresh.MappingPresent);
        Assert.Equal(shadowBefore, refresh.OverlayAnalyzerPath);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);

        var edited = fixture.WithConsumerComment("after-stale-refresh");
        var update = await host.SendAsync(
            new HostCommand { Op = "updateDocument", Path = fixture.ConsumerSourcePath, Text = edited });
        Assert.True(update.Ok, update.Error);
        Assert.True(update.LastRefreshStale);
        Assert.Equal(shadowBefore, update.OverlayAnalyzerPath);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
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
