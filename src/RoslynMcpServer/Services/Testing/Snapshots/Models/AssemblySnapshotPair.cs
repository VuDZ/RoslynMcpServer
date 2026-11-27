using RoslynMcpServer.Services.Testing.Il.Models;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Testing.Snapshots.Models;

/// <summary>
/// The snapshot before the edit and the current one, taken together, with the graph of each. Both graphs use
/// one neighbor map, listed when the current snapshot was built.
/// </summary>
/// <param name="WorkspacePath">The solution or project path of the baseline.</param>
/// <param name="PublishedAtUtc">When the baseline was published: the moment the snapshot before the edit stands for.</param>
/// <param name="Before">
/// Output assemblies of the projects loaded when the baseline was published. A project that appeared after
/// a reload has no entry here.
/// </param>
/// <param name="Current">Output assemblies of the projects given to the current request.</param>
/// <param name="BeforeGraph">
/// Built from <see cref="Before"/>. An <see cref="AssemblySnapshotState.Unknown"/> assembly is not in it;
/// its state stays visible in <see cref="Before"/>.
/// </param>
internal sealed record AssemblySnapshotPair(
    string WorkspacePath,
    DateTime PublishedAtUtc,
    AssemblySnapshot Before,
    AssemblySnapshot Current,
    IlNeighborMap Neighbors,
    IlGraphBuildResult BeforeGraph,
    IlGraphBuildResult CurrentGraph)
{
    /// <summary>
    /// True when the pair was made for the baseline named by its solution path and publication moment, as a
    /// baseline session snapshot or an epoch 1 estimate names it. A reload of the same solution keeps both
    /// values; a new baseline changes them.
    /// </summary>
    internal bool BelongsTo(string? workspacePath, DateTime publishedAtUtc) =>
        workspacePath is not null
        && InputPathCanon.Comparer.Equals(WorkspacePath, workspacePath)
        && PublishedAtUtc == publishedAtUtc;
}
