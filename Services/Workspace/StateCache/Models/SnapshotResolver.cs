namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

/// <summary>Closed factory identity plus public constructor inputs; unknown runtime types are unsupported.</summary>
public sealed record SnapshotResolver(
    string Kind,
    string? BaseDirectory,
    string[] SearchPaths,
    SnapshotNameValue[] PathMap);
