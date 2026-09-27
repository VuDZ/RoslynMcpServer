using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services;

/// <summary>
/// Solution returned from one load/prepare boundary, with the shadow-copy results of that same prepare.
/// </summary>
internal sealed record WorkspaceLoadPreparationResult(
    Solution Solution,
    IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> ShadowCopyResults);
