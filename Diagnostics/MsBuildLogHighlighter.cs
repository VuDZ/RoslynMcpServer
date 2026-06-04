using System.Text;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Diagnostics;

/// <summary>Extracts MSBuild/NuGet highlights and task-failure context from verbose dotnet logs.</summary>
public static class MsBuildLogHighlighter
{
    private const int ContextLinesBeforeTaskFailure = 20;
    private const int MaxHighlightLines = 50;
    private const int ScanBackwardForProjectLines = 60;

    private static readonly Regex MsBuildExecutablePath = new(
        @"MSBuild executable path\s*=\s*(?<path>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TaskFailedLine = new(
        @"Done executing task ""(?<task>[^""]+)"" -- FAILED",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BareTaskFailedLine = new(
        @"^(?<task>.+?) -- FAILED\.?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex IssueCodeLine = new(
        @"(?<sev>error|warning)\s+(?<code>(?:MSB|NU)\d+)\s*:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BuildingProject = new(
        @"Building project ""(?<proj>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ProjectInLog = new(
        @"Project ""(?<proj>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TargetInProject = new(
        @"Target ""(?<target>[^""]+)"" in project ""(?<proj>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? TryGetMsBuildExecutablePath(string combinedOutput)
    {
        if (string.IsNullOrWhiteSpace(combinedOutput))
        {
            return null;
        }

        foreach (var raw in combinedOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = MsBuildExecutablePath.Match(raw.Trim());
            if (match.Success)
            {
                return match.Groups["path"].Value.Trim();
            }
        }

        return null;
    }

    public static void AppendKeyLinesSection(StringBuilder sb, string combinedOutput)
    {
        var highlights = CollectHighlights(combinedOutput);
        if (highlights.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("### Key lines from MSBuild log");
        foreach (var line in highlights)
        {
            sb.AppendLine($"- {line}");
        }
    }

    public static IReadOnlyList<string> CollectHighlights(string combinedOutput)
    {
        var lines = combinedOutput.Split(['\r', '\n'], StringSplitOptions.None);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || seen.Contains(trimmed))
            {
                return;
            }

            seen.Add(trimmed);
            result.Add(trimmed);
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || DotNetBuildDiagnosticParser.IsSectionHeaderLine(line))
            {
                continue;
            }

            if (MsBuildExecutablePath.IsMatch(line))
            {
                Add(line);
            }

            if (IssueCodeLine.IsMatch(line))
            {
                Add(line);
            }

            if (line.Contains("GHSA-", StringComparison.OrdinalIgnoreCase)
                || line.Contains("vulnerability", StringComparison.OrdinalIgnoreCase))
            {
                Add(line);
            }
        }

        AppendTaskFailureBlocks(lines, Add);

        if (result.Count > MaxHighlightLines)
        {
            return result.Take(MaxHighlightLines).ToList();
        }

        return result;
    }

    private static void AppendTaskFailureBlocks(string[] lines, Action<string> add)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var taskName = TryGetFailedTaskName(line);
            if (taskName is null)
            {
                continue;
            }

            var project = FindProjectContext(lines, i);
            add(string.IsNullOrEmpty(project)
                ? $"Task failed: `{taskName}`"
                : $"Task failed: `{taskName}` (project `{project}`)");

            add(line);
            var start = Math.Max(0, i - ContextLinesBeforeTaskFailure);
            for (var j = start; j < i; j++)
            {
                var ctx = lines[j].Trim();
                if (ctx.Length > 0)
                {
                    add($"  > {ctx}");
                }
            }
        }
    }

    private static string? TryGetFailedTaskName(string line)
    {
        var done = TaskFailedLine.Match(line);
        if (done.Success)
        {
            return done.Groups["task"].Value;
        }

        var bare = BareTaskFailedLine.Match(line);
        if (bare.Success && !line.StartsWith("Build FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return bare.Groups["task"].Value.Trim();
        }

        return null;
    }

    private static string? FindProjectContext(string[] lines, int failureIndex)
    {
        var start = Math.Max(0, failureIndex - ScanBackwardForProjectLines);
        for (var j = failureIndex - 1; j >= start; j--)
        {
            var line = lines[j].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var target = TargetInProject.Match(line);
            if (target.Success)
            {
                return target.Groups["proj"].Value;
            }

            var building = BuildingProject.Match(line);
            if (building.Success)
            {
                return building.Groups["proj"].Value;
            }

            var project = ProjectInLog.Match(line);
            if (project.Success && project.Groups["proj"].Value.Contains('.', StringComparison.Ordinal))
            {
                return project.Groups["proj"].Value;
            }
        }

        return null;
    }
}
