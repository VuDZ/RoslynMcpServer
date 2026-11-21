using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Workspace.Models;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Raw solution, shadow-copy results, and published project list from the same load/prepare boundary.
/// </summary>
internal sealed record WorkspaceLoadPreparationResult(
    Solution Solution,
    IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> ShadowCopyResults,
    ProjectListSnapshot PublishedProjectList);
