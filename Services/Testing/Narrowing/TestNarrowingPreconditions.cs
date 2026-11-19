using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using RoslynMcpServer.Services.Testing.Snapshots.Models;

namespace RoslynMcpServer.Services.Testing.Narrowing;

/// <summary>
/// The conditions under which a test project selected whole by epoch 1 may be narrowed at all. Each failed
/// condition names why the project stays whole; narrowing without the data for it would lose tests silently.
/// </summary>
/// <remarks>
/// The checks go from what no build can fix to what a build fixes, so that "not rebuilt", which the answer
/// pairs with a hint to build, is named only when a build would help.
/// <para>
/// The snapshot and readability checks cover every assembly the graph search for the test project can pass
/// through, not only the path from the edit: a load by name in an assembly that does not depend on the edited
/// project still leads to the edited assembly, and an assembly missing from a graph or read without some method
/// bodies hides that route (review of task-04, F-02). Freshness is checked on the path from the edit only: an
/// assembly off it has nothing to rebuild.
/// </para>
/// </remarks>
internal static class TestNarrowingPreconditions
{
    /// <param name="path">The projects between the edited projects and the test project.</param>
    /// <param name="searched">
    /// The test project and every project it depends on, transitively: their output assemblies and those in the
    /// output directory of the test project are what the search can reach.
    /// </param>
    /// <returns>Null when the project may be narrowed.</returns>
    internal static TestNarrowingWholeReason? Check(
        TestNarrowingInput input,
        ProjectId testProject,
        IReadOnlyList<Project> path,
        IReadOnlyList<Project> searched)
    {
        if ((Selection(input, testProject) ?? OutputPaths(searched)) is { } whole)
        {
            return whole;
        }

        var outputs = SearchedOutputs(input.Pair, testProject, searched);
        return CheckAssemblies(input, path, outputs);
    }

    /// <summary>Direct filters need no reachability check, but counts still need current, readable fingerprints.</summary>
    internal static bool CanCompareFingerprints(TestNarrowingInput input, IReadOnlyList<Project> path)
    {
        if (path.Count == 0 || input.Impact.ProjectGraphStale || OutputPaths(path) is not null)
        {
            return false;
        }

        return CheckAssemblies(input, path, [.. path.Select(project => project.OutputFilePath!)]) is null;
    }

    private static TestNarrowingWholeReason? CheckAssemblies(
        TestNarrowingInput input,
        IReadOnlyList<Project> path,
        IReadOnlyList<string> outputs) =>
        BeforeSnapshot(input.Pair, outputs)
            ?? CurrentSnapshot(input.Pair, outputs)
            ?? MethodBodies(input.Pair, outputs)
            ?? EditedFiles(input, path)
            ?? DependentAssemblies(input, path);

    /// <summary>Only a C# edit can be narrowed by compiled code; any other reason of epoch 1 keeps the project whole.</summary>
    private static TestNarrowingWholeReason? Selection(TestNarrowingInput input, ProjectId testProject)
    {
        foreach (var reason in input.Impact.ExpandedTestProjects[testProject])
        {
            TestNarrowingWholeKind? kind = reason.Kind switch
            {
                ProjectExpansionKind.CSharpSource when reason.Owner is not null => null,
                ProjectExpansionKind.ProjectFile => TestNarrowingWholeKind.ProjectFile,
                ProjectExpansionKind.ProjectGraphFile => TestNarrowingWholeKind.ProjectGraphStale,
                ProjectExpansionKind.ObservationGap => TestNarrowingWholeKind.ObservationGap,
                _ => TestNarrowingWholeKind.NonCSharpInput,
            };
            if (kind is { } whole)
            {
                return new TestNarrowingWholeReason(whole, reason.Path, reason.Text);
            }
        }

        // The path from the edit to the test project is read from the project graph.
        return input.Impact.ProjectGraphStale
            ? new TestNarrowingWholeReason(
                TestNarrowingWholeKind.ProjectGraphStale,
                null,
                "The project graph is stale; reload the workspace to narrow by assemblies.")
            : null;
    }

    private static TestNarrowingWholeReason? OutputPaths(IReadOnlyList<Project> searched)
    {
        var project = searched.FirstOrDefault(project => string.IsNullOrEmpty(project.OutputFilePath));
        return project is null
            ? null
            : new TestNarrowingWholeReason(
                TestNarrowingWholeKind.NoBeforeSnapshot,
                project.FilePath ?? project.Name,
                $"Project {project.Name} has no output assembly path, so it has no snapshot before the edit.");
    }

    /// <summary>
    /// Output assemblies of the searched projects, then the solution assemblies next to the test assembly
    /// (owner decision M-06), without repeats.
    /// </summary>
    private static IReadOnlyList<string> SearchedOutputs(AssemblySnapshotPair pair, ProjectId testProject, IReadOnlyList<Project> searched)
    {
        var testOutput = searched.First(project => project.Id == testProject).OutputFilePath!;
        var neighbors = pair.Neighbors.NeighborsByTestOutputPath.GetValueOrDefault(testOutput) ?? [];
        return [.. searched.Select(project => project.OutputFilePath!).Concat(neighbors).Distinct(IlPaths.Comparer)];
    }

    private static TestNarrowingWholeReason? BeforeSnapshot(AssemblySnapshotPair pair, IReadOnlyList<string> outputs)
    {
        foreach (var output in outputs)
        {
            var entry = pair.Before.Find(output);
            var reason = entry?.State switch
            {
                null => new TestNarrowingWholeReason(
                    TestNarrowingWholeKind.NoBeforeSnapshot,
                    output,
                    $"No snapshot before the edit for {output}: its project was not loaded when the baseline was published."),
                AssemblySnapshotState.Missing => new TestNarrowingWholeReason(
                    TestNarrowingWholeKind.NoBeforeSnapshot,
                    output,
                    $"No snapshot before the edit for {output}: the assembly was not on disk when the solution was loaded."),
                AssemblySnapshotState.Unknown => new TestNarrowingWholeReason(
                    TestNarrowingWholeKind.BeforeSnapshotUnknown,
                    output,
                    $"The state of {output} before the edit is unknown: {entry.Reason}"),
                AssemblySnapshotState.Unreadable => new TestNarrowingWholeReason(
                    TestNarrowingWholeKind.AssemblyUnreadable,
                    output,
                    $"{output} could not be read when the solution was loaded: {entry.Reason}"),
                _ => null,
            };
            if (reason is not null)
            {
                return reason;
            }
        }

        return null;
    }

    private static TestNarrowingWholeReason? CurrentSnapshot(AssemblySnapshotPair pair, IReadOnlyList<string> outputs)
    {
        foreach (var output in outputs)
        {
            var entry = pair.Current.Find(output);
            var reason = entry?.State switch
            {
                AssemblySnapshotState.Read => null,
                AssemblySnapshotState.Unreadable => new TestNarrowingWholeReason(
                    TestNarrowingWholeKind.AssemblyUnreadable,
                    output,
                    $"{output} could not be read: {entry.Reason}"),
                _ => new TestNarrowingWholeReason(
                    TestNarrowingWholeKind.NotRebuilt,
                    output,
                    $"{output} does not exist"),
            };
            if (reason is not null)
            {
                return reason;
            }
        }

        return null;
    }

    /// <summary>Edges from a method body that could not be read are missing from the graph (review of task-01).</summary>
    private static TestNarrowingWholeReason? MethodBodies(AssemblySnapshotPair pair, IReadOnlyList<string> outputs)
    {
        foreach (var output in outputs)
        {
            var unread = Math.Max(pair.Before.Find(output)!.Facts!.UnreadableBodies, pair.Current.Find(output)!.Facts!.UnreadableBodies);
            if (unread > 0)
            {
                return new TestNarrowingWholeReason(
                    TestNarrowingWholeKind.UnreadableMethodBodies,
                    output,
                    $"{unread} method bodies of {output} could not be read; their dependencies are missing from the graph.");
            }
        }

        return null;
    }

    /// <summary>
    /// The assembly of a project with edited C# documents must be newer than the last of them (owner decision
    /// M-04). Equal fingerprints do not prove a build: after "edit, build, edit again" they differ from the
    /// snapshot before the edit, and the assembly still lacks the second edit.
    /// </summary>
    private static TestNarrowingWholeReason? EditedFiles(TestNarrowingInput input, IReadOnlyList<Project> path)
    {
        foreach (var project in path)
        {
            var files = input.EditedFiles.Where(file => file.ProjectId == project.Id).ToArray();
            if (files.Length == 0)
            {
                continue;
            }

            if (files.FirstOrDefault(file => file.LastWriteTimeUtc is null) is { } deleted)
            {
                return new TestNarrowingWholeReason(
                    TestNarrowingWholeKind.EditedDocumentMissing,
                    deleted.Path,
                    $"The edited document {deleted.Path} no longer exists, so the time of the last edit is unknown.");
            }

            var last = files.MaxBy(file => file.LastWriteTimeUtc!.Value)!;
            var output = project.OutputFilePath!;
            if (OutputTime(input.Pair, output) <= last.LastWriteTimeUtc!.Value)
            {
                return new TestNarrowingWholeReason(
                    TestNarrowingWholeKind.NotRebuilt,
                    output,
                    $"{output} is not newer than the last edit of {last.Path}");
            }
        }

        return null;
    }

    /// <summary>
    /// A project on the path must be newer than the file MSBuild compares to decide whether to recompile it
    /// after a dependency changed: the reference assembly of the dependency, or its output assembly when it
    /// makes none. A body edit leaves a reference assembly as it was, so building the dependency alone is
    /// enough; without a reference assembly MSBuild recompiles the dependents, and until it has, their code
    /// may still hold an inlined constant or a call to an old overload
    /// (docs/rapid-tia-2/evidence/il-build-diff-self-2026-11-15.md).
    /// </summary>
    /// <remarks>
    /// Every dependency on the path is compared, not only direct references: an SDK project compiles against the
    /// transitive references too, and comparing more can only keep a project whole, never lose a test.
    /// </remarks>
    private static TestNarrowingWholeReason? DependentAssemblies(TestNarrowingInput input, IReadOnlyList<Project> path)
    {
        var graph = input.Solution.GetProjectDependencyGraph();
        foreach (var dependent in path)
        {
            var dependentTime = OutputTime(input.Pair, dependent.OutputFilePath!);
            var dependencies = graph.GetProjectsThatThisProjectTransitivelyDependsOn(dependent.Id);
            foreach (var dependency in path.Where(project => dependencies.Contains(project.Id)))
            {
                DateTime shapeTime;
                string shapeFile;
                if (string.IsNullOrEmpty(dependency.OutputRefFilePath))
                {
                    shapeFile = dependency.OutputFilePath!;
                    shapeTime = OutputTime(input.Pair, shapeFile);
                }
                else if (input.ReferenceAssemblyTimesUtc.TryGetValue(dependency.OutputRefFilePath, out var referenceTime))
                {
                    shapeFile = dependency.OutputRefFilePath;
                    shapeTime = referenceTime;
                }
                else
                {
                    return new TestNarrowingWholeReason(
                        TestNarrowingWholeKind.ReferenceAssemblyMissing,
                        dependency.OutputRefFilePath,
                        $"The reference assembly {dependency.OutputRefFilePath} of {dependency.Name} does not exist");
                }

                if (shapeTime > dependentTime)
                {
                    return new TestNarrowingWholeReason(
                        TestNarrowingWholeKind.DependentNotRebuilt,
                        dependent.OutputFilePath,
                        $"{dependent.OutputFilePath} is older than {shapeFile}");
                }
            }
        }

        return null;
    }

    /// <summary>The stamp of the content that was compared, not a second look at the file.</summary>
    private static DateTime OutputTime(AssemblySnapshotPair pair, string output) => pair.Current.Find(output)!.Stamp!.Value.LastWriteTimeUtc;
}
