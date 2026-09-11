using RoslynMcpServer.LifecycleTestHost;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
public sealed class Epoch1WritePathTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        yield return new object[] { OutputPathMode.RedirectedMissingAnalyzerPath };
        yield return new object[] { OutputPathMode.SdkDefaultCorrectPath };
    }

    private readonly ITestOutputHelper _output;

    public Epoch1WritePathTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AnalyzerLifecycleTheory]
    [MemberData(nameof(Fixtures))]
    public async Task Text_edit_UpdateDocumentInMemoryAsync_keeps_marker_text_and_csproj(OutputPathMode mode)
    {
        using var fixture = GeneratorConsumerFixture.Create(mode);
        await using var host = LifecycleHostClient.Start();
        await PrepareAsync(host, fixture);

        var newText = fixture.WithConsumerComment("text-edit-" + mode);
        var afterLoad = await host.SendAsync(new HostCommand { Op = "inspect" });
        var update = await host.SendAsync(
            new HostCommand { Op = "updateDocument", Path = fixture.ConsumerSourcePath, Text = newText });
        Assert.True(update.Ok, update.Error);
        Assert.Equal(afterLoad.OverlayPrepareCount, update.OverlayPrepareCount);
        Assert.Equal(afterLoad.AnalyzerFileIoCount, update.AnalyzerFileIoCount);
        RecordIo(mode, "text-edit", afterLoad, update);

        var oracle = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        Assert.Contains("text-edit-", File.ReadAllText(fixture.ConsumerSourcePath), StringComparison.Ordinal);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
        RecordLoadPath(mode, "text-edit", oracle, snapshot);
        var rebuild = await Epoch1HostOps.AssertForcedGeneratorRebuildWritesBytesAsync(host, fixture);
        RecordRebuild(mode, "text-edit", rebuild);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
    }

    [AnalyzerLifecycleTheory]
    [MemberData(nameof(Fixtures))]
    public async Task Overlay_derived_ApplySolutionChangesToDiskAsync_keeps_marker_text_and_csproj(OutputPathMode mode)
    {
        using var fixture = GeneratorConsumerFixture.Create(mode);
        await using var host = LifecycleHostClient.Start();
        await PrepareAsync(host, fixture);

        var newText = fixture.WithConsumerComment("overlay-apply-" + mode);
        var afterLoad = await host.SendAsync(new HostCommand { Op = "inspect" });
        var apply = await host.SendAsync(
            new HostCommand { Op = "applyOverlayEdit", Path = fixture.ConsumerSourcePath, Text = newText });
        Assert.True(apply.Ok, apply.Error);
        Assert.Equal(afterLoad.OverlayPrepareCount, apply.OverlayPrepareCount);
        Assert.Equal(afterLoad.AnalyzerFileIoCount, apply.AnalyzerFileIoCount);
        RecordIo(mode, "overlay-apply", afterLoad, apply);

        var oracle = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        Assert.Contains("overlay-apply-", File.ReadAllText(fixture.ConsumerSourcePath), StringComparison.Ordinal);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);

        var rename = await host.SendAsync(
            new HostCommand
            {
                Op = "rename",
                Path = fixture.ConsumerSourcePath,
                Symbol = "ConsumerMarkerConsumer",
                NewName = "ConsumerMarkerConsumerRenamed",
            });
        Assert.True(rename.Ok, rename.Error);
        Assert.Contains("ConsumerMarkerConsumerRenamed", rename.DocumentText ?? "", StringComparison.Ordinal);
        _output.WriteLine(
            "Rename SameSnapshotAfterSymbol={0} (production RenameSymbol re-gets GetCurrentSolution after symbol).",
            rename.SameSnapshotAfterSymbol);
        Assert.True(
            rename.SameSnapshotAfterSymbol,
            "E1-S4: GetCurrentSolution() after symbol was a different snapshot than FindDocumentAsync. Follow-up epoch 4.");

        var afterRename = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var snapshot2 = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot2);
        RecordLoadPath(mode, "overlay-apply", afterRename, snapshot2);
        var rebuild = await Epoch1HostOps.AssertForcedGeneratorRebuildWritesBytesAsync(host, fixture);
        RecordRebuild(mode, "overlay-apply", rebuild);
    }

    [AnalyzerLifecycleTheory]
    [MemberData(nameof(Fixtures))]
    public async Task Real_FSW_delivery_then_production_flush_publishes_text_and_marker(OutputPathMode mode)
    {
        using var fixture = GeneratorConsumerFixture.Create(mode);
        await using var host = LifecycleHostClient.Start();
        await PrepareAsync(host, fixture);

        var newText = fixture.WithConsumerComment("fsw-" + mode);
        var afterLoad = await host.SendAsync(new HostCommand { Op = "inspect" });
        File.WriteAllText(fixture.ConsumerSourcePath, newText);

        var wait = await host.SendAsync(
            new HostCommand
            {
                Op = "waitDirty",
                Path = fixture.ConsumerSourcePath,
                TimeoutMs = 15_000,
            });
        if (!wait.DirtyDelivered)
        {
            Assert.Fail(
                "FSW dirty event was not delivered within timeout (not a flush failure). pending="
                + wait.PendingDirtyCount
                + " err="
                + wait.Error
                + " stderr="
                + host.StderrSnapshot);
        }

        var flushed = await host.SendAsync(
            new HostCommand { Op = "flushFind", Path = fixture.ConsumerSourcePath });
        Assert.True(flushed.Ok, "Production flush via FindDocumentAsync failed: " + flushed.Error);
        Assert.Contains("fsw-", flushed.DocumentText ?? "", StringComparison.Ordinal);
        Assert.Equal(afterLoad.OverlayPrepareCount, flushed.OverlayPrepareCount);
        Assert.Equal(afterLoad.AnalyzerFileIoCount, flushed.AnalyzerFileIoCount);
        RecordIo(mode, "fsw-flush", afterLoad, flushed);

        var getter = await host.SendAsync(new HostCommand { Op = "flushGetter" });
        Assert.True(getter.Ok, getter.Error);

        var oracle = await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
        var snapshot = await host.SendAsync(new HostCommand { Op = "snapshotCsproj", Path = fixture.Root });
        Epoch1HostOps.AssertProjectFilesUnchanged(fixture, snapshot);
        RecordLoadPath(mode, "fsw-flush", oracle, snapshot);
        var rebuild = await Epoch1HostOps.AssertForcedGeneratorRebuildWritesBytesAsync(host, fixture);
        RecordRebuild(mode, "fsw-flush", rebuild);
    }

    private static async Task PrepareAsync(LifecycleHostClient host, GeneratorConsumerFixture fixture)
    {
        await Epoch1HostOps.BuildAsync(host, fixture.SolutionPath);
        var load = await Epoch1HostOps.LoadAsync(host, fixture.SolutionPath, shadowCopy: true);
        Assert.True(load.Ok, load.Error);
        await Epoch1HostOps.RequireMarkerAsync(host, GeneratorConsumerFixture.MarkerV1);
    }

    private void RecordIo(OutputPathMode mode, string pathName, HostResponse before, HostResponse after)
    {
        _output.WriteLine(
            "io {0}/{1}: OverlayPrepareCount {2}->{3} AnalyzerFileIoCount {4}->{5}",
            mode,
            pathName,
            before.OverlayPrepareCount,
            after.OverlayPrepareCount,
            before.AnalyzerFileIoCount,
            after.AnalyzerFileIoCount);
    }

    private void RecordLoadPath(OutputPathMode mode, string pathName, HostResponse oracle, HostResponse snapshot)
    {
        _output.WriteLine(
            "anti-lock {0}/{1}: marker={2} overlay={3} workspace={4} loaded={5} identity={6} analyzerIncludes={7}",
            mode,
            pathName,
            oracle.Marker,
            oracle.OverlayAnalyzerPath,
            oracle.WorkspaceAnalyzerPath,
            oracle.LoadedAnalyzerPath,
            oracle.AssemblyIdentity,
            string.Join("; ", snapshot.TemporaryAnalyzerIncludes ?? Array.Empty<string>()));
        Assert.False(string.IsNullOrWhiteSpace(oracle.OverlayAnalyzerPath ?? oracle.LoadedAnalyzerPath));
    }

    private void RecordRebuild(OutputPathMode mode, string pathName, (string Dll, string Before, string After) rebuild)
    {
        _output.WriteLine(
            "forced-rebuild {0}/{1}: dll={2} before={3} after={4}",
            mode,
            pathName,
            rebuild.Dll,
            rebuild.Before,
            rebuild.After);
    }
}

internal sealed class AnalyzerLifecycleTheoryAttribute : TheoryAttribute
{
    public AnalyzerLifecycleTheoryAttribute()
    {
        try
        {
            var (available, reason) = LifecycleEnvironment.Probe.Value;
            if (!available)
            {
                Skip = "Environment unavailable: " + reason;
            }
        }
        catch (Exception ex) when (
            ex is FileNotFoundException
                or FileLoadException
                or BadImageFormatException
                or TypeLoadException
                or InvalidOperationException)
        {
            Skip = "Environment unavailable: " + ex.GetType().Name + ": " + ex.Message;
        }
    }
}
