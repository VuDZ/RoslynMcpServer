using System.Collections.Immutable;

namespace RoslynMcpServer.Services.Testing.Baseline.Models;

/// <summary>After a watcher gap, RequiresFullScan makes TouchedPaths include original/current C# rescan candidates.</summary>
internal sealed record BaselineSessionSnapshot(
    string? WorkspacePath,
    long Generation,
    DateTime PublishedAtUtc,
    ImmutableArray<BaselineDocumentState> Documents,
    ImmutableHashSet<string> TouchedPaths,
    bool RequiresFullScan);
