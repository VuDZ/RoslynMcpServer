namespace RoslynMcpServer.Hosting;

public sealed class McpToolEnableResult
{
    public required bool Changed { get; init; }

    public required string Markdown { get; init; }

    public IReadOnlyList<string> NewlyEnabledTools { get; init; } = [];
}
