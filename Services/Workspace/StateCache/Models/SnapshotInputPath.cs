namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

/// <summary>Every occurrence is retained, including linked owners and distinct roles on one physical path.</summary>
public sealed record SnapshotInputPath(string Path, SnapshotInputOccurrence[] Occurrences);
