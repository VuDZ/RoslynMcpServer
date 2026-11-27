namespace RoslynMcpServer.Services.Models;

/// <summary>
/// Immutable pull of one watcher generation. Reading it does not consume events.
/// It is not an atomic filesystem snapshot: a missed watcher callback is not proven by silence.
/// </summary>
internal sealed class WorkspaceInputSnapshot
{
    public Guid Generation { get; }

    public int SnapshotRevision { get; }

    public int MembershipRevision { get; }

    public IReadOnlyList<string> LoadedProjectPaths { get; }

    public IReadOnlyList<LoadedProjectInstance> Projects { get; }

    public IReadOnlyList<MembershipRegion> Regions { get; }

    public IReadOnlyList<InputPathSnapshot> Paths { get; }

    public InputCoverage Coverage { get; }

    public IReadOnlyList<InputCoverageGap> Gaps { get; }

    public string? EvidenceSource { get; }

    public bool HasPendingContent { get; }

    public WorkspaceInputSnapshot(
        Guid generation,
        int snapshotRevision,
        int membershipRevision,
        IReadOnlyList<string> loadedProjectPaths,
        IReadOnlyList<LoadedProjectInstance> projects,
        IReadOnlyList<MembershipRegion> regions,
        IReadOnlyList<InputPathSnapshot> paths,
        InputCoverage coverage,
        IReadOnlyList<InputCoverageGap> gaps,
        string? evidenceSource,
        bool hasPendingContent)
    {
        ArgumentNullException.ThrowIfNull(loadedProjectPaths);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(gaps);
        Generation = generation;
        SnapshotRevision = snapshotRevision;
        MembershipRevision = membershipRevision;
        LoadedProjectPaths = loadedProjectPaths;
        Projects = projects;
        Regions = regions;
        Paths = paths;
        Coverage = coverage;
        Gaps = gaps;
        EvidenceSource = evidenceSource;
        HasPendingContent = hasPendingContent;
    }
}
