using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Models;

/// <summary>
/// Aggregated observation of one path and role. A generated or output occurrence of the producing
/// project is recorded here and does not count as that project's input revision.
/// </summary>
internal sealed record InputPathNotice(
    string CanonicalPath,
    InputRole Role,
    ProjectId? ProjectId,
    bool CountsAsInputRevision);
