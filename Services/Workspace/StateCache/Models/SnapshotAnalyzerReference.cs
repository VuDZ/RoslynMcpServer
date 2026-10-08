namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

/// <summary>Original file identity only; shadow-copy paths and cached execution provenance are forbidden.</summary>
public sealed record SnapshotAnalyzerReference(string OriginalPath, string IdentitySource);
