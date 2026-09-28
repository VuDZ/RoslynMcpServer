namespace RoslynMcpServer.Plugins;

/// <summary>
/// Result of one shadow-copy pass: the plans to load, in input order, and the plans whose copy failed.
/// </summary>
/// <remarks>
/// A plan that needed a copy keeps its original directory in <see cref="PluginLoadPlan.Directory"/> while its
/// entry path points into the copy, so the directory the plugin was found in and the file the host loads are
/// both known to the caller.
/// </remarks>
public sealed record PluginShadowCopyResult
{
    /// <summary>Plans to load, in input order, each one loadable as it is.</summary>
    public IReadOnlyList<PluginLoadPlan> Plans { get; init; } = [];

    /// <summary>Plans whose copy failed, each with its concrete reason; one skip never ends the pass.</summary>
    public IReadOnlyList<PluginDiscoverySkip> Skipped { get; init; } = [];
}
