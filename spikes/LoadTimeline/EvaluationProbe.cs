using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;

namespace LoadTimeline;

/// <summary>
/// Evaluates every project of a solution the way BuildHost loads it: the project once, then once per target
/// framework of a multi-targeting project. BuildHost does not pass its loggers to these evaluations, so they
/// are missing from the binlog; this probe measures them in a separate process as an estimate, not as the
/// time inside the measured load.
/// </summary>
internal static class EvaluationProbe
{
    public static string Run(string solutionPath)
    {
        var solution = SolutionFile.Parse(solutionPath);
        var projects = solution.ProjectsInOrder
            .Where(project => project.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat
                && project.AbsolutePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(project => project.AbsolutePath)
            .ToList();
        using var collection = new ProjectCollection();
        var rows = new List<(string Project, int Evaluations, double Ms)>();
        var total = Stopwatch.StartNew();
        foreach (var path in projects)
        {
            var watch = Stopwatch.StartNew();
            var evaluations = 1;
            var project = collection.LoadProject(path);
            var frameworks = project.GetPropertyValue("TargetFrameworks");
            if (!string.IsNullOrWhiteSpace(frameworks))
            {
                foreach (var framework in frameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    collection.LoadProject(path, new Dictionary<string, string> { ["TargetFramework"] = framework }, toolsVersion: null);
                    evaluations++;
                }
            }

            rows.Add((Path.GetRelativePath(Path.GetDirectoryName(solutionPath)!, path), evaluations, watch.Elapsed.TotalMilliseconds));
        }

        var md = new StringBuilder();
        md.AppendLine("# Evaluation probe (BuildHost/task-00a)");
        md.AppendLine();
        md.AppendLine($"- C# projects: {rows.Count}; evaluations: {rows.Sum(row => row.Evaluations)}; total {Ms(total.Elapsed.TotalMilliseconds)} ms in one ProjectCollection");
        md.AppendLine("- Separate process and a fresh collection; compare with the binlog gaps only as an estimate.");
        md.AppendLine();
        md.AppendLine("| project (top 15) | evaluations | ms |");
        md.AppendLine("|---|---|---|");
        foreach (var row in rows.OrderByDescending(row => row.Ms).Take(15))
        {
            md.AppendLine($"| `{row.Project}` | {row.Evaluations} | {Ms(row.Ms)} |");
        }

        return md.ToString();
    }

    private static string Ms(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);
}
