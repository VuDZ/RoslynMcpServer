using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record RegionEntry(
    InstanceKey Owner,
    string Root,
    string Role,
    ImmutableArray<string> Includes,
    ImmutableArray<string> Excludes,
    ImmutableArray<RegionMember> Members,
    string ManifestSha256,
    string RawEvidenceLocator);
