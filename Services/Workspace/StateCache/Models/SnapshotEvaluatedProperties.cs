namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

public sealed record SnapshotEvaluatedProperties(
    SnapshotPropertySet Values,
    string Source,
    string[] UnknownProperties);
