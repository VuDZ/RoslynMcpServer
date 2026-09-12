using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynMcpServer.Services;

internal sealed record AnalyzerShadowReferenceEntry(
    ProjectId ProjectId,
    string ProjectName,
    string AnalyzerDisplay,
    string? OriginalFullPath,
    string MatchedProjectName,
    string? ShadowCopyPath,
    string? GenerationId,
    bool Applied,
    string? SkipReason,
    bool StaleGeneration);

/// <summary>
/// Immutable in-memory original↔shadow mapping for one load session. Applying it never reads analyzer files.
/// </summary>
internal sealed class AnalyzerShadowMapping
{
    public AnalyzerShadowMapping(
        Guid sessionId,
        string? loadedPath,
        IReadOnlyList<AnalyzerShadowReferenceEntry> entries)
    {
        SessionId = sessionId;
        LoadedPath = loadedPath;
        Entries = entries;
    }

    public Guid SessionId { get; }

    public string? LoadedPath { get; }

    public IReadOnlyList<AnalyzerShadowReferenceEntry> Entries { get; }

    public bool HasAnyApplied => Entries.Any(e => e.Applied && !string.IsNullOrWhiteSpace(e.ShadowCopyPath));

    public AnalyzerShadowMapping WithStale(string reason)
    {
        var updated = Entries
            .Select(e => e.Applied
                ? e with { StaleGeneration = true, SkipReason = "stale-generation: " + reason }
                : e)
            .ToList();
        return new AnalyzerShadowMapping(SessionId, LoadedPath, updated);
    }

    public Solution Apply(Solution solution, IAnalyzerAssemblyLoader loader)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(loader);

        foreach (var projectId in solution.ProjectIds.ToList())
        {
            var project = solution.GetProject(projectId);
            if (project is null)
            {
                continue;
            }

            var projectEntries = Entries.Where(e => e.ProjectId == projectId).ToList();
            if (projectEntries.Count == 0)
            {
                continue;
            }

            var changed = false;
            var newReferences = new List<AnalyzerReference>(project.AnalyzerReferences.Count);
            foreach (var reference in project.AnalyzerReferences)
            {
                var entry = FindEntry(projectEntries, reference);
                if (entry is { Applied: true, ShadowCopyPath: not null })
                {
                    newReferences.Add(new AnalyzerFileReference(entry.ShadowCopyPath, loader));
                    changed = true;
                }
                else
                {
                    newReferences.Add(reference);
                }
            }

            if (changed)
            {
                solution = solution.WithProjectAnalyzerReferences(projectId, newReferences);
            }
        }

        return solution;
    }

    /// <summary>
    /// Replaces only known shadow references from this mapping with their originals.
    /// Unrelated references, order, and multiplicity are preserved. A removed project
    /// is not recreated. Does not wipe the analyzer list to the workspace copy.
    /// </summary>
    public Solution InvertKnownReplacements(
        Solution candidate,
        Solution? workspaceCurrent,
        IAnalyzerAssemblyLoader loader)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(loader);

        foreach (var projectId in candidate.ProjectIds.ToList())
        {
            var project = candidate.GetProject(projectId);
            if (project is null)
            {
                continue;
            }

            var projectEntries = Entries.Where(e => e.ProjectId == projectId && e.Applied).ToList();
            if (projectEntries.Count == 0)
            {
                continue;
            }

            var workspaceProject = workspaceCurrent?.GetProject(projectId);
            var changed = false;
            var newReferences = new List<AnalyzerReference>(project.AnalyzerReferences.Count);
            foreach (var reference in project.AnalyzerReferences)
            {
                var entry = FindEntryByShadowPath(projectEntries, reference);
                if (entry is { OriginalFullPath: not null })
                {
                    newReferences.Add(RestoreOriginal(entry, workspaceProject, loader));
                    changed = true;
                }
                else
                {
                    newReferences.Add(reference);
                }
            }

            if (changed)
            {
                candidate = candidate.WithProjectAnalyzerReferences(projectId, newReferences);
            }
        }

        return candidate;
    }

    internal static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, comparison);
        }
    }

    private static AnalyzerReference RestoreOriginal(
        AnalyzerShadowReferenceEntry entry,
        Project? workspaceProject,
        IAnalyzerAssemblyLoader loader)
    {
        if (workspaceProject is not null)
        {
            foreach (var original in workspaceProject.AnalyzerReferences)
            {
                if (original.FullPath is not null
                    && entry.OriginalFullPath is not null
                    && PathsEqual(original.FullPath, entry.OriginalFullPath))
                {
                    return original;
                }
            }
        }

        return new AnalyzerFileReference(entry.OriginalFullPath!, loader);
    }

    private static AnalyzerShadowReferenceEntry? FindEntryByShadowPath(
        IReadOnlyList<AnalyzerShadowReferenceEntry> projectEntries,
        AnalyzerReference reference)
    {
        if (string.IsNullOrWhiteSpace(reference.FullPath))
        {
            return null;
        }

        foreach (var entry in projectEntries)
        {
            if (!string.IsNullOrWhiteSpace(entry.ShadowCopyPath)
                && PathsEqual(entry.ShadowCopyPath, reference.FullPath))
            {
                return entry;
            }
        }

        return null;
    }

    private static AnalyzerShadowReferenceEntry? FindEntry(
        IReadOnlyList<AnalyzerShadowReferenceEntry> projectEntries,
        AnalyzerReference reference)
    {
        foreach (var entry in projectEntries)
        {
            if (!string.IsNullOrWhiteSpace(entry.OriginalFullPath)
                && !string.IsNullOrWhiteSpace(reference.FullPath)
                && PathsEqual(entry.OriginalFullPath, reference.FullPath))
            {
                return entry;
            }
        }

        return null;
    }
}
