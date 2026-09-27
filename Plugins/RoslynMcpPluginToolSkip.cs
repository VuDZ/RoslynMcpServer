namespace RoslynMcpServer.Plugins;

/// <summary>
/// One public method of a plugin tool host type whose name was refused, with the reason, while the other
/// names of the same call keep registering. A refused name is not a broken plugin: the built-in catalog
/// or another registration of the same service collection already owns it.
/// </summary>
public sealed class RoslynMcpPluginToolSkip
{
    /// <summary>Tool name read from <c>[McpServerTool]</c>.</summary>
    public required string ToolName { get; init; }

    /// <summary>Public method the name was read from.</summary>
    public required string MethodName { get; init; }

    /// <summary>Why the name was refused. A creation failure names the failing method.</summary>
    public required string Reason { get; init; }
}
