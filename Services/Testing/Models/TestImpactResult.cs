using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Testing.Models;

internal sealed record TestImpactResult(
    IReadOnlyList<AffectedTest> Tests,
    IReadOnlyDictionary<ProjectId, IReadOnlyList<ProjectExpansionReason>> ExpandedTestProjects,
    IReadOnlyList<string> NotCoveredPaths,
    bool RequiresReload,
    bool ProjectGraphStale);
