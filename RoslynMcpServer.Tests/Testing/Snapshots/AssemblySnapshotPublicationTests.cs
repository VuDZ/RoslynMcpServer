using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Snapshots.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Snapshots;

/// <summary>
/// The snapshot through a real workspace load. The output assemblies are compiled into the paths MSBuild
/// evaluates for the projects, so no build runs.
/// </summary>
public sealed class AssemblySnapshotPublicationTests
{
    [Fact]
    public async Task Publication_does_not_wait_for_the_first_read_and_the_snapshot_covers_every_project()
    {
        await using var fixture = new BaselineLifecycleFixture();
        var solutionPath = fixture.LinkedSolution();
        var (first, second) = WriteOutputs(fixture);
        var assemblies = fixture.Manager.Baseline.Assemblies;
        using var gate = new AssemblyReadGate(first);
        assemblies.BeforeFirstReadForTests = gate.WaitAsync;

        // The read of First is held until after the load returns: a load that waited for it would time out.
        var solution = await fixture.Manager.LoadAsync(solutionPath).WaitAsync(AssemblySnapshotFixture.Timeout);
        await gate.Entered.WaitAsync(AssemblySnapshotFixture.Timeout);
        Assert.False(assemblies.FirstReadTask.IsCompleted);
        AssertOutputs(solution, first, second);
        gate.Release();
        await assemblies.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);

        var before = assemblies.PeekBefore()!;
        Assert.Equal(2, before.Entries.Length);
        Assert.All(before.Entries, entry => Assert.Equal(AssemblySnapshotState.Read, entry.State));
    }

    [Fact]
    public async Task Reset_workspace_cancels_the_unfinished_first_read_and_reload_keeps_the_snapshot()
    {
        // M-10: reset_workspace of the same solution keeps the baseline and therefore the snapshot.
        await using var fixture = new BaselineLifecycleFixture();
        var solutionPath = fixture.LinkedSolution();
        var (first, second) = WriteOutputs(fixture);
        var assemblies = fixture.Manager.Baseline.Assemblies;
        using var gate = new AssemblyReadGate(second);
        assemblies.BeforeFirstReadForTests = gate.WaitAsync;
        await fixture.Manager.LoadAsync(solutionPath);
        await gate.Entered.WaitAsync(AssemblySnapshotFixture.Timeout);
        await AssemblySnapshotFixture.WaitUntilAsync(() => assemblies.PeekBefore()?.Find(first) is not null, "First is read");
        var read = assemblies.PeekBefore()!.Find(first);
        var cancelled = assemblies.FirstReadTask;

        await fixture.Manager.ClearWorkspaceAsync().WaitAsync(AssemblySnapshotFixture.Timeout);
        Assert.Same(read, assemblies.PeekBefore()!.Find(first));
        Assert.Equal(AssemblySnapshotState.Unknown, assemblies.PeekBefore()!.Find(second)!.State);
        gate.Release();
        await cancelled.WaitAsync(AssemblySnapshotFixture.Timeout);

        var solution = await fixture.Manager.LoadAsync(solutionPath);
        var pair = (await assemblies.GetPairAsync(solution, CancellationToken.None))!;
        Assert.Same(read, pair.Before.Find(first));
        Assert.Equal(AssemblySnapshotState.Unknown, pair.Before.Find(second)!.State);
        Assert.Equal(AssemblySnapshotState.Read, pair.Current.Find(second)!.State);
    }

    private static (string First, string Second) WriteOutputs(BaselineLifecycleFixture fixture)
    {
        var first = Output(fixture, "First");
        var second = Output(fixture, "Second");
        var past = DateTime.UtcNow.AddDays(-1);
        AssemblySnapshotFixture.WriteBytes(first, AssemblySnapshotFixture.Compile("First", "public class FirstType { }"), past);
        AssemblySnapshotFixture.WriteBytes(second, AssemblySnapshotFixture.Compile("Second", "public class SecondType { }"), past);
        return (first, second);
    }

    private static string Output(BaselineLifecycleFixture fixture, string name) =>
        Path.Combine(fixture.Root, name, "bin", "Debug", "net10.0", name + ".dll");

    private static void AssertOutputs(Solution solution, params string[] expected)
    {
        var outputs = solution.Projects.Select(project => project.OutputFilePath!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.Order(StringComparer.Ordinal), outputs, IlPaths.Comparer);
    }
}
