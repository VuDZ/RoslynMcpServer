using System.Text.Json;

namespace RoslynMcpServer.Services.Workspace;

internal static class SolutionFilterPath
{
    public static string? TryReadBackingSolution(string filterPath) =>
        TryRead(filterPath, includeProjects: false).BackingSolution;

    internal static (string? BackingSolution, HashSet<string>? Projects) TryReadWithProjects(string filterPath) =>
        TryRead(filterPath, includeProjects: true);

    internal static string? FindMissingBackingSolutionError(string targetPath)
    {
        if (WorkspaceInputPath.Classify(targetPath) != WorkspaceInputKind.SolutionFilter
            || TryReadWithProjects(targetPath).BackingSolution is not { } backingSolution)
        {
            return null;
        }

        try
        {
            _ = File.GetAttributes(backingSolution);
            return null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return $"Error: solution filter `{targetPath}` references a backing solution that was not found: `{backingSolution}`.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (string? BackingSolution, HashSet<string>? Projects) TryRead(string filterPath, bool includeProjects)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(filterPath), includeProjects ? default : new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            var solution = document.RootElement.GetProperty("solution");
            var path = solution.GetProperty("path").GetString();
            if (!WorkspaceInputPath.IsSolution(WorkspaceInputPath.Classify(path)))
            {
                return default;
            }

            // Filters written on Windows use backslashes even when read on another OS.
            var backingSolution = Path.GetFullPath(
                path!.Replace('\\', Path.DirectorySeparatorChar),
                Path.GetDirectoryName(Path.GetFullPath(filterPath))!);
            if (!includeProjects)
            {
                return (backingSolution, null);
            }

            var projects = new HashSet<string>(InputPathCanon.Comparer);
            foreach (var item in solution.GetProperty("projects").EnumerateArray())
            {
                var project = item.GetString();
                if (string.IsNullOrWhiteSpace(project))
                {
                    return default;
                }

                projects.Add(project.Replace('\\', '/'));
            }

            return (backingSolution, projects);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or KeyNotFoundException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return default;
        }
    }
}
