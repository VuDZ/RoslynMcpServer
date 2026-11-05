using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Testing.Models;

internal sealed record TestImpactResult(
    IReadOnlyList<AffectedTest> Tests,
    IReadOnlyDictionary<ProjectId, IReadOnlyList<string>> ExpandedTestProjects,
    IReadOnlyList<string> NotCoveredPaths,
    bool RequiresReload,
    bool ProjectGraphStale);
