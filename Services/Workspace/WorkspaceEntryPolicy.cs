using RoslynMcpServer.Config;

namespace RoslynMcpServer.Services.Workspace;

internal static class WorkspaceEntryPolicy
{
    private const string DefaultGuidance =
        "Use the containing .sln or .slnx to load or build this project. A direct .csproj invocation lacks solution context, "
        + "so settings such as SolutionDir and intermediate output paths can differ. If errors occurred, retry through the solution.";

    internal static bool IsDirectProject(string? path) =>
        string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase);

    internal static string Rejection(RoslynMcpFileSettings settings) =>
        "Direct .csproj entry points are disabled by require-solution. " + Guidance(settings);

    internal static string Warning(RoslynMcpFileSettings settings) => "Warning: " + Guidance(settings);

    internal static string AppendWarning(string result, string? path, RoslynMcpFileSettings settings)
    {
        if (settings.RequireSolution || !IsDirectProject(path))
        {
            return result;
        }

        var warning = Warning(settings);
        if (WorkspaceEntryGuidance.Current is { } guidance)
        {
            guidance.Message = warning;
            return result;
        }

        return result.TrimEnd() + Environment.NewLine + Environment.NewLine + warning;
    }

    private static string Guidance(RoslynMcpFileSettings settings) => settings.DirectProjectGuidance ?? DefaultGuidance;
}
