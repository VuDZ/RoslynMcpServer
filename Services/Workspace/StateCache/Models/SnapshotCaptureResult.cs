namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

/// <summary>An unsupported outcome carries no candidate snapshot and does not block ordinary loading.</summary>
public sealed record SnapshotCaptureResult(WorkspaceSnapshot? Snapshot, string? UnsupportedReason);
