using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Models;

/// <summary>
/// What the loaded graph can say about one input category: where the data came from,
/// which events matter, which project instances own it, and what is not claimed.
/// </summary>
internal sealed record InputCategoryEvidence(
    InputCategoryKind Kind,
    string DataSource,
    string WatchedEvents,
    IReadOnlyList<ProjectId> Owners,
    string Limits,
    bool ItemsIndexedFromLoadedGraph,
    bool CompletenessKnown);
