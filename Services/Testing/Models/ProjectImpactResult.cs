using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Testing.Models;

/// <summary>Project-level impact and the paths whose ownership could not be determined.</summary>
internal sealed record ProjectImpactResult(
    IReadOnlyList<ProjectId> ProjectIds,
    IReadOnlyList<ProjectId> TestProjectIds,
    IReadOnlyList<string> NotCoveredPaths,
    bool RequiresReload,
    bool ProjectGraphStale);
