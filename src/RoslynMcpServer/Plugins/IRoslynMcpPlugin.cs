namespace RoslynMcpServer.Plugins;

/// <summary>
/// A plugin compiled against the host assembly: it contributes singletons and MCP tools to the host
/// service collection before <c>IHost.Build</c>.
/// </summary>
/// <remarks>
/// <see cref="Name"/> is the plugin id from its manifest and must match the directory the plugin was
/// loaded from. The host creates each tool host type per invocation through
/// <c>ActivatorUtilities</c>, the same way it creates built-in tool host types, so constructor
/// dependencies come from the same <see cref="IServiceProvider"/> as the built-in tools.
/// </remarks>
public interface IRoslynMcpPlugin
{
    /// <summary>Plugin id. Matches the <c>id</c> field of the plugin manifest.</summary>
    string Name { get; }

    /// <summary>
    /// Runs before <c>IHost.Build</c>. Throwing from here rejects the plugin: the host publishes no
    /// services or tools of this plugin into that container and keeps the process alive.
    /// </summary>
    void Register(RoslynMcpPluginContext context);
}
