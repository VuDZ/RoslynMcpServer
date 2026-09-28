namespace RoslynMcpServer.Plugins;

/// <summary>
/// One plugin source that produced no plan. A skip never ends the pass: the remaining sources are still
/// examined, so a broken plugin leaves the other plugins and the built-in tools usable.
/// </summary>
public sealed record PluginDiscoverySkip
{
    /// <summary>Path of the refused source: a plugin directory, an entry file or a configured path.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Plugin id, when the manifest was read far enough to name it.</summary>
    public string? PluginId { get; init; }

    /// <summary>Why this source produced no plan.</summary>
    public required string Reason { get; init; }
}
