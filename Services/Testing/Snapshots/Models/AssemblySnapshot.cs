using System.Collections.Immutable;
using RoslynMcpServer.Services.Testing.Il;

namespace RoslynMcpServer.Services.Testing.Snapshots.Models;

/// <summary>The output assemblies of a solution at one moment, in the order of the projects.</summary>
internal sealed class AssemblySnapshot
{
    public ImmutableArray<AssemblySnapshotEntry> Entries { get; }

    public AssemblySnapshot(IEnumerable<AssemblySnapshotEntry> entries)
    {
        Entries = [.. entries];
        _byPath = Entries.ToImmutableDictionary(entry => entry.Path, IlPaths.Comparer);
    }

    /// <summary>The entry of an output path, or null when the snapshot has none for it.</summary>
    public AssemblySnapshotEntry? Find(string path) => _byPath.GetValueOrDefault(path);

    private readonly ImmutableDictionary<string, AssemblySnapshotEntry> _byPath;
}
