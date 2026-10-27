using System.Globalization;
using System.Text;
using System.Text.Json;
using BuildOrderProbe.Models;

namespace BuildOrderProbe;

/// <summary>
/// Summarizes the <see cref="EvaluationScaling"/> runs of a result folder (<c>evaluate-&lt;variant&gt;-round&lt;N&gt;.json</c>;
/// round 0 is warmup). The baseline is one worker in one collection with the host's way of evaluating: speedup is its median wall against a variant's,
/// and the time-weighted slowdown is the summed duration of a run's evaluations against the summed median durations of
/// the same projects with one worker. When evaluations run truly in parallel, the slowdown stays near 1 while several
/// run at once; when they wait for each other, it grows with the number at once. Writes summary.md without paths.
/// </summary>
internal static class ScalingComparer
{
    public static string Run(string directory)
    {
        var runs = Directory.GetFiles(directory, "evaluate-*-round*.json")
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith("-round0", StringComparison.Ordinal))
            .Select(path => JsonSerializer.Deserialize<ScalingRun>(File.ReadAllText(path))!)
            .ToArray();
        var baseline = runs.Where(run => run.Workers == 1 && !run.SeparateCollections && !run.UsesLoadProject).ToArray();
        if (baseline.Length == 0)
        {
            throw new InvalidDataException($"No measured one-worker run in {directory}.");
        }

        var sequential = baseline.SelectMany(run => run.Steps)
            .GroupBy(step => step.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => Median(group.Select(step => step.DurationMs)), StringComparer.OrdinalIgnoreCase);
        var baselineWall = Median(baseline.Select(run => run.WallMs));

        var text = new StringBuilder();
        text.AppendLine("# Evaluation scaling (BuildHost/task-02)").AppendLine();
        text.AppendLine($"- measured runs: {runs.Length}, round 0 is warmup; {baseline[0].Evaluations} projects evaluated, " +
            $"{baseline[0].Steps.Count(step => step.MultiTargeting)} of them multi-targeting, {baseline[0].ProjectsNotEvaluated} not evaluated; " +
            $"logical processors: {baseline[0].LogicalProcessors}");
        text.AppendLine().AppendLine("| evaluation | workers | wall ms (median) | speedup against 1 worker | evaluations at once (mean) | " +
            "time-weighted slowdown, one TFM | time-weighted slowdown, multi-TFM | wall per round |");
        text.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var variant in runs.GroupBy(run => (run.SeparateCollections, run.UsesLoadProject, run.Workers))
            .OrderBy(group => group.Key.Workers)
            .ThenBy(group => group.Key.UsesLoadProject)
            .ThenBy(group => group.Key.SeparateCollections))
        {
            var wall = Median(variant.Select(run => run.WallMs));
            var atOnce = Median(variant.Select(run => run.Steps.Sum(step => step.DurationMs) / run.WallMs));
            var label = variant.Key.UsesLoadProject ? "LoadProject, shared collection"
                : variant.Key.SeparateCollections ? "host, collection per worker"
                : "host, shared collection";
            text.AppendLine($"| {label} | {variant.Key.Workers} | {Format(wall)} | " +
                $"{Ratio(baselineWall / wall)} | {Ratio(atOnce)} | {Ratio(Median(variant.Select(run => Slowdown(run, sequential, multiTargeting: false))))} | " +
                $"{Ratio(Median(variant.Select(run => Slowdown(run, sequential, multiTargeting: true))))} | {string.Join(", ", variant.Select(run => Format(run.WallMs)))} |");
        }

        text.AppendLine();
        text.AppendLine("Evaluations at once is the summed duration of a run's evaluations over its wall time.");
        var summary = text.ToString();
        File.WriteAllText(Path.Combine(directory, "summary.md"), summary);
        return summary;
    }

    private static double Slowdown(ScalingRun run, Dictionary<string, double> sequential, bool multiTargeting)
    {
        var steps = run.Steps.Where(step => step.MultiTargeting == multiTargeting && sequential.ContainsKey(step.Path)).ToArray();
        var expected = steps.Sum(step => sequential[step.Path]);
        return expected > 0 ? steps.Sum(step => step.DurationMs) / expected : double.NaN;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Where(value => !double.IsNaN(value)).Order().ToArray();
        return sorted.Length == 0 ? double.NaN
            : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static string Format(double value)
        => double.IsNaN(value) ? "n/a" : value.ToString("0", CultureInfo.InvariantCulture);

    private static string Ratio(double value)
        => double.IsNaN(value) ? "n/a" : value.ToString("0.00", CultureInfo.InvariantCulture);
}
