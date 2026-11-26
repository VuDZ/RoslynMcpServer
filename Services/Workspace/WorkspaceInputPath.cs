namespace RoslynMcpServer.Services.Workspace;

internal static class WorkspaceInputPath
{
    public static WorkspaceInputKind Classify(string? path) =>
        Path.GetExtension(path)?.ToLowerInvariant() switch
        {
            ".csproj" => WorkspaceInputKind.CSharpProject,
            ".sln" => WorkspaceInputKind.Sln,
            ".slnx" => WorkspaceInputKind.Slnx,
            ".slnf" => WorkspaceInputKind.SolutionFilter,
            _ => WorkspaceInputKind.Unknown,
        };

    public static bool IsSolution(WorkspaceInputKind kind) =>
        kind is WorkspaceInputKind.Sln or WorkspaceInputKind.Slnx;

    public static bool IsSolutionOrFilter(WorkspaceInputKind kind) =>
        IsSolution(kind) || kind == WorkspaceInputKind.SolutionFilter;
}
