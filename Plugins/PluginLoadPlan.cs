namespace RoslynMcpServer.Plugins;

/// <summary>
/// One plugin accepted for loading: where its files are, which type to ask for, and how it must be loaded.
/// </summary>
/// <remarks>
/// Discovery produces a plan without opening the entry assembly. <see cref="Directory"/> is the directory
/// the plugin was found in, so a later stage can copy that directory aside and load the copy while the
/// plan still names the original location.
/// </remarks>
public sealed record PluginLoadPlan
{
    /// <summary>Plugin id from the manifest. Unique among the plans of one discovery pass.</summary>
    public required string Id { get; init; }

    /// <summary>Original plugin directory holding the manifest, the entry assembly and its dependencies.</summary>
    public required string Directory { get; init; }

    /// <summary>
    /// Full path of the entry assembly. <see cref="UsesShadowCopy"/> says whether this file is loaded as it
    /// is or copied first.
    /// </summary>
    public required string EntryPath { get; init; }

    /// <summary>Full name of the one type the host asks from the entry assembly.</summary>
    public required string PluginType { get; init; }

    /// <summary>Prefix every tool name of this plugin must start with.</summary>
    public required string ToolPrefix { get; init; }

    /// <summary>
    /// True when the plugin must be loaded from a copy of <see cref="Directory"/>. An explicit path points
    /// at a build output the next build overwrites while the host still runs; a drop-in directory next to
    /// the host executable stays where it is.
    /// </summary>
    public required bool UsesShadowCopy { get; init; }
}
