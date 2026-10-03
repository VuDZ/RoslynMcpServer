using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record SemanticSnapshot(
    string Schema,
    ExperimentRequest Request,
    ImmutableArray<ProjectEntry> Projects,
    ImmutableArray<ProjectEdge> ProjectReferences);
