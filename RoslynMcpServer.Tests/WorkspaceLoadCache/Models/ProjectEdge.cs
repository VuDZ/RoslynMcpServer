using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record ProjectEdge(
    InstanceKey From,
    InstanceKey To,
    ImmutableArray<string> Aliases,
    bool EmbedInteropTypes);
