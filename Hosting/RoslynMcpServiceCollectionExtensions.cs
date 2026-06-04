using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using RoslynMcpServer.Tools;

namespace RoslynMcpServer.Hosting;

public static class RoslynMcpServiceCollectionExtensions
{
    /// <summary>
    /// Registers shared services required by MCP tool types (e.g. <see cref="SolutionManager"/> and tool singletons).
    /// MCP <c>WithTools</c> activates tools via <see cref="ActivatorUtilities"/>; any constructor dependency must be resolvable here.
    /// </summary>
    public static IServiceCollection AddRoslynMcpCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<SolutionManager>();
        foreach (var toolType in McpToolRegistry.ToolTypes)
        {
            services.AddSingleton(toolType);
        }

        return services;
    }

    /// <summary>Registers stdio MCP transport and all Roslyn MCP tools (same set as production <c>Program.cs</c>).</summary>
    public static IMcpServerBuilder AddRoslynMcpServerTools(this IServiceCollection services)
    {
        services.AddRoslynMcpCoreServices();

        return services
            .AddMcpServer(o => McpInboundProtocolLogger.Register(o))
            .WithStdioServerTransport()
            .WithTools<RoslynTools>()
            .WithTools<WorkspaceTools>()
            .WithTools<CodeAnalysisTools>()
            .WithTools<CodeFixTools>()
            .WithTools<CodeSkeletonTools>()
            .WithTools<NavigationTools>()
            .WithTools<RefactoringTools>()
            .WithTools<AstTools>()
            .WithTools<EditingTools>()
            .WithTools<BuildTools>()
            .WithTools<RunTools>()
            .WithTools<TestTools>()
            .WithTools<NuGetTools>()
            .WithTools<ProjectTools>()
            .WithTools<UtilityTools>()
            .WithTools<ServerLifecycleTools>();
    }
}
