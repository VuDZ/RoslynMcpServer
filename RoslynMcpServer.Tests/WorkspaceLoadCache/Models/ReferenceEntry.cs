using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record ReferenceEntry(
    string Path,
    MetadataImageKind Kind,
    ImmutableArray<string> Aliases,
    bool EmbedInteropTypes,
    string? DocumentationPath);
