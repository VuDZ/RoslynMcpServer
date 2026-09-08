using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Hosting;

public static class RoslynMcpServiceCollectionExtensions
{
    /// <summary>
    /// Registers shared services required by MCP tool types (e.g. <see cref="SolutionManager"/> and tool singletons).
    /// MCP tool activation uses <see cref="ActivatorUtilities"/>; any constructor dependency must be resolvable here.
    /// </summary>
    public static IServiceCollection AddRoslynMcpCoreServices(
        this IServiceCollection services,
        McpToolProfileOptions? profileOptions = null)
    {
        RegisterCore(services, McpToolCatalog.CreateSurface(profileOptions ?? McpToolProfileOptions.FromEnvironment()));
        return services;
    }

    /// <summary>
    /// Registers stdio MCP transport and the tools selected by the startup profile/groups
    /// (catalog-driven; method granularity, not class-level <c>WithTools&lt;T&gt;</c>).
    /// </summary>
    public static IMcpServerBuilder AddRoslynMcpServerTools(
        this IServiceCollection services,
        McpToolProfileOptions? profileOptions = null)
    {
        var surface = McpToolCatalog.CreateSurface(profileOptions ?? McpToolProfileOptions.FromEnvironment());
        RegisterCore(services, surface);

        var builder = services
            .AddMcpServer(o =>
            {
                o.ServerInstructions = McpToolHelpCatalog.ServerInstructions;
                McpInboundProtocolLogger.Register(o);
            })
            .WithStdioServerTransport();

        McpToolRegistry.RegisterSelectedTools(services, surface);
        return builder;
    }

    private static void RegisterCore(IServiceCollection services, McpToolSurface surface)
    {
        var options = new McpToolProfileOptions
        {
            Profile = surface.Profile,
            Groups = surface.StartupGroups.Count == 0 ? null : string.Join(',', surface.StartupGroups),
        };

        services.AddSingleton<IOptions<McpToolProfileOptions>>(Options.Create(options));
        services.AddSingleton(surface);
        services.AddSingleton<SolutionManager>();
        foreach (var toolType in McpToolCatalog.HostTypes)
        {
            services.AddSingleton(toolType);
        }
    }
}
