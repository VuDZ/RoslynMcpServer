namespace RoslynMcpServer.Plugins;

/// <summary>
/// One plugin accepted for loading: where its files are, which type to ask for, and how it must be loaded.
/// </summary>
/// <remarks>
/// Discovery produces a plan without opening the entry assembly. Together <see cref="Directory"/> and
/// <see cref="EntryPath"/> keep both locations of a plugin that is loaded from a copy: the directory it was
/// found in stays in <see cref="Directory"/>, and <see cref="EntryPath"/> moves into the copy, which is what
/// a later <c>get_mcp_server_info</c> line has to report.
/// </remarks>
public sealed record PluginLoadPlan
{
    /// <summary>Plugin id from the manifest. Unique among the plans of one discovery pass.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Original plugin directory holding the manifest, the entry assembly and its dependencies. It is always
    /// the directory discovery found the plugin in: a shadow copy adds a second directory and is reported
    /// through <see cref="EntryPath"/>, so this property never names the copy.
    /// </summary>
    public required string Directory { get; init; }

    /// <summary>
    /// Full path of the entry file the host loads. While <see cref="UsesShadowCopy"/> is <see langword="false"/>
    /// this is the entry file inside <see cref="Directory"/>; once <see cref="PluginShadowCopy.Copy"/> has
    /// copied the directory it is the file of the same name inside the copy, so this property and
    /// <see cref="Directory"/> together name both the copy and the location the plugin was found in.
    /// </summary>
    public required string EntryPath { get; init; }

    /// <summary>Full name of the one type the host asks from the entry assembly.</summary>
    public required string PluginType { get; init; }

    /// <summary>Prefix every tool name of this plugin must start with.</summary>
    public required string ToolPrefix { get; init; }

    /// <summary>
    /// True when the plugin must be loaded from a copy of <see cref="Directory"/>, which
    /// <see cref="PluginShadowCopy.Copy"/> performs by copying the directory and repointing
    /// <see cref="EntryPath"/> at the copy. An explicit path points at a build output the next build
    /// overwrites while the host still runs; a drop-in directory next to the host executable stays where it
    /// is.
    /// </summary>
    public required bool UsesShadowCopy { get; init; }
}
