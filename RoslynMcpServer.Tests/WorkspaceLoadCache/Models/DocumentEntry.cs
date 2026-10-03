using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record DocumentEntry(
    InstanceKey Project,
    string Role,
    string Path,
    string Name,
    ImmutableArray<string> Folders,
    SourceCodeKind SourceKind,
    bool GeneratedOnDisk,
    EncodingPolicy Encoding);
