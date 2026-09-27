namespace RoslynMcpServer.Services;

/// <summary>How the published semantic snapshot was obtained for this session.</summary>
public enum WorkspaceLoadSource
{
    None = 0,
    ConfigFile = 1,
    ExplicitLoad = 2,
}
