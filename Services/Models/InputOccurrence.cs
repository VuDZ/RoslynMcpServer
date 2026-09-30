using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Models;

/// <summary>One project instance's relationship to a canonical path. Occurrences are not merged.</summary>
internal sealed record InputOccurrence(
    ProjectId ProjectId,
    DocumentId? DocumentId,
    InputRole Role,
    ProjectId? ProducerProjectId,
    bool Present);
