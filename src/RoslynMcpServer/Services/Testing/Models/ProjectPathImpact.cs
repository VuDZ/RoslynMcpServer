using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Models;

namespace RoslynMcpServer.Services.Testing.Models;

internal sealed record ProjectPathImpact(
    string Path,
    IReadOnlyList<ProjectId> ProjectIds,
    IReadOnlyList<InputOccurrence> Occurrences);
