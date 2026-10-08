namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

public sealed record SnapshotProjectReference(
    SnapshotProjectKey Target,
    string[] Aliases,
    bool EmbedInteropTypes);
