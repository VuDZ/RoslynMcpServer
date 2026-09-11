using System.Text.Json;
using RoslynMcpServer.Services;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests;

[Collection("AnalyzerShadowPublisher")]
public sealed class AnalyzerShadowGenerationPublisherTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _root;

    public AnalyzerShadowGenerationPublisherTests(ITestOutputHelper output)
    {
        _output = output;
        AnalyzerShadowGenerationPublisher.ResetTestHooks();
        _root = Path.Combine(Path.GetTempPath(), "RoslynMcpServer.Tests", "e2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        AnalyzerShadowGenerationPublisher.ResetTestHooks();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort.
        }
    }

    [Fact]
    public void Same_content_reuses_generation_without_overwrite()
    {
        var source = WriteDll("same.dll", [1, 2, 3, 4]);
        var first = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(first.Success, first.FailureReason);
        Assert.False(first.ReusedExisting);
        var marker = Path.Combine(first.GenerationDirectory!, "do-not-overwrite.txt");
        File.WriteAllText(marker, "keep");

        var second = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(second.Success, second.FailureReason);
        Assert.True(second.ReusedExisting);
        Assert.Equal(first.GenerationId, second.GenerationId);
        Assert.Equal(first.MainShadowPath, second.MainShadowPath);
        Assert.True(File.Exists(marker));
        Assert.Equal("keep", File.ReadAllText(marker));
    }

    [Fact]
    public void Same_size_and_timestamp_but_different_bytes_get_new_identity()
    {
        var source = WriteDll("mut.dll", [10, 10, 10, 10]);
        var timestamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, timestamp);
        var first = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(first.Success, first.FailureReason);

        File.WriteAllBytes(source, [11, 11, 11, 11]);
        File.SetLastWriteTimeUtc(source, timestamp);
        Assert.Equal(4, new FileInfo(source).Length);

        var second = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(second.Success, second.FailureReason);
        Assert.False(second.ReusedExisting);
        Assert.NotEqual(first.GenerationId, second.GenerationId);
        Assert.True(Directory.Exists(first.GenerationDirectory));
        Assert.True(Directory.Exists(second.GenerationDirectory));
    }

    [Fact]
    public void Pdb_copy_failure_does_not_fail_main_only_and_does_not_patch_published_generation()
    {
        var source = WriteDll("pdb.dll", [9, 8, 7, 6]);
        File.WriteAllBytes(Path.ChangeExtension(source, ".pdb"), [1, 2, 3]);
        AnalyzerShadowGenerationPublisher.RemainingForcedPdbCopyFailures = 1;
        var first = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(first.Success, first.FailureReason);
        Assert.False(File.Exists(Path.ChangeExtension(first.MainShadowPath!, ".pdb")));

        AnalyzerShadowGenerationPublisher.RemainingForcedPdbCopyFailures = 0;
        var second = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(second.ReusedExisting);
        Assert.False(File.Exists(Path.ChangeExtension(second.MainShadowPath!, ".pdb")));
    }

    [Fact]
    public void Interrupted_staging_does_not_publish_and_leaves_destination_untouched()
    {
        var source = WriteDll("int.dll", [5, 5, 5, 5]);
        AnalyzerShadowGenerationPublisher.AfterRequiredCopyBeforeManifest = _ =>
            throw new IOException("interrupted-staging");
        var failed = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.False(failed.Success);
        var policyRoot = AnalyzerShadowPolicy.GetPolicyRoot(_root);
        if (Directory.Exists(policyRoot))
        {
            Assert.DoesNotContain(
                Directory.GetDirectories(policyRoot),
                d => !Path.GetFileName(d).StartsWith(".staging-", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Missing_manifest_is_unfit_and_is_not_replaced()
    {
        var source = WriteDll("miss.dll", [1, 1, 1, 1]);
        var first = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(first.Success, first.FailureReason);
        File.Delete(Path.Combine(first.GenerationDirectory!, AnalyzerShadowPolicy.ManifestFileName));
        var marker = Path.Combine(first.GenerationDirectory!, "payload.bin");
        File.WriteAllBytes(marker, [42]);

        var second = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.False(second.Success);
        Assert.Contains("unfit", second.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(marker));
        Assert.False(File.Exists(Path.Combine(first.GenerationDirectory!, AnalyzerShadowPolicy.ManifestFileName)));
    }

    [Fact]
    public void Corrupt_required_file_is_unfit_and_is_not_replaced()
    {
        var source = WriteDll("corr.dll", [2, 2, 2, 2]);
        var first = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(first.Success, first.FailureReason);
        File.WriteAllBytes(first.MainShadowPath!, [9, 9, 9, 9]);

        var second = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.False(second.Success);
        Assert.Contains("unfit", second.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new byte[] { 9, 9, 9, 9 }, File.ReadAllBytes(first.MainShadowPath!));
    }

    [Fact]
    public void Unsafe_manifest_path_is_rejected_without_deleting_destination()
    {
        var source = WriteDll("unsafe.dll", [3, 3, 3, 3]);
        var first = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(first.Success, first.FailureReason);
        var manifestPath = Path.Combine(first.GenerationDirectory!, AnalyzerShadowPolicy.ManifestFileName);
        var manifest = JsonSerializer.Deserialize<AnalyzerShadowManifest>(
            File.ReadAllText(manifestPath),
            AnalyzerShadowManifest.JsonOptions)!;
        manifest.Required[0].RelativePath = "../escape.dll";
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, AnalyzerShadowManifest.JsonOptions));

        var second = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.False(second.Success);
        Assert.Contains("unfit", second.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(first.GenerationDirectory));
    }

    [Fact]
    public void Wrong_policy_is_unfit()
    {
        var source = WriteDll("pol.dll", [4, 4, 4, 4]);
        var first = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(first.Success, first.FailureReason);
        var manifestPath = Path.Combine(first.GenerationDirectory!, AnalyzerShadowPolicy.ManifestFileName);
        var manifest = JsonSerializer.Deserialize<AnalyzerShadowManifest>(
            File.ReadAllText(manifestPath),
            AnalyzerShadowManifest.JsonOptions)!;
        manifest.Policy = "dependency-set";
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, AnalyzerShadowManifest.JsonOptions));

        var second = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.False(second.Success);
        Assert.Contains("unfit", second.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Move_failure_does_not_create_or_replace_destination()
    {
        var source = WriteDll("move.dll", [6, 6, 6, 6]);
        AnalyzerShadowGenerationPublisher.RemainingForcedMoveFailures = 1;
        var failed = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.False(failed.Success);
        Assert.Contains("io-failure", failed.FailureReason, StringComparison.OrdinalIgnoreCase);
        var policyRoot = AnalyzerShadowPolicy.GetPolicyRoot(_root);
        Assert.True(!Directory.Exists(policyRoot) || Directory.GetDirectories(policyRoot).Length == 0);
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("manifest")]
    [InlineData("move")]
    public void Disk_full_at_copy_manifest_or_move_does_not_activate_partial_staging(string stage)
    {
        var source = WriteDll("full-" + stage + ".dll", [7, 7, 7, 7]);
        AnalyzerShadowGenerationPublisher.ForcedDiskFullStage = stage;
        var failed = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.False(failed.Success);
        Assert.Contains("disk-full", failed.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(stage, failed.FailureReason, StringComparison.OrdinalIgnoreCase);
        var policyRoot = AnalyzerShadowPolicy.GetPolicyRoot(_root);
        Assert.True(!Directory.Exists(policyRoot) || Directory.GetDirectories(policyRoot).Length == 0);
    }

    [Fact]
    public void Cleanup_refuses_path_outside_allowed_parent_even_when_owners_stopped()
    {
        var outside = Path.Combine(Path.GetTempPath(), "RoslynMcpServer.NotAllowed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            Assert.False(
                AnalyzerShadowCacheCleanup.TryDeleteRoot(outside, allOwningProcessesStopped: true, out var reason));
            Assert.Equal("outside-allowed-cache-parent", reason);
            Assert.True(Directory.Exists(outside));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Source_change_during_copy_is_detected()
    {
        var source = WriteDll("chg.dll", [8, 8, 8, 8]);
        var n = 0;
        AnalyzerShadowGenerationPublisher.MutateSourceAfterFirstHash = path =>
        {
            n++;
            File.WriteAllBytes(path, [(byte)n, (byte)n, (byte)n, (byte)n]);
        };
        var failed = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.False(failed.Success);
        Assert.Contains("source-unstable", failed.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Timestamp_layout_without_manifest_is_not_a_v2_generation()
    {
        var source = WriteDll("old.dll", [0, 1, 2, 3]);
        var legacy = Path.Combine(_root, "Generator", "123456789");
        Directory.CreateDirectory(legacy);
        File.Copy(source, Path.Combine(legacy, "old.dll"));

        var published = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(published.Success, published.FailureReason);
        Assert.Contains("v2-main-only", published.GenerationDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(legacy, "old.dll")));
        Assert.NotEqual(legacy, published.GenerationDirectory);
    }

    [Fact]
    public void Forced_copy_failure_does_not_publish()
    {
        var source = WriteDll("copyfail.dll", [1, 0, 1, 0]);
        AnalyzerShadowGenerationPublisher.RemainingForcedCopyFailures = 1;
        var failed = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.False(failed.Success);
        Assert.Contains("Forced copy failure", failed.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cleanup_requires_confirmed_stopped_owners()
    {
        var source = WriteDll("clean.dll", [2, 0, 2, 0]);
        var published = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(published.Success, published.FailureReason);

        Assert.False(AnalyzerShadowCacheCleanup.TryDeleteRoot(_root, allOwningProcessesStopped: false, out var blocked));
        Assert.Equal("owners-not-confirmed-stopped", blocked);
        Assert.True(Directory.Exists(published.GenerationDirectory));

        Assert.True(AnalyzerShadowCacheCleanup.TryDeleteRoot(_root, allOwningProcessesStopped: true, out var deleted));
        Assert.Equal("deleted", deleted);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void Growth_same_content_does_not_add_generation_changed_content_does()
    {
        var source = WriteDll("grow.dll", Enumerable.Repeat((byte)3, 256).ToArray());
        var first = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(first.Success, first.FailureReason);
        var afterFirst = AnalyzerShadowGenerationPublisher.MeasureDirectoryBytes(_root);

        var reuse = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(reuse.ReusedExisting);
        var afterReuse = AnalyzerShadowGenerationPublisher.MeasureDirectoryBytes(_root);
        Assert.Equal(afterFirst, afterReuse);

        File.WriteAllBytes(source, Enumerable.Repeat((byte)4, 256).ToArray());
        var second = AnalyzerShadowGenerationPublisher.PublishMainOnly(source, _root, "Generator");
        Assert.True(second.Success, second.FailureReason);
        Assert.False(second.ReusedExisting);
        var afterSecond = AnalyzerShadowGenerationPublisher.MeasureDirectoryBytes(_root);
        Assert.True(afterSecond > afterReuse);
        Assert.True(first.GenerationBytes > 0);
        Assert.True(second.GenerationBytes > 0);
        _output.WriteLine(
            "growth firstGenBytes={0} reuseRootBytes={1} secondGenBytes={2} afterSecondRootBytes={3}",
            first.GenerationBytes,
            afterReuse,
            second.GenerationBytes,
            afterSecond);
    }

    private string WriteDll(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, "src", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}

[CollectionDefinition("AnalyzerShadowPublisher", DisableParallelization = true)]
public sealed class AnalyzerShadowPublisherCollection
{
}
