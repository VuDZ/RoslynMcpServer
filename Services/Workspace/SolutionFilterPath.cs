using System.Text.Json;

namespace RoslynMcpServer.Services.Workspace;

internal static class SolutionFilterPath
{
    public static string? TryReadBackingSolution(string filterPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(filterPath), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            var path = document.RootElement.GetProperty("solution").GetProperty("path").GetString();
            if (!WorkspaceInputPath.IsSolution(WorkspaceInputPath.Classify(path)))
            {
                return null;
            }

            // Filters written on Windows use backslashes even when read on another OS.
            return Path.GetFullPath(
                path!.Replace('\\', Path.DirectorySeparatorChar),
                Path.GetDirectoryName(Path.GetFullPath(filterPath))!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or KeyNotFoundException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
