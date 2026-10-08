namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

/// <summary>Canonical path and witnessed instance context; never a Roslyn ID or a path-only TFM guess.</summary>
public sealed record SnapshotProjectKey(
    string ProjectPath,
    string Configuration,
    string Platform,
    string TargetFramework);
