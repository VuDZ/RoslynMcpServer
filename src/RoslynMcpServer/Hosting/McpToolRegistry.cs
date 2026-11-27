using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcpServer.Hosting;

/// <summary>
/// MCP tool host types and selected-tool SDK registration. Catalog is the source of truth;
/// do not maintain a parallel <c>WithTools&lt;T&gt;()</c> list.
/// </summary>
public static class McpToolRegistry
{
    public static IReadOnlyList<Type> ToolTypes => McpToolCatalog.HostTypes;

    public static void RegisterSelectedTools(IServiceCollection services, McpToolSurface surface)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(surface);

        foreach (var descriptor in surface.RegisteredTools)
        {
            services.AddSingleton(descriptor.CreateFactory());
        }
    }
}
