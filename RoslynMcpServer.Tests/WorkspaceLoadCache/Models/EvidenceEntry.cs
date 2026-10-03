using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record EvidenceEntry(
    string Category,
    InstanceKey? Owner,
    string PathOrRegion,
    string? Sha256,
    bool IsAbsent,
    string Source,
    string SourceVersion,
    string RawEvidenceLocator);
