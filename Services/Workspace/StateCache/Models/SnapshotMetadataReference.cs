namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

public sealed record SnapshotMetadataReference(
    string Path,
    string Kind,
    string[] Aliases,
    bool EmbedInteropTypes,
    string BinaryDocumentationPolicy);
