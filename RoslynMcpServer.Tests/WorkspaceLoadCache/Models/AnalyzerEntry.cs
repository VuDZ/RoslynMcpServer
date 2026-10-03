using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record AnalyzerEntry(
    InstanceKey Consumer,
    string OriginalPath,
    string Sha256,
    string OriginDirectory,
    ImmutableArray<string> DependencyPaths);
