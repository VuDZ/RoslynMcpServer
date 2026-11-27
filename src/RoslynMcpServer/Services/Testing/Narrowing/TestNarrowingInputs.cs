using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using RoslynMcpServer.Services.Testing.Snapshots.Models;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Testing.Narrowing;

/// <summary>Takes what narrowing needs from the epoch 1 estimate and from the disk, once per request.</summary>
internal static class TestNarrowingInputs
{
    internal static TestNarrowingInput Collect(Solution solution, TestImpactEstimate estimate, AssemblySnapshotPair pair)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentNullException.ThrowIfNull(pair);
        return new TestNarrowingInput(
            solution,
            estimate.Impact,
            estimate.Classes,
            EditedFiles(estimate),
            ReferenceAssemblyTimes(solution),
            pair);
    }

    /// <summary>
    /// Every C# document occurrence that changed since load, whether epoch 1 narrowed the edit itself or not,
    /// and every C# path behind a C# reason, even one no document stands for any more (a deleted file).
    /// </summary>
    private static EditedSourceFile[] EditedFiles(TestImpactEstimate estimate)
    {
        var changed = estimate.ChangedPaths.ToHashSet(InputPathCanon.Comparer);
        var documents = estimate.Documents
            .Where(document => WorkspaceDiskPathFilter.IsCSharpSource(document.Path) && changed.Contains(document.Path))
            .Select(document => (document.ProjectId, document.Path));
        var reasons = estimate.Impact.ExpandedTestProjects.Values
            .SelectMany(reasons => reasons)
            .Where(reason => reason.Kind == ProjectExpansionKind.CSharpSource && reason.Owner is not null && reason.Path is not null)
            .Select(reason => (ProjectId: reason.Owner!, Path: reason.Path!));
        var seen = new Dictionary<ProjectId, HashSet<string>>();
        var files = new List<EditedSourceFile>();
        foreach (var (project, path) in documents.Concat(reasons))
        {
            if (!seen.TryGetValue(project, out var paths))
            {
                paths = new HashSet<string>(InputPathCanon.Comparer);
                seen.Add(project, paths);
            }

            if (paths.Add(InputPathCanon.TryCanonicalize(path) ?? path))
            {
                files.Add(new EditedSourceFile(project, path, LastWriteTimeUtc(path)));
            }
        }

        return [.. files];
    }

    private static Dictionary<string, DateTime> ReferenceAssemblyTimes(Solution solution)
    {
        var times = new Dictionary<string, DateTime>(IlPaths.Comparer);
        foreach (var path in solution.Projects.Select(project => project.OutputRefFilePath).OfType<string>().Where(path => path.Length > 0))
        {
            if (LastWriteTimeUtc(path) is { } time)
            {
                times[path] = time;
            }
        }

        return times;
    }

    /// <summary>Null when the file does not exist or cannot be looked at: the caller treats both as unknown.</summary>
    private static DateTime? LastWriteTimeUtc(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.LastWriteTimeUtc : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
