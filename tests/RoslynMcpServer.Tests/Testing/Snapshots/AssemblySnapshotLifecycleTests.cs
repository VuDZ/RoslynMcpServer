using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Services.Testing.Baseline;
using RoslynMcpServer.Services.Testing.Snapshots;
using RoslynMcpServer.Services.Testing.Snapshots.Models;
using RoslynMcpServer.Tests.SourceStructure;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Snapshots;

/// <summary>
/// The snapshot follows the baseline through <see cref="BaselineSession"/>: the same publication, reload and
/// interruption calls the workspace makes, with solutions built in memory.
/// </summary>
public sealed class AssemblySnapshotLifecycleTests
{
    [Fact]
    public async Task Solution_request_belongs_to_the_baseline_current_when_it_starts()
    {
        // Post-acceptance review, round 1 (PF-01): finding the test projects of the solution takes long enough
        // for another baseline to start. The request must still answer for the baseline it started with, not
        // label the projects of the old solution with the new one.
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var other = fixture.Write("Other", "public class O { }");
        var session = fixture.Start(new AssemblySnapshotSession(), "Lib");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        using var loader = new BlockingTextLoader();
        var solution = Solution(fixture, "Lib");
        var libProject = solution.Projects.Single().Id;
        solution = solution.AddDocument(DocumentInfo.Create(
            DocumentId.CreateNewId(libProject),
            "Lib.cs",
            loader: loader,
            filePath: Path.Combine(fixture.Root, "Lib", "Lib.cs")));

        var request = Task.Run(() => session.GetPairAsync(solution, CancellationToken.None));
        await loader.Entered.WaitAsync(AssemblySnapshotFixture.Timeout);
        var secondPath = Path.Combine(fixture.Root, "Second.slnx");
        session.Start(secondPath, DateTime.UtcNow, fixture.Inventory("Other"), NullLogger.Instance).Cancel();
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        loader.Release();
        var pair = (await request.WaitAsync(AssemblySnapshotFixture.Timeout))!;

        Assert.True(pair.BelongsTo(Path.Combine(fixture.Root, "Solution.slnx"), fixture.PublishedAtUtc));
        Assert.False(pair.BelongsTo(secondPath, fixture.PublishedAtUtc));
        Assert.Equal(AssemblySnapshotState.Read, pair.Before.Find(lib)!.State);
        Assert.Null(pair.Before.Find(other));
        Assert.Equal(AssemblySnapshotState.Read, pair.Current.Find(lib)!.State);
    }

    [Fact]
    public async Task Pair_after_a_reload_belongs_to_the_baseline_the_session_reports()
    {
        // Post-acceptance review, round 1 (PN-03): the baseline snapshot and the pair name the baseline from
        // their own sources. A path that needs canonicalization and a reload of the same solution must not
        // make them disagree, or narrowing would never happen after a reload.
        using var fixture = new AssemblySnapshotFixture();
        fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var baseline = new BaselineSession();
        var workspacePath = Path.Combine(fixture.Root, "Nested", "..", "First.slnx");
        var solution = Solution(fixture, "Lib");
        baseline.Publish(workspacePath, solution, fixture.PublishedAtUtc, NullLogger.Instance);
        await baseline.Assemblies.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        baseline.BeginLoad(workspacePath, NullLogger.Instance);
        baseline.Publish(workspacePath, solution, DateTime.UtcNow, NullLogger.Instance);

        var snapshot = baseline.GetSnapshot();
        var pair = (await baseline.Assemblies.GetPairAsync(solution, CancellationToken.None))!;

        Assert.True(pair.BelongsTo(snapshot.WorkspacePath, snapshot.PublishedAtUtc));
        Assert.Equal(fixture.PublishedAtUtc, snapshot.PublishedAtUtc);
    }

    [Fact]
    public async Task Reload_of_the_same_solution_keeps_the_before_snapshot_and_a_new_project_has_none()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var other = fixture.Write("Other", "public class O { }");
        var baseline = new BaselineSession();
        var workspacePath = Path.Combine(fixture.Root, "First.slnx");
        baseline.Publish(workspacePath, Solution(fixture, "Lib"), fixture.PublishedAtUtc, NullLogger.Instance);
        await baseline.Assemblies.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        var read = baseline.Assemblies.PeekBefore()!.Find(lib);
        Assert.Equal(AssemblySnapshotState.Read, read!.State);

        baseline.BeginLoad(workspacePath, NullLogger.Instance);
        baseline.Publish(workspacePath, Solution(fixture, "Lib", "Other"), DateTime.UtcNow, NullLogger.Instance);
        await baseline.Assemblies.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);

        Assert.Same(read, baseline.Assemblies.PeekBefore()!.Find(lib));
        Assert.Null(baseline.Assemblies.PeekBefore()!.Find(other));
        var pair = (await baseline.Assemblies.GetPairAsync(fixture.Projects("Lib", "Other"), CancellationToken.None))!;
        Assert.Equal(fixture.PublishedAtUtc, pair.PublishedAtUtc);
        Assert.Null(pair.Before.Find(other));
        Assert.Equal(AssemblySnapshotState.Read, pair.Current.Find(other)!.State);
    }

    [Fact]
    public async Task Another_solution_starts_a_new_snapshot_and_the_cancelled_read_publishes_nothing()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var other = fixture.Write("Other", "public class O { }");
        var baseline = new BaselineSession();
        using var gate = new AssemblyReadGate(lib);
        baseline.Assemblies.BeforeFirstReadForTests = gate.WaitAsync;
        var firstPath = Path.Combine(fixture.Root, "First.slnx");
        baseline.Publish(firstPath, Solution(fixture, "Lib"), fixture.PublishedAtUtc, NullLogger.Instance);
        await gate.Entered.WaitAsync(AssemblySnapshotFixture.Timeout);
        var cancelled = baseline.Assemblies.FirstReadTask;

        var secondPath = Path.Combine(fixture.Root, "Second.slnx");
        baseline.BeginLoad(secondPath, NullLogger.Instance);
        var secondPublication = DateTime.UtcNow;
        baseline.Publish(secondPath, Solution(fixture, "Other"), secondPublication, NullLogger.Instance);
        await baseline.Assemblies.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        gate.Release();
        await cancelled.WaitAsync(AssemblySnapshotFixture.Timeout);

        var before = baseline.Assemblies.PeekBefore()!;
        Assert.Null(before.Find(lib));
        Assert.Equal(AssemblySnapshotState.Read, before.Find(other)!.State);
        var pair = (await baseline.Assemblies.GetPairAsync(fixture.Projects("Other"), CancellationToken.None))!;
        Assert.Equal(Path.GetFullPath(secondPath), pair.WorkspacePath);
        Assert.Equal(secondPublication, pair.PublishedAtUtc);
        Assert.Null(pair.Before.Find(lib));
    }

    [Fact]
    public async Task Reset_cancels_the_first_read_keeps_read_assemblies_and_reload_keeps_the_snapshot()
    {
        // M-10: reset_workspace of the same solution keeps the baseline, so it keeps the snapshot as well.
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var other = fixture.Write("Other", "public class O { }");
        var baseline = new BaselineSession();
        using var gate = new AssemblyReadGate(other);
        baseline.Assemblies.BeforeFirstReadForTests = gate.WaitAsync;
        var workspacePath = Path.Combine(fixture.Root, "First.slnx");
        var solution = Solution(fixture, "Lib", "Other");
        baseline.Publish(workspacePath, solution, fixture.PublishedAtUtc, NullLogger.Instance);
        await gate.Entered.WaitAsync(AssemblySnapshotFixture.Timeout);
        await AssemblySnapshotFixture.WaitUntilAsync(() => baseline.Assemblies.PeekBefore()!.Find(lib) is not null, "Lib is read");
        var read = baseline.Assemblies.PeekBefore()!.Find(lib);
        var cancelled = baseline.Assemblies.FirstReadTask;

        baseline.Interrupt(NullLogger.Instance);

        Assert.Same(read, baseline.Assemblies.PeekBefore()!.Find(lib));
        Assert.Equal(AssemblySnapshotState.Unknown, baseline.Assemblies.PeekBefore()!.Find(other)!.State);
        gate.Release();
        await cancelled.WaitAsync(AssemblySnapshotFixture.Timeout);
        Assert.Equal(AssemblySnapshotState.Unknown, baseline.Assemblies.PeekBefore()!.Find(other)!.State);

        baseline.BeginLoad(workspacePath, NullLogger.Instance);
        baseline.Publish(workspacePath, solution, DateTime.UtcNow, NullLogger.Instance);
        await baseline.Assemblies.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);

        var pair = (await baseline.Assemblies.GetPairAsync(fixture.Projects("Lib", "Other"), CancellationToken.None))!;
        Assert.Equal(fixture.PublishedAtUtc, pair.PublishedAtUtc);
        Assert.Same(read, pair.Before.Find(lib));
        Assert.Equal(AssemblySnapshotState.Unknown, pair.Before.Find(other)!.State);
        Assert.Equal(AssemblySnapshotState.Read, pair.Current.Find(other)!.State);
    }

    [Fact]
    public async Task Reset_while_an_assembly_is_being_read_keeps_it_unknown_through_reload()
    {
        // M-10 with the reset arriving while the file is open: the read in progress must not be published.
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var baseline = new BaselineSession();
        var resets = 0;
        baseline.Assemblies.Reader.WhileFileIsOpenForTests = path =>
        {
            if (path == lib && Interlocked.Increment(ref resets) == 1)
            {
                baseline.Interrupt(NullLogger.Instance);
            }
        };
        var workspacePath = Path.Combine(fixture.Root, "First.slnx");
        var solution = Solution(fixture, "Lib");
        baseline.Publish(workspacePath, solution, fixture.PublishedAtUtc, NullLogger.Instance);
        await baseline.Assemblies.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);

        Assert.Equal(1, resets);
        Assert.Equal(AssemblySnapshotState.Unknown, baseline.Assemblies.PeekBefore()!.Find(lib)!.State);
        baseline.BeginLoad(workspacePath, NullLogger.Instance);
        baseline.Publish(workspacePath, solution, DateTime.UtcNow, NullLogger.Instance);
        var pair = (await baseline.Assemblies.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None))!;
        Assert.Equal(AssemblySnapshotState.Unknown, pair.Before.Find(lib)!.State);
        Assert.Equal(AssemblySnapshotState.Read, pair.Current.Find(lib)!.State);
        Assert.Equal(2, baseline.Assemblies.Reader.FileReads);
    }

    [Fact]
    public async Task Snapshot_keeps_no_solution()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var baseline = new BaselineSession();

        var solution = await PublishAndRequestAsync(baseline, fixture);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(solution.IsAlive, "The snapshot must not keep the published or the requested solution.");
        Assert.Equal(AssemblySnapshotState.Read, baseline.Assemblies.PeekBefore()!.Find(lib)!.State);
    }

    [Fact]
    public async Task Current_neighbor_map_lists_a_solution_assembly_copied_next_to_the_tests_after_load()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var tests = fixture.Write("Tests", "public class LibTests { }");
        var baseline = new BaselineSession();
        var solution = Solution(fixture, "Lib", "Tests");
        baseline.Publish(Path.Combine(fixture.Root, "First.slnx"), solution, fixture.PublishedAtUtc, NullLogger.Instance);
        await baseline.Assemblies.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        var beforeCopy = (await baseline.Assemblies.GetPairAsync(solution, CancellationToken.None))!;
        Assert.Empty(beforeCopy.Neighbors.NeighborsByTestOutputPath[tests]);

        File.Copy(lib, Path.Combine(Path.GetDirectoryName(tests)!, "Lib.dll"));
        var pair = (await baseline.Assemblies.GetPairAsync(solution, CancellationToken.None))!;

        Assert.Equal(new[] { lib }, pair.Neighbors.NeighborsByTestOutputPath[tests]);
        Assert.DoesNotContain(lib, pair.Neighbors.NeighborsByTestOutputPath.Keys);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> PublishAndRequestAsync(BaselineSession baseline, AssemblySnapshotFixture fixture)
    {
        using var workspace = new AdhocWorkspace();
        var solution = AddProjects(workspace, fixture, "Lib");
        baseline.Publish(Path.Combine(fixture.Root, "First.slnx"), solution, fixture.PublishedAtUtc, NullLogger.Instance);
        await baseline.Assemblies.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        await baseline.CaptureTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        Assert.NotNull(await baseline.Assemblies.GetPairAsync(solution, CancellationToken.None));
        return new WeakReference(solution);
    }

    /// <summary>
    /// Projects named after the fixture assemblies with their output paths. A project whose name ends with
    /// <c>Tests</c> gets a test method, so epoch 1 counts it as a test project.
    /// </summary>
    private static Solution Solution(AssemblySnapshotFixture fixture, params string[] names) =>
        AddProjects(new AdhocWorkspace(), fixture, names);

    private static Solution AddProjects(AdhocWorkspace workspace, AssemblySnapshotFixture fixture, params string[] names)
    {
        var solution = workspace.CurrentSolution;
        foreach (var name in names)
        {
            var id = ProjectId.CreateNewId(name);
            solution = solution.AddProject(ProjectInfo.Create(
                id,
                VersionStamp.Create(),
                name,
                name,
                LanguageNames.CSharp,
                outputFilePath: fixture.PathOf(name),
                metadataReferences: MetadataReferenceSets.Runtime()));
            if (name.EndsWith("Tests", StringComparison.Ordinal))
            {
                solution = solution.AddDocument(
                    DocumentId.CreateNewId(id),
                    "LibTests.cs",
                    SourceText.From("public class LibTests { [Xunit.Fact] public void Runs() { } }"),
                    filePath: Path.Combine(fixture.Root, name, "LibTests.cs"));
            }
        }

        return solution;
    }

    /// <summary>Holds the first load of the document text until the test releases it.</summary>
    private sealed class BlockingTextLoader : TextLoader, IDisposable
    {
        internal Task Entered => _entered.Task;

        public override async Task<TextAndVersion> LoadTextAndVersionAsync(LoadTextOptions options, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(AssemblySnapshotFixture.Timeout, cancellationToken);
            return TextAndVersion.Create(SourceText.From("public class LibCode { }"), VersionStamp.Create());
        }

        public void Dispose() => Release();

        internal void Release() => _release.TrySetResult();

        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
