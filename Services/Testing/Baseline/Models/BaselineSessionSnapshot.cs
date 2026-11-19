using System.Collections.Immutable;

namespace RoslynMcpServer.Services.Testing.Baseline.Models;

/// <summary>After reload or a watcher gap, RequiresFullScan includes original/current C# rescan candidates.</summary>
internal sealed record BaselineSessionSnapshot(
    string? WorkspacePath,
    long Generation,
    DateTime PublishedAtUtc,
    ImmutableArray<BaselineDocumentState> Documents,
    ImmutableHashSet<string> TouchedPaths,
    bool RequiresFullScan)
{
    /// <summary>A sticky watcher/capture failure, distinct from the ordinary reload observation window.</summary>
    internal bool HasObservationGap { get; init; }

    /// <summary>Paths that have belonged to a loaded C# document, including documents added by reload.</summary>
    internal ImmutableHashSet<string> LoadedSourcePaths { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>Known non-source paths from the first loaded solution, preserved across same-solution reloads.</summary>
    internal ImmutableHashSet<string> OriginalNonSourcePaths { get; init; } = ImmutableHashSet<string>.Empty;
}
