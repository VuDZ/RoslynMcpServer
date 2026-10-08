using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Workspace.StateCache.Models;

namespace RoslynMcpServer.Services.Workspace.StateCache;

/// <summary>
/// Capture-time link from a live document to its portable facts. The document id is not stored in the snapshot.
/// </summary>
internal sealed record SnapshotDocumentBinding(
    ProjectId ProjectId,
    DocumentId DocumentId,
    SnapshotDocument Document,
    string RoleSource);
