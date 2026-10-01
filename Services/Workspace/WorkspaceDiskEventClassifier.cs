using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Models;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Classifies one watcher path against the evaluated input map.
/// Absence from the reverse index is not a Compile Remove. A new file under a project directory
/// stays a potential glob item until a later MSBuild evaluation. Generated and output events of
/// the producing project are not user-input edits. Unknown provenance is not dropped as generated.
/// </summary>
internal static class WorkspaceDiskEventClassifier
{
    public static DiskEventDecision Classify(string fullPath, WorkspaceInputMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var canonical = InputPathCanon.TryCanonicalize(fullPath);
        if (canonical is null)
        {
            return DiskEventDecision.Irrelevant(fullPath);
        }

        if (WorkspaceDiskPathFilter.IsProjectGraphFile(canonical))
        {
            return ClassifyGraphFile(canonical, map);
        }

        var consultation = map.Consult(canonical);
        if (consultation.Known is { } known)
        {
            return ClassifyKnown(canonical, known);
        }

        return ClassifyUnindexed(canonical, consultation);
    }

    public static bool SyncsUserText(WorkspaceInputMap? map, string fullPath, DocumentId documentId)
    {
        if (map?.Find(fullPath) is not { } entry)
        {
            return true;
        }

        var roles = entry.Occurrences
            .Where(occurrence => occurrence.DocumentId == documentId)
            .Select(occurrence => occurrence.Role)
            .ToArray();
        if (roles.Length == 0)
        {
            return true;
        }

        return roles.Contains(InputRole.UserInput);
    }

    /// <summary>
    /// True when <paramref name="fullPath"/> is an explicit input that must not be dropped just
    /// because a path segment is <c>obj</c>, <c>bin</c>, or <c>artifacts</c>.
    /// </summary>
    public static bool IsAddressedUserInput(WorkspaceInputMap? map, string fullPath)
    {
        return map?.Find(fullPath)?.Occurrences.Any(static occurrence => occurrence.Role == InputRole.UserInput) == true;
    }

    /// <summary>
    /// Directory rename evidence. <see cref="Directory.Exists"/> on the old path is not required:
    /// a moved or deleted directory inside a membership region may already be gone and may have
    /// held glob items that were never in the document index.
    /// </summary>
    public static bool IsDirectoryRename(string? oldPath, string? newPath, WorkspaceInputMap? map)
    {
        return LooksLikeDirectory(oldPath, map) || LooksLikeDirectory(newPath, map);
    }

    /// <summary>
    /// The path is a membership region, lies inside one, or is an ancestor of one.
    /// </summary>
    public static bool AffectsMembershipRegion(string? rawPath, WorkspaceInputMap? map)
    {
        var fullPath = InputPathCanon.TryCanonicalize(rawPath);
        if (fullPath is null || map is null)
        {
            return false;
        }

        foreach (var region in map.Regions)
        {
            if (InputPathCanon.Comparer.Equals(fullPath, region.Directory)
                || WorkspaceDiskPathFilter.IsPathUnderDirectory(fullPath, region.Directory, InputPathCanon.Comparison)
                || WorkspaceDiskPathFilter.IsPathUnderDirectory(region.Directory, fullPath, InputPathCanon.Comparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Ignored-directory events still reach classification when the map has a role for them or
    /// when the path is an unindexed intermediate (unknown provenance, not a silent drop).
    /// </summary>
    public static bool ShouldClassifyIgnoredPath(WorkspaceInputMap map, string fullPath)
    {
        if (map.Find(fullPath) is not null)
        {
            return true;
        }

        var consultation = map.Consult(fullPath);
        return consultation.ExternalAppearanceUnknown
            || consultation.UnindexedRole == InputRole.Unknown;
    }

    private static DiskEventDecision ClassifyGraphFile(string canonical, WorkspaceInputMap map)
    {
        var known = map.Find(canonical);
        if (known is null && !map.Regions.Any(region =>
                WorkspaceDiskPathFilter.IsPathUnderDirectory(canonical, region.Directory, InputPathCanon.Comparison)))
        {
            // Point watchers also deliver neighboring files. A foreign solution in a shared ancestor
            // (for example, %TEMP%) is not an input of this graph just because its extension matches.
            // Unindexed imports are still unknown; this does not establish their irrelevance.
            // See docs/archive/project-input-watching/epoch-2-watchers-and-shared-sync.md.
            return new DiskEventDecision(
                canonical,
                provenIrrelevant: false,
                dirtyUserSource: false,
                compositionStale: false,
                graphFile: false,
                new[]
                {
                    new InputPathNotice(canonical, InputRole.Unknown, ProjectId: null, CountsAsInputRevision: false),
                });
        }

        var notices = known is null
            ? new List<InputPathNotice>
            {
                new(canonical, InputRole.EvaluationInput, ProjectId: null, CountsAsInputRevision: true),
            }
            : NoticesFor(canonical, known.Occurrences);
        return new DiskEventDecision(
            canonical,
            provenIrrelevant: false,
            dirtyUserSource: false,
            compositionStale: false,
            graphFile: true,
            notices);
    }

    private static DiskEventDecision ClassifyKnown(string canonical, InputPathEntry known)
    {
        var missing = !File.Exists(canonical);
        var notices = NoticesFor(canonical, known.Occurrences);
        var dirty = known.Occurrences.Any(static occurrence => occurrence.Role == InputRole.UserInput)
            && WorkspaceDiskPathFilter.IsCSharpSource(canonical);
        var composition = missing
            && known.Occurrences.Any(static occurrence => occurrence.Role == InputRole.UserInput);
        return new DiskEventDecision(
            canonical,
            provenIrrelevant: false,
            dirty,
            composition,
            graphFile: false,
            notices);
    }

    private static DiskEventDecision ClassifyUnindexed(string canonical, InputPathConsultation consultation)
    {
        if (consultation.ProvenExcluded)
        {
            return DiskEventDecision.Irrelevant(canonical);
        }

        if (consultation.UnindexedRole == InputRole.PotentialMembership)
        {
            var notices = consultation.PotentialRegions
                .Select(region => new InputPathNotice(
                    canonical,
                    InputRole.PotentialMembership,
                    region.Owner,
                    CountsAsInputRevision: false))
                .ToArray();
            var dirty = WorkspaceDiskPathFilter.IsCSharpSource(canonical);
            return new DiskEventDecision(
                canonical,
                provenIrrelevant: false,
                dirtyUserSource: dirty,
                compositionStale: true,
                graphFile: false,
                notices);
        }

        if (consultation.ExternalAppearanceUnknown || consultation.UnindexedRole == InputRole.Unknown)
        {
            return new DiskEventDecision(
                canonical,
                provenIrrelevant: false,
                dirtyUserSource: false,
                compositionStale: false,
                graphFile: false,
                new[]
                {
                    new InputPathNotice(canonical, InputRole.Unknown, ProjectId: null, CountsAsInputRevision: false),
                });
        }

        return DiskEventDecision.Irrelevant(canonical);
    }

    private static List<InputPathNotice> NoticesFor(
        string canonical,
        IReadOnlyList<InputOccurrence> occurrences)
    {
        var notices = new List<InputPathNotice>(occurrences.Count);
        foreach (var occurrence in occurrences)
        {
            var counts = occurrence.Role is InputRole.UserInput
                or InputRole.AdditionalFile
                or InputRole.AnalyzerConfig
                or InputRole.EvaluationInput
                or InputRole.MetadataDependency;
            var projectId = occurrence.Role is InputRole.Generated or InputRole.Output
                ? occurrence.ProducerProjectId ?? occurrence.ProjectId
                : occurrence.ProjectId;
            notices.Add(new InputPathNotice(canonical, occurrence.Role, projectId, counts));
        }

        return notices;
    }

    private static bool LooksLikeDirectory(string? rawPath, WorkspaceInputMap? map)
    {
        var fullPath = InputPathCanon.TryCanonicalize(rawPath);
        if (fullPath is null)
        {
            return false;
        }

        if (Directory.Exists(fullPath))
        {
            return true;
        }

        if (map is null || Path.HasExtension(fullPath))
        {
            return false;
        }

        if (AffectsMembershipRegion(fullPath, map))
        {
            return true;
        }

        foreach (var entry in map.Paths)
        {
            if (WorkspaceDiskPathFilter.IsPathUnderDirectory(entry.CanonicalPath, fullPath, InputPathCanon.Comparison))
            {
                return true;
            }
        }

        return false;
    }
}
