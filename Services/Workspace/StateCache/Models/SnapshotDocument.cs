namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

/// <summary>One membership. Text remains on disk; generated flag is the public loader descriptor fact.</summary>
public sealed record SnapshotDocument(
    string Kind,
    string Path,
    string Name,
    string[] Folders,
    string SourceCodeKind,
    bool IsGenerated,
    SnapshotTextMetadata TextMetadata);
