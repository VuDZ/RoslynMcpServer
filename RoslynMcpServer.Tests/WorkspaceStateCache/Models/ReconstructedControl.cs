using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynMcpServer.Tests.WorkspaceStateCache.Models;

internal sealed record ReconstructedControl(
    Solution Solution,
    IReadOnlyDictionary<ProjectId, CSharpCompilation> Compilations,
    IReadOnlyList<ProjectInfo> Descriptors);
