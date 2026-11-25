namespace RoslynMcpServer.Services.Workspace.Models;

internal readonly record struct ProjectLoadMetadata(string? TargetFramework, string? OutputType)
{
    public int? CodePage { get; init; }
}
