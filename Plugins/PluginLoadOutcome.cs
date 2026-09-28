namespace RoslynMcpServer.Plugins;

/// <summary>
/// Result of loading one plan: loaded or skipped, the plugin id, the entry file the loader opened, the tool
/// names the plugin published, and the reason of a skip.
/// </summary>
/// <remarks>
/// <para>
/// There is no third state. A plugin that loaded and published no tool name is loaded; only a plan whose
/// entry, type, instance, name or registration failed is skipped.
/// </para>
/// <para>
/// <see cref="EntryPath"/> is the file the loader opened, so a plan with a shadow copy names the copy here
/// while <see cref="PluginLoadPlan.Directory"/> of that plan keeps the directory the plugin was found in.
/// </para>
/// </remarks>
public sealed record PluginLoadOutcome
{
    /// <summary>Plugin id of the plan.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Entry file the loader opened: the copy of the plugin directory when the plan had one, otherwise the
    /// file the plan named.
    /// </summary>
    public required string EntryPath { get; init; }

    /// <summary>
    /// True when the entry assembly was loaded, the plugin type was created, its name equalled the plan id
    /// and <see cref="IRoslynMcpPlugin.Register"/> returned normally.
    /// </summary>
    public required bool IsLoaded { get; init; }

    /// <summary>Tool names the plugin published into the host collection, in registration order.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>
    /// Why this plan produced no plugin, always set when <see cref="IsLoaded"/> is <see langword="false"/>
    /// and <see langword="null"/> when the plugin loaded.
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Registration report of this plan: the names it published and the reasons names were refused.
    /// </summary>
    /// <remarks>
    /// A rejected plugin keeps in this report the names it had published before the failure even though the
    /// container does not; <see cref="ToolNames"/> is what a loaded plugin actually contributed. The
    /// registered types are the tool host types of this plan, which is what a later
    /// <c>get_mcp_server_info</c> pass reads a tool description and its parameters from.
    /// </remarks>
    public RoslynMcpPluginRegistration Registration { get; init; } = RoslynMcpPluginRegistration.Empty;
}
