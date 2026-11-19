using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Services.Testing.Narrowing.Models;

/// <summary>
/// Top-level types whose compiled code differs between the two snapshots, and where the dependents of each
/// are looked for.
/// </summary>
/// <param name="Changed">
/// In both snapshots with different fingerprints, and every type of an assembly whose own fingerprint changed.
/// Dependents are looked for in both graphs: the edit may have removed the very dependency, such as an
/// interface a scan found the type by.
/// </param>
/// <param name="Added">Only in the current snapshot: dependents are in the current graph.</param>
/// <param name="Removed">Only in the snapshot before the edit: dependents are in the graph before the edit.</param>
/// <param name="ChangedAssemblies">Assemblies whose own fingerprint (resources, attributes) changed.</param>
internal sealed record TypeChangeSet(
    IReadOnlySet<IlTypeName> Changed,
    IReadOnlySet<IlTypeName> Added,
    IReadOnlySet<IlTypeName> Removed,
    IReadOnlyList<string> ChangedAssemblies)
{
    internal bool IsEmpty => Changed.Count == 0 && Added.Count == 0 && Removed.Count == 0 && ChangedAssemblies.Count == 0;

    /// <summary>The types to look for in the current graph.</summary>
    internal IReadOnlyCollection<IlTypeName> InCurrentGraph => [.. Changed.Concat(Added)];

    /// <summary>The types to look for in the graph before the edit.</summary>
    internal IReadOnlyCollection<IlTypeName> InBeforeGraph => [.. Changed.Concat(Removed)];

    /// <summary>Every changed, added and removed type, ordered by assembly and name.</summary>
    internal IReadOnlyList<IlTypeName> All =>
    [
        .. Changed.Concat(Added).Concat(Removed)
            .Distinct()
            .OrderBy(type => type.Assembly, StringComparer.OrdinalIgnoreCase)
            .ThenBy(type => type.Name, StringComparer.Ordinal),
    ];
}
