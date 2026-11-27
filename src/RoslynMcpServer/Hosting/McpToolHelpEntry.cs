namespace RoslynMcpServer.Hosting;

/// <summary>Supplemental long-form guidance. Parameter names/types/defaults come from the live schema.</summary>
public sealed class McpToolHelpEntry
{
    public string? Prerequisites { get; init; }

    public string? Workflow { get; init; }

    public string? Pitfalls { get; init; }

    public IReadOnlyList<string> RelatedTools { get; init; } = [];
}
