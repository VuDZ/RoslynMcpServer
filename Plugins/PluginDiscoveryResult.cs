namespace RoslynMcpServer.Plugins;

/// <summary>
/// Outcome of one discovery pass: the plugins to load, in order, and the sources that were refused.
/// </summary>
public sealed record PluginDiscoveryResult
{
    /// <summary>
    /// Accepted plugins in discovery order: drop-in directories, then the settings array, then the
    /// environment variable.
    /// </summary>
    public IReadOnlyList<PluginLoadPlan> Plans { get; init; } = [];

    /// <summary>Refused sources in the order they were examined, each with its concrete reason.</summary>
    public IReadOnlyList<PluginDiscoverySkip> Skipped { get; init; } = [];
}
