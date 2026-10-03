using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record RegionMember(string RelativePath, string Sha256);
