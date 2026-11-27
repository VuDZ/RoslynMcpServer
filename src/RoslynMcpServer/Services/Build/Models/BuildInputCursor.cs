using RoslynMcpServer.Services.Models;

namespace RoslynMcpServer.Services.Build;

/// <summary>
/// Cursor copied from one pull. It records generation, membership, per-path input
/// revisions, coverage, and pending state. <see cref="InputContentHashes"/> stays
/// empty: the shared snapshot has no input-byte hash, and a revision match is not
/// a content match. Output identity is stored on the proof, not here, because the
/// caller supplies it.
/// </summary>
internal sealed class BuildInputCursor
{
    public Guid Generation { get; }

    public int MembershipRevision { get; }

    public int SnapshotRevision { get; }

    public InputCoverage Coverage { get; }

    public bool Pending { get; }

    public IReadOnlyList<BuildPathRevision> PathRevisions { get; }

    public IReadOnlyList<string> InputContentHashes { get; }

    public BuildInputCursor(
        Guid generation,
        int membershipRevision,
        int snapshotRevision,
        InputCoverage coverage,
        bool pending,
        IReadOnlyList<BuildPathRevision> pathRevisions)
    {
        ArgumentNullException.ThrowIfNull(pathRevisions);
        Generation = generation;
        MembershipRevision = membershipRevision;
        SnapshotRevision = snapshotRevision;
        Coverage = coverage;
        Pending = pending;
        PathRevisions = pathRevisions;
        InputContentHashes = Array.Empty<string>();
    }
}
