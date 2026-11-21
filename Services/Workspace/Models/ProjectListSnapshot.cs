using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Workspace.Models;

internal sealed record ProjectListSnapshot(
    Solution? Solution,
    ImmutableDictionary<ProjectId, ProjectLoadMetadata> Metadata,
    string? UnavailableMessage);
