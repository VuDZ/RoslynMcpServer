namespace RoslynMcpServer.Plugins;

/// <summary>
/// A tool host type of a plugin could not be constructed, so the accepted name cannot be served. The
/// whole plugin is rejected instead of a single name: the host must not keep a plugin that would fail on
/// the first agent call.
/// </summary>
/// <remarks>
/// <see cref="RoslynMcpPluginContext.AddToolsFrom{T}"/> throws this out to
/// <see cref="RoslynMcpPluginRegistrar"/>, which is the only place that can undo everything the plugin
/// already published. It never reaches the host process.
/// </remarks>
public sealed class RoslynMcpPluginToolHostException : Exception
{
    public RoslynMcpPluginToolHostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
