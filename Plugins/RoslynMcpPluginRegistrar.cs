using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcpServer.Plugins;

/// <summary>
/// Calls <see cref="IRoslynMcpPlugin.Register"/> for plugins already created in this process. Nothing
/// here reads a plugin assembly from disk.
/// </summary>
/// <remarks>
/// <para>
/// Registration runs before <c>IHost.Build</c>. The SDK fills the live tool collection from the
/// registered <c>McpServerTool</c> services at the first <c>IOptions&lt;McpServerOptions&gt;.Value</c>,
/// not at <c>Build</c>, so a plugin tool appears in that collection only after that resolve.
/// </para>
/// <para>
/// A plugin that throws — from <c>Register</c> or from a tool host the host cannot construct — rejects
/// the plugin as a whole: the container keeps neither its services nor its tools, the names it had taken
/// are free again, and the process stays alive. This is the only place that can undo what the plugin
/// published before the failure.
/// </para>
/// </remarks>
public static class RoslynMcpPluginRegistrar
{
    /// <summary>
    /// Runs one plugin against <paramref name="services"/>.
    /// </summary>
    /// <param name="services">Host collection, after <c>AddRoslynMcpServerTools</c>.</param>
    /// <param name="pluginName">Plugin id from the manifest, the same value as <see cref="IRoslynMcpPlugin.Name"/>.</param>
    /// <param name="toolPrefix">Manifest tool prefix every tool name of this plugin must start with.</param>
    /// <param name="plugin">Plugin instance.</param>
    /// <returns>
    /// Published names and skip reasons. A rejected plugin reports its reason under
    /// <see cref="RoslynMcpPluginRegistration.Failures"/> and publishes no name.
    /// </returns>
    public static RoslynMcpPluginRegistration Register(
        IServiceCollection services,
        string pluginName,
        string toolPrefix,
        IRoslynMcpPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        ArgumentNullException.ThrowIfNull(plugin);

        // Descriptors of the host and of earlier plugins end here: everything this plugin adds lands after
        // this mark and is what the rollback of a rejected plugin cuts off.
        var baseline = services.Count;
        using var context = new RoslynMcpPluginContext(services, pluginName, toolPrefix);

        try
        {
            plugin.Register(context);
            return Report(pluginName, context.Registration);
        }
        catch (Exception ex)
        {
            // A throwing Register and a refused tool host both land here, and both reject the plugin as a
            // whole: nothing this plugin added may stay, including the tools, singletons and names of an
            // AddToolsFrom call that already succeeded.
            context.RemoveDescriptorsAddedSince(baseline);
            var released = context.ReleasePublishedNames();

            // One failure line only: the host must not have to read two entries to learn what was refused
            // and which names went back into the pool.
            var failure = Failed(
                pluginName,
                released.Count == 0
                    ? Message(ex)
                    : $"{Message(ex)} (released tool names: {string.Join(", ", released)})");

            return Report(pluginName, context.Registration.Merge(failure));
        }
    }

    private static RoslynMcpPluginRegistration Report(string pluginName, RoslynMcpPluginRegistration registration)
    {
        foreach (var failure in registration.Failures)
        {
            Console.Error.WriteLine($"[RoslynMcp] {failure}");
        }

        foreach (var skip in registration.Skipped)
        {
            Console.Error.WriteLine(
                $"[RoslynMcp] plugin '{pluginName}' skipped '{skip.ToolName}' ({skip.MethodName}): {skip.Reason}");
        }

        return registration;
    }

    private static RoslynMcpPluginRegistration Failed(string pluginName, string reason) =>
        new() { Failures = [$"plugin '{pluginName}' was not loaded: {reason}"] };

    private static string Message(Exception exception)
    {
        var message = exception.Message.Trim();
        return message.Length == 0 ? exception.GetType().Name : message;
    }
}
