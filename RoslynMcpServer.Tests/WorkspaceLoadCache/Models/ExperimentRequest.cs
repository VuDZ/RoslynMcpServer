using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record ExperimentRequest(
    string WorkspacePath,
    string? Configuration,
    string? Platform,
    string? TargetFramework,
    ImmutableArray<string> SelectedRoots,
    string Scope,
    bool MetadataMode);
