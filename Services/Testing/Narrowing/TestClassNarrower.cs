using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Il.Models;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using RoslynMcpServer.Services.Testing.Snapshots;

namespace RoslynMcpServer.Services.Testing.Narrowing;

/// <summary>
/// Narrows each test project that epoch 1 selected whole to test classes in stages, from the two assembly
/// snapshots of the session, or names why the project stays whole. Narrowing never adds a project.
/// </summary>
internal static class TestClassNarrower
{
    /// <summary>Takes the pair of snapshots and the times of files, then narrows.</summary>
    internal static Task<TestNarrowingResult> NarrowAsync(
        Solution solution,
        TestImpactEstimate estimate,
        AssemblySnapshotSession assemblies,
        CancellationToken cancellationToken) =>
        NarrowAsync(solution, estimate, assemblies, assemblyInputs: null, projectId: null, cancellationToken);

    /// <summary>Narrows one project of the estimate after its build.</summary>
    /// <param name="assemblyInputs">
    /// The outputs of <paramref name="solution"/> from <see cref="AssemblySnapshotSession.InputsAsync"/>, taken once
    /// for all the projects of a run.
    /// </param>
    internal static Task<TestNarrowingResult> AnalyzeProjectAsync(
        Solution solution,
        TestImpactEstimate estimate,
        AssemblySnapshotSession assemblies,
        IReadOnlyList<IlAssemblyInput> assemblyInputs,
        ProjectId projectId,
        CancellationToken cancellationToken) =>
        NarrowAsync(solution, estimate, assemblies, assemblyInputs, projectId, cancellationToken);

    /// <summary>The answer for a project whose narrowing threw: it runs whole, and the answer names the error.</summary>
    internal static TestNarrowingResult Failed(ProjectId projectId, Exception exception) =>
        new([
            Whole(projectId, new TestNarrowingWholeReason(
                TestNarrowingWholeKind.NarrowingFailed,
                null,
                $"Narrowing failed: {exception.GetType().Name}: {exception.Message}")),
        ]);

    /// <summary>Decides from the input alone; no file is read.</summary>
    internal static TestNarrowingResult Narrow(TestNarrowingInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Narrow(input, Ordered(input.Solution, input.Impact), cancellationToken);
    }

    private static async Task<TestNarrowingResult> NarrowAsync(
        Solution solution,
        TestImpactEstimate estimate,
        AssemblySnapshotSession assemblies,
        IReadOnlyList<IlAssemblyInput>? assemblyInputs,
        ProjectId? projectId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentNullException.ThrowIfNull(assemblies);
        ProjectId[] projects = [.. Ordered(solution, estimate.Impact).Where(id => projectId is null || id == projectId)];
        ProjectId[] direct =
        [
            .. estimate.Plans.Select(plan => plan.ProjectId).Where(id => !estimate.Impact.ExpandedTestProjects.ContainsKey(id)
                && (projectId is null || id == projectId) && solution.GetProject(id) is not null),
        ];
        if (projects.Length == 0 && direct.Length == 0)
        {
            return TestNarrowingResult.Empty;
        }

        var pair = assemblyInputs is null
            ? await assemblies.GetPairAsync(solution, cancellationToken).ConfigureAwait(false)
            : await assemblies.GetPairAsync(assemblyInputs, cancellationToken).ConfigureAwait(false);
        if (pair is null)
        {
            return AllWhole(projects, new TestNarrowingWholeReason(
                TestNarrowingWholeKind.NoBeforeSnapshot,
                null,
                "No baseline was published, so no assembly has a snapshot before the edit."));
        }

        // A new baseline between the estimate and the pair can reuse an output path with other content in its
        // snapshot before the edit; comparing across baselines could then report "code unchanged" and drop
        // every test (post-acceptance review of task-03, POST-F-01).
        if (!pair.BelongsTo(estimate.BaselineWorkspacePath, estimate.BaselinePublishedAtUtc))
        {
            return AllWhole(projects, new TestNarrowingWholeReason(
                TestNarrowingWholeKind.BaselineChanged,
                null,
                "Another solution was loaded while the request ran, so the assembly snapshots and the epoch 1 answer belong to different baselines; repeat the request."));
        }

        var input = TestNarrowingInputs.Collect(solution, estimate, pair);
        return Narrow(input, projects, cancellationToken) with
        {
            DirectChangedTypes = CompareDirectProjects(input, direct, cancellationToken),
        };
    }

    private static TestNarrowingResult Narrow(
        TestNarrowingInput input,
        IEnumerable<ProjectId> projectIds,
        CancellationToken cancellationToken)
    {
        var projects = new List<TestProjectNarrowing>();
        foreach (var projectId in projectIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            projects.Add(NarrowProject(input, projectId, cancellationToken));
        }

        return new TestNarrowingResult(projects);
    }

    private static IReadOnlyList<IlTypeName> CompareDirectProjects(
        TestNarrowingInput input,
        IReadOnlyList<ProjectId> projectIds,
        CancellationToken cancellationToken)
    {
        var types = new HashSet<IlTypeName>();
        foreach (var projectId in projectIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = TestNarrowingPath.Find(input.Solution, projectId, [], input.EditedFiles);
            if (TestNarrowingPreconditions.CanCompareFingerprints(input, path))
            {
                types.UnionWith(TypeChangeCollector.Collect(input.Pair, path).All);
            }
        }

        return [.. types];
    }

    private static TestProjectNarrowing NarrowProject(TestNarrowingInput input, ProjectId projectId, CancellationToken cancellationToken)
    {
        var project = input.Solution.GetProject(projectId);
        if (project is null)
        {
            return Whole(projectId, new TestNarrowingWholeReason(
                TestNarrowingWholeKind.ProjectGraphStale,
                null,
                "The test project is not in the loaded solution; reload the workspace."));
        }

        var path = TestNarrowingPath.Find(input.Solution, projectId, input.Impact.ExpandedTestProjects[projectId], input.EditedFiles);
        var searched = TestNarrowingPath.Searched(input.Solution, projectId);
        if (TestNarrowingPreconditions.Check(input, projectId, path, searched) is { } whole)
        {
            return Whole(projectId, whole);
        }

        var changes = TypeChangeCollector.Collect(input.Pair, path);
        var stages = TestClassStageBuilder.Build(input, project, changes, cancellationToken);
        var widest = stages[^1];
        var outcome = widest.Classes.Count > 0 || widest.Whole is not null
            ? TestNarrowingOutcome.Narrowed
            : changes.IsEmpty ? TestNarrowingOutcome.CodeUnchanged : TestNarrowingOutcome.ChangedTypesWithoutTests;
        return new TestProjectNarrowing(projectId, outcome, null, stages, changes.All, changes.ChangedAssemblies);
    }

    /// <summary>The test projects epoch 1 selected whole: those of the solution in its order, then any other.</summary>
    private static IEnumerable<ProjectId> Ordered(Solution solution, TestImpactResult impact) =>
        solution.ProjectIds.Where(impact.ExpandedTestProjects.ContainsKey)
            .Concat(impact.ExpandedTestProjects.Keys.Where(project => solution.GetProject(project) is null));

    private static TestNarrowingResult AllWhole(IEnumerable<ProjectId> projects, TestNarrowingWholeReason reason) =>
        new([.. projects.Select(project => Whole(project, reason))]);

    private static TestProjectNarrowing Whole(ProjectId project, TestNarrowingWholeReason reason) =>
        new(project, TestNarrowingOutcome.WholeProject, reason, [], [], []);
}
