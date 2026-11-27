namespace RoslynMcpServer.Hosting;

/// <summary>
/// Startup tool-surface configuration. Bound from
/// <c>ROSLYN_MCP_TOOL_PROFILE</c> and <c>ROSLYN_MCP_TOOL_GROUPS</c>.
/// </summary>
public sealed class McpToolProfileOptions
{
    public const string ProfileVariableName = "ROSLYN_MCP_TOOL_PROFILE";
    public const string GroupsVariableName = "ROSLYN_MCP_TOOL_GROUPS";

    public const string FullProfile = "full";
    public const string LiteProfile = "lite";

    /// <summary>
    /// <c>full</c> or <c>lite</c>. Null, empty, or whitespace means <c>full</c>.
    /// Matching is case-insensitive.
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>
    /// Comma-separated logical groups added before the first <c>tools/list</c>.
    /// Duplicates and surrounding whitespace are ignored. Unknown names fail startup.
    /// </summary>
    public string? Groups { get; set; }

    public static McpToolProfileOptions FromEnvironment() => new()
    {
        Profile = Environment.GetEnvironmentVariable(ProfileVariableName),
        Groups = Environment.GetEnvironmentVariable(GroupsVariableName),
    };
}
