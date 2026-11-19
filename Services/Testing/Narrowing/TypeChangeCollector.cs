using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using RoslynMcpServer.Services.Testing.Snapshots.Models;

namespace RoslynMcpServer.Services.Testing.Narrowing;

/// <summary>Compares the fingerprints of the assemblies on a path between the two snapshots.</summary>
internal static class TypeChangeCollector
{
    /// <summary>
    /// Every project on <paramref name="path"/> must have read entries in both snapshots: the preconditions are
    /// checked before.
    /// </summary>
    internal static TypeChangeSet Collect(AssemblySnapshotPair pair, IReadOnlyList<Project> path)
    {
        var changed = new HashSet<IlTypeName>();
        var added = new HashSet<IlTypeName>();
        var removed = new HashSet<IlTypeName>();
        var assemblies = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var output in path.Select(project => project.OutputFilePath!).Distinct(IlPaths.Comparer))
        {
            var before = pair.Before.Find(output)!.Fingerprints!;
            var current = pair.Current.Find(output)!.Fingerprints!;

            // A renamed assembly is a project file edit, which keeps the project whole before this point; if it
            // ever gets here, every type counts as changed rather than the comparison failing.
            var sameAssembly = string.Equals(before.Assembly, current.Assembly, StringComparison.Ordinal);
            var changes = sameAssembly ? before.Compare(current) : null;
            if (changes is null || changes.AssemblyChanged)
            {
                // A resource or an assembly attribute belongs to no type, and any type may read it.
                changed.UnionWith(before.Types.Keys);
                changed.UnionWith(current.Types.Keys);
                assemblies.Add(current.Assembly);
                continue;
            }

            changed.UnionWith(changes.Changed);
            added.UnionWith(changes.Added);
            removed.UnionWith(changes.Removed);
        }

        // A type added in one target framework and changed in another is changed: look for it in both graphs.
        added.ExceptWith(changed);
        removed.ExceptWith(changed);
        return new TypeChangeSet(changed, added, removed, [.. assemblies]);
    }
}
