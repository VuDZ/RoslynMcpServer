using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Baseline.Models;

namespace RoslynMcpServer.Services.Testing.Models;

/// <summary>Current syntax and detected changes for one loaded project occurrence; null changes mean unknown baseline.</summary>
internal sealed record DocumentImpact(
    ProjectId ProjectId,
    string Path,
    BaselineDocumentSnapshot? Current,
    IReadOnlyList<BaselineDocumentChange>? Changes);
