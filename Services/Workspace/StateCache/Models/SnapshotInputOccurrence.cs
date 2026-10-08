namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

public sealed record SnapshotInputOccurrence(
    SnapshotProjectKey Owner,
    string? DocumentKind,
    string Role,
    string RoleSource,
    SnapshotProjectKey? Producer,
    string? ProducerSource,
    bool Present);
