namespace RoslynMcpServer.Services.Workspace;

/// <summary>Tracks workspace entry guidance generated during one MCP call, including lazy loads.</summary>
internal sealed class WorkspaceEntryGuidance
{
    internal static WorkspaceEntryGuidance? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    internal string? Message { get; set; }

    private static readonly AsyncLocal<WorkspaceEntryGuidance?> _current = new();
}
