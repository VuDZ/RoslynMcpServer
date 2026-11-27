using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Models;

/// <summary>
/// A directory that may gain project items after the current document list was evaluated.
/// Recursive regions are project directories only.
/// </summary>
internal sealed record MembershipRegion(
    string Directory,
    bool Recursive,
    ProjectId Owner,
    string Evidence,
    bool CompletenessKnown);
