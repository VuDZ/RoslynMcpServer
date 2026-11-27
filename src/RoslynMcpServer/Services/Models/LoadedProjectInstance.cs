using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Models;

/// <summary>
/// One project object actually present in the loaded Roslyn graph.
/// Identity is <see cref="Id"/>, not the project name or the <c>.csproj</c> path:
/// a path may theoretically appear twice, but an ordinary inner-TFM load does not invent the second object.
/// </summary>
internal sealed record LoadedProjectInstance(
    ProjectId Id,
    string? FilePath,
    string Name,
    string AssemblyName,
    IReadOnlyList<ProjectId> ProjectReferenceIds,
    IReadOnlyList<string> DeclaredTargetFrameworks);
