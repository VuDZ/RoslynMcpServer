namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

public sealed record SnapshotPropertySet(string? Configuration, string? Platform, string? TargetFramework);
