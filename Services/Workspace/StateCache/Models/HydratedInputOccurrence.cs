using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

/// <summary>One portable occurrence rebound to a fresh host, including its provenance and all owners.</summary>
internal sealed record HydratedInputOccurrence(
    ProjectId Owner,
    DocumentId? DocumentId,
    string Role,
    string RoleSource,
    ProjectId? Producer,
    string? ProducerSource,
    bool Present);
