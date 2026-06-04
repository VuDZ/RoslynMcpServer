using RoslynMcpServer.Tools;

namespace RoslynMcpServer.Hosting;

/// <summary>All MCP tool host types registered via <see cref="RoslynMcpServiceCollectionExtensions"/>.</summary>
public static class McpToolRegistry
{
    public static IReadOnlyList<Type> ToolTypes { get; } =
    [
        typeof(RoslynTools),
        typeof(WorkspaceTools),
        typeof(CodeAnalysisTools),
        typeof(CodeFixTools),
        typeof(CodeSkeletonTools),
        typeof(NavigationTools),
        typeof(RefactoringTools),
        typeof(AstTools),
        typeof(EditingTools),
        typeof(BuildTools),
        typeof(RunTools),
        typeof(TestTools),
        typeof(NuGetTools),
        typeof(ProjectTools),
        typeof(UtilityTools),
        typeof(ServerLifecycleTools),
    ];
}
