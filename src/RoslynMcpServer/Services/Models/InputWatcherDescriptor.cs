namespace RoslynMcpServer.Services.Models;

/// <summary>
/// Planned watcher. This is not a search root and it does not start a <see cref="FileSystemWatcher"/>.
/// Non-recursive descriptors list point paths; they are not merged into a recursive ancestor watch.
/// </summary>
internal sealed record InputWatcherDescriptor(
    string Directory,
    bool IncludeSubdirectories,
    IReadOnlyList<string> PointFilters,
    IReadOnlyList<InputRole> Roles,
    string CompletenessEvidence);
