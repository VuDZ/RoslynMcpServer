namespace RoslynMcpServer.Hosting;

/// <summary>Resolved startup selection: profile, requested groups, and actually registered tools.</summary>
public sealed class McpToolSurface
{
    public required string Profile { get; init; }

    /// <summary>Canonical group names requested via <c>ROSLYN_MCP_TOOL_GROUPS</c>, distinct, first-seen order.</summary>
    public required IReadOnlyList<string> StartupGroups { get; init; }

    public required IReadOnlyList<McpToolDescriptor> RegisteredTools { get; init; }

    public int RegisteredToolCount => RegisteredTools.Count;

    public string FormatStartupGroupsDisplay() =>
        StartupGroups.Count == 0 ? "(none)" : string.Join(", ", StartupGroups);

    public string FormatStartupGroupsMarkdown() =>
        StartupGroups.Count == 0 ? "(none)" : $"`{FormatStartupGroupsDisplay()}`";
}
