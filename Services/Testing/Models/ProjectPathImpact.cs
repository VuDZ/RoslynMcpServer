using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Testing.Models;

internal sealed record ProjectPathImpact(string Path, IReadOnlyList<ProjectId> ProjectIds);
