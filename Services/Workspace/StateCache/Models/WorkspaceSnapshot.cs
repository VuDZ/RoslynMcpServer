namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

/// <summary>Portable graph facts only. Version/profile matching does not authorize reuse or DLL execution.</summary>
public sealed record WorkspaceSnapshot(
    int SchemaVersion,
    string Profile,
    string RoslynVersion,
    string WorkspacePath,
    SnapshotLoadProperties Properties,
    SnapshotProject[] Projects,
    SnapshotInputPath[] InputPaths);
