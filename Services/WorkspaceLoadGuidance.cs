using System.Text;

namespace RoslynMcpServer.Services;

/// <summary>
/// Builds consistent "no workspace loaded" guidance with candidate .sln/.slnx paths.
/// Does not auto-load — agents must call <c>load_workspace</c>.
/// </summary>
public static class WorkspaceLoadGuidance
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "packages",
        ".nuget",
    };

    /// <summary>Default cap so MCP error text stays LLM-friendly.</summary>
    public const int DefaultMaxCandidates = 20;

    /// <summary>BFS depth under each search root.</summary>
    public const int DefaultMaxDepth = 5;

    public static string FormatNoWorkspaceLoadedMessage(string? leadingSentence = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(leadingSentence ?? "Error: No workspace loaded.");
        sb.AppendLine(
            "Call `load_workspace` with the absolute path to your `.sln`, `.slnx`, or entry `.csproj`.");

        var candidates = DiscoverSolutionCandidates();
        if (candidates.Count == 0)
        {
            sb.AppendLine("No `.sln`/`.slnx` candidates found under `ROSLYN_MCP_WORKSPACE` or the current directory.");
            sb.AppendLine("Set MCP env `ROSLYN_MCP_WORKSPACE` to the repo root, or pass an absolute solution path.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("Candidate solution files:");
        foreach (var path in candidates)
        {
            sb.AppendLine($"- `{path}`");
        }

        return sb.ToString().TrimEnd();
    }

    public static IReadOnlyList<string> DiscoverSolutionCandidates(
        int maxCandidates = DefaultMaxCandidates,
        int maxDepth = DefaultMaxDepth)
    {
        if (maxCandidates <= 0)
        {
            return Array.Empty<string>();
        }

        var roots = new List<string>();
        var env = Environment.GetEnvironmentVariable("ROSLYN_MCP_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(env))
        {
            try
            {
                var full = Path.GetFullPath(env.Trim());
                if (Directory.Exists(full))
                {
                    roots.Add(full);
                }
            }
            catch
            {
                // ignore invalid env
            }
        }

        try
        {
            var cwd = Path.GetFullPath(Environment.CurrentDirectory);
            if (Directory.Exists(cwd) && !roots.Exists(r => PathsEqual(r, cwd)))
            {
                roots.Add(cwd);
            }
        }
        catch
        {
            // ignore
        }

        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            CollectSolutions(root, maxDepth, maxCandidates, found, seen);
            if (found.Count >= maxCandidates)
            {
                break;
            }
        }

        return found;
    }

    private static void CollectSolutions(
        string root,
        int maxDepth,
        int maxCandidates,
        List<string> found,
        HashSet<string> seen)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0 && found.Count < maxCandidates)
        {
            var (current, depth) = queue.Dequeue();

            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    var ext = Path.GetExtension(file);
                    if (!ext.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                        && !ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var full = Path.GetFullPath(file);
                    if (seen.Add(full))
                    {
                        found.Add(full);
                        if (found.Count >= maxCandidates)
                        {
                            return;
                        }
                    }
                }
            }
            catch
            {
                continue;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                if (ExcludedDirectoryNames.Contains(name))
                {
                    continue;
                }

                queue.Enqueue((sub, depth + 1));
            }
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        return string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
