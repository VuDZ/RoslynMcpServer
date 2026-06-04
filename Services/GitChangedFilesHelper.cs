using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services;

/// <summary>Git status/diff helpers for commit-scoped agent workflows.</summary>
public static class GitChangedFilesHelper
{
    public sealed record ChangedFile(string Path, string Status);

    public static string? FindRepositoryRoot(string startPath)
    {
        var full = Path.GetFullPath(startPath);
        if (File.Exists(full))
        {
            full = Path.GetDirectoryName(full) ?? full;
        }

        var current = new DirectoryInfo(full);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    public static async Task<(bool Success, string Output, string? Error)> RunGitAsync(
        string repositoryRoot,
        string arguments,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return (false, string.Empty, "Failed to start `git`. Ensure Git is on PATH.");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            return (false, stdout.TrimEnd(), string.IsNullOrWhiteSpace(stderr) ? $"git exit {process.ExitCode}" : stderr.TrimEnd());
        }

        return (true, stdout.TrimEnd(), null);
    }

    public static IReadOnlyList<ChangedFile> ParsePorcelainStatus(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var files = new List<ChangedFile>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4)
            {
                continue;
            }

            var status = line[..2].Trim();
            var path = line[3..].Trim().Trim('"');
            if (path.Contains(" -> ", StringComparison.Ordinal))
            {
                path = path.Split(" -> ", 2, StringSplitOptions.TrimEntries)[^1];
            }

            files.Add(new ChangedFile(path, status));
        }

        return files;
    }

    public static IReadOnlyList<string> SuggestTestProjects(Solution? solution, IReadOnlyList<string> changedRelativePaths)
    {
        if (solution is null || changedRelativePaths.Count == 0)
        {
            return [];
        }

        var changedSet = new HashSet<string>(changedRelativePaths, StringComparer.OrdinalIgnoreCase);
        var suggestions = new List<string>();

        foreach (var project in solution.Projects)
        {
            var name = project.Name;
            if (!name.Contains("test", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var doc in project.Documents)
            {
                if (doc.FilePath is null)
                {
                    continue;
                }

                var rel = GetRelativePathSafe(solution, doc.FilePath);
                if (rel is null)
                {
                    continue;
                }

                var stem = Path.GetFileNameWithoutExtension(rel);
                if (changedSet.Any(p => p.Contains(stem, StringComparison.OrdinalIgnoreCase)
                                        || stem.Contains(Path.GetFileNameWithoutExtension(p), StringComparison.OrdinalIgnoreCase)))
                {
                    suggestions.Add(project.FilePath ?? name);
                    break;
                }
            }
        }

        return suggestions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? GetRelativePathSafe(Solution solution, string filePath)
    {
        var roots = solution.Projects
            .Select(p => p.FilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetDirectoryName(p!)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(r => r.Length);

        foreach (var root in roots)
        {
            if (filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(root, filePath);
            }
        }

        return Path.GetFileName(filePath);
    }
}
