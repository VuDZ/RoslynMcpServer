using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Services.Models;

/// <summary>
/// Physical inputs of one loaded Roslyn graph. The document index is not a complete membership map:
/// project directories stay potential regions, and categories the public workspace does not carry stay unknown.
/// Search roots are the existing workspace-plus-project set; watcher descriptors are a separate list.
/// </summary>
internal sealed class WorkspaceInputMap
{
    public const string EvidenceSource =
        "Loaded Roslyn workspace after MSBuild evaluation: documents, additional documents, "
        + "analyzer config documents, project references, metadata references, and output paths. "
        + "DocumentInfo.IsGenerated is the build-side-effect flag stored on the live document. "
        + "File globs, the Import graph, restore inputs, and custom task inputs are not on this public surface.";

    public Guid Generation { get; }

    public bool LoadGraphComplete { get; }

    public InputCoverage MultiTargetCoverage { get; }

    public InputCoverage WholeGraphCoverage { get; }

    /// <summary>
    /// True only when every required category is evidenced. A partial document index never sets this.
    /// </summary>
    public bool PublishedAsComplete { get; }

    public IReadOnlyList<string> UnknownReasons { get; }

    public IReadOnlyList<string> MetadataOnlyDependencyPaths { get; }

    public IReadOnlyList<LoadedProjectInstance> Projects { get; }

    public IReadOnlyList<InputPathEntry> Paths { get; }

    public IReadOnlyList<MembershipRegion> Regions { get; }

    public IReadOnlyList<InputWatcherDescriptor> Watchers { get; }

    public IReadOnlyList<InputCategoryEvidence> Categories { get; }

    public IReadOnlyList<string> SearchRoots { get; }

    public WorkspaceInputMap(
        Guid generation,
        bool loadGraphComplete,
        InputCoverage multiTargetCoverage,
        InputCoverage wholeGraphCoverage,
        IReadOnlyList<string> unknownReasons,
        IReadOnlyList<string> metadataOnlyDependencyPaths,
        IReadOnlyList<LoadedProjectInstance> projects,
        IReadOnlyList<InputPathEntry> paths,
        IReadOnlyList<MembershipRegion> regions,
        IReadOnlyList<InputWatcherDescriptor> watchers,
        IReadOnlyList<InputCategoryEvidence> categories,
        IReadOnlyList<string> searchRoots)
    {
        ArgumentNullException.ThrowIfNull(unknownReasons);
        ArgumentNullException.ThrowIfNull(metadataOnlyDependencyPaths);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(watchers);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(searchRoots);

        Generation = generation;
        LoadGraphComplete = loadGraphComplete;
        MultiTargetCoverage = multiTargetCoverage;
        WholeGraphCoverage = wholeGraphCoverage;
        UnknownReasons = unknownReasons;
        MetadataOnlyDependencyPaths = metadataOnlyDependencyPaths;
        Projects = projects;
        Paths = paths;
        Regions = regions;
        Watchers = watchers;
        Categories = categories;
        SearchRoots = searchRoots;
        PublishedAsComplete = loadGraphComplete
            && multiTargetCoverage == InputCoverage.Complete
            && wholeGraphCoverage == InputCoverage.Complete
            && unknownReasons.Count == 0;

        _paths = new Dictionary<string, InputPathEntry>(InputPathCanon.Comparer);
        foreach (var entry in paths)
        {
            _paths[entry.CanonicalPath] = entry;
        }
    }

    public InputPathEntry? Find(string? path)
    {
        var canonical = InputPathCanon.TryCanonicalize(path);
        if (canonical is null)
        {
            return null;
        }

        return _paths.TryGetValue(canonical, out var entry) ? entry : null;
    }

    public IReadOnlyList<LoadedProjectInstance> InstancesForFile(string? projectFilePath)
    {
        var canonical = InputPathCanon.TryCanonicalize(projectFilePath);
        if (canonical is null)
        {
            return Array.Empty<LoadedProjectInstance>();
        }

        return Projects
            .Where(project => project.FilePath is not null
                && InputPathCanon.Comparer.Equals(project.FilePath, canonical))
            .ToArray();
    }

    public InputPathConsultation Consult(string? path)
    {
        var canonical = InputPathCanon.TryCanonicalize(path);
        if (canonical is null)
        {
            return new InputPathConsultation(
                null,
                null,
                Array.Empty<MembershipRegion>(),
                provenExcluded: false,
                ignored: false,
                InputRole.None,
                externalAppearanceUnknown: false);
        }

        _paths.TryGetValue(canonical, out var known);
        var regions = Regions
            .Where(region => WorkspaceDiskPathFilter.IsPathUnderDirectory(canonical, region.Directory, InputPathCanon.Comparison))
            .ToArray();
        var externalAppearanceUnknown = known is null && IsSiblingOfExternalInput(canonical);
        var unindexedRole = InputRole.None;
        if (known is null)
        {
            if (HasIntermediateOrOutputSegment(canonical))
            {
                unindexedRole = InputRole.Unknown;
            }
            else if (regions.Length > 0)
            {
                unindexedRole = InputRole.PotentialMembership;
            }
            else if (externalAppearanceUnknown)
            {
                unindexedRole = InputRole.Unknown;
            }
        }

        return new InputPathConsultation(
            canonical,
            known,
            regions,
            provenExcluded: false,
            ignored: false,
            unindexedRole,
            externalAppearanceUnknown);
    }

    private bool IsSiblingOfExternalInput(string canonicalPath)
    {
        var directory = Path.GetDirectoryName(canonicalPath);
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        foreach (var entry in _paths.Values)
        {
            if (entry.Occurrences.All(static occurrence => occurrence.Role == InputRole.EvaluationInput))
            {
                continue;
            }

            var entryDirectory = Path.GetDirectoryName(entry.CanonicalPath);
            if (string.IsNullOrEmpty(entryDirectory)
                || !InputPathCanon.Comparer.Equals(entryDirectory, directory))
            {
                continue;
            }

            if (Regions.Any(region => WorkspaceDiskPathFilter.IsPathUnderDirectory(
                    entry.CanonicalPath,
                    region.Directory,
                    InputPathCanon.Comparison)))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool HasIntermediateOrOutputSegment(string canonicalPath)
    {
        foreach (var segment in canonicalPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private readonly Dictionary<string, InputPathEntry> _paths;
}
