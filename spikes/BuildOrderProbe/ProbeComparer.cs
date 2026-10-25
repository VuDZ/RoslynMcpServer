using System.Globalization;
using System.Text;
using System.Text.Json;
using BuildOrderProbe.Models;

namespace BuildOrderProbe;

/// <summary>
/// Compares the runs of a result folder (<c>&lt;variant&gt;-round&lt;N&gt;.json</c>; round 0 is warmup). A variant is an
/// order with its worker count, window and garbage collector mode. Every run must build the same projects and target
/// frameworks with the same fingerprints as the first client run; timings are medians of rounds 1 and later, and
/// evaluation slowdowns are against the references-first runs with the workstation collector. Writes summary.md
/// without paths, and mismatches-private.json with the differing keys when there are any.
/// </summary>
internal static class ProbeComparer
{
    public static (string Summary, bool Identical) Run(string directory)
    {
        var runs = Directory.GetFiles(directory, "*-round*.json")
            .Select(path => (Name: Path.GetFileNameWithoutExtension(path), Run: JsonSerializer.Deserialize<ProbeRun>(File.ReadAllText(path))!))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var reference = runs.FirstOrDefault(item => item.Run.Order == nameof(LoadOrder.Client)).Run
            ?? throw new InvalidDataException($"No client run in {directory}.");
        var mismatches = Mismatches(runs, reference);
        var measured = runs.Where(item => !item.Name.EndsWith("-round0", StringComparison.Ordinal)).Select(item => item.Run).ToArray();
        var variants = measured
            .GroupBy(run => (run.Order, run.Workers, run.Window, run.ServerGc))
            .OrderBy(group => (int)Enum.Parse<LoadOrder>(group.Key.Order))
            .ThenBy(group => group.Key.ServerGc)
            .ThenBy(group => group.Key.Workers)
            .ThenBy(group => group.Key.Window == 0 ? int.MaxValue : group.Key.Window)
            .ToArray();
        var clientWall = Median(measured.Where(run => run.Order == nameof(LoadOrder.Client) && !run.ServerGc).Select(run => run.WallMs));
        var sequential = StepMetrics.EvaluationDurations(measured.Where(run => run.Order == nameof(LoadOrder.ReferencesFirst) && !run.ServerGc));

        var text = new StringBuilder();
        text.AppendLine("# Build order probe (BuildHost/task-02, step 0)").AppendLine();
        var multiTargeting = reference.BuildSteps.Count(step => step.MultiTargeting);
        text.AppendLine($"- runs: {runs.Length}, round 0 is warmup; {reference.Builds} design-time builds of {reference.BuildSteps.Count} projects, " +
            $"{multiTargeting} of them multi-targeting; logical processors: {reference.LogicalProcessors}");
        text.AppendLine(mismatches.Count == 0
            ? $"- results: **identical** — every run built the same {reference.Fingerprints.Count} project and target framework results as the first client run"
            : $"- results: **differ** in {mismatches.Count} run(s); keys in mismatches-private.json");
        text.AppendLine().AppendLine("| order | workers | window | server GC | wall ms (median) | against client | builds ms | not building ms | root evaluations ms (summed) | " +
            "evaluations on build thread | evaluations during builds | evaluation time overlapping builds | evaluations at once (mean) | evaluation slowdown, one TFM | " +
            "evaluation slowdown, multi-TFM | GC pause ms | GCs gen 0/1/2 | peak working set MB | wall per round |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var variant in variants)
        {
            var group = variant.ToArray();
            var wall = Median(group.Select(run => run.WallMs));
            text.AppendLine(
                $"| {variant.Key.Order} | {(variant.Key.Workers == 0 ? "—" : variant.Key.Workers)} | {WindowLabel(variant.Key.Order, variant.Key.Window)} | {(variant.Key.ServerGc ? "yes" : "no")} | {Format(wall)} | {(wall / clientWall).ToString("0.000", CultureInfo.InvariantCulture)} | " +
                $"{Format(Median(group.Select(run => run.BuildMs)))} | {Format(Median(group.Select(StepMetrics.NotBuildingMs)))} | " +
                $"{Format(Median(group.Select(run => run.RootEvaluationMs)))} | {Format(Median(group.Select(run => (double)StepMetrics.EvaluationsOnBuildThread(run))))} | " +
                $"{string.Join(" / ", group.Select(run => run.EvaluationsDuringBuilds).Distinct())} | " +
                $"{Percent(Median(group.Select(StepMetrics.EvaluationOverlapWithBuilds)))} | {Ratio(Median(group.Select(StepMetrics.MeanConcurrentEvaluations)))} | " +
                $"{Ratio(Median(group.Select(run => StepMetrics.EvaluationSlowdown(run, sequential, multiTargeting: false))))} | " +
                $"{Ratio(Median(group.Select(run => StepMetrics.EvaluationSlowdown(run, sequential, multiTargeting: true))))} | " +
                $"{Format(Median(group.Select(run => run.GcPauseMs)))} | {GcCollections(group)} | {Format(Median(group.Select(run => run.PeakWorkingSetMb)))} | " +
                $"{string.Join(", ", group.Select(run => Format(run.WallMs)))} |");
        }

        text.AppendLine().AppendLine("Builds ms and root evaluations ms add up each build and evaluation; when evaluating ahead they overlap, and only wall is elapsed time.")
            .AppendLine("Not building ms is wall time the build thread spent waiting for evaluations or running them itself: what evaluating ahead can remove.")
            .AppendLine("Evaluation slowdown is the median, over projects, of a root evaluation's duration against the same project's in the references-first runs without server GC.")
            .AppendLine("Window is how many solution entries ahead of the build thread are queued; all means every entry at the start. GC pause and collections cover the wall time; " +
                "the peak working set is the whole process's; runs of older probe versions show 0 or n/a.");
        File.WriteAllText(Path.Combine(directory, "summary.md"), text.ToString());
        if (mismatches.Count > 0)
        {
            File.WriteAllText(Path.Combine(directory, "mismatches-private.json"), JsonSerializer.Serialize(mismatches, new JsonSerializerOptions { WriteIndented = true }));
        }

        return (text.ToString(), mismatches.Count == 0);
    }

    private static SortedDictionary<string, string[]> Mismatches((string Name, ProbeRun Run)[] runs, ProbeRun reference)
    {
        var mismatches = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (name, run) in runs)
        {
            var differing = reference.Fingerprints.Keys.Union(run.Fingerprints.Keys)
                .Where(key => reference.Fingerprints.GetValueOrDefault(key) != run.Fingerprints.GetValueOrDefault(key))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (differing.Length > 0)
            {
                mismatches[name] = differing;
            }
        }

        return mismatches;
    }

    private static string WindowLabel(string order, int window)
        => order != nameof(LoadOrder.EvaluateSolutionPreorder) ? "—" : window == 0 ? "all" : window.ToString(CultureInfo.InvariantCulture);

    // Median of each generation's count over the rounds.
    private static string GcCollections(ProbeRun[] group)
        => group.Any(run => run.GcCollections is null)
            ? "n/a"
            : string.Join("/", Enumerable.Range(0, 3).Select(generation => Format(Median(group.Select(run => (double)run.GcCollections[generation])))));

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

    private static string Percent(double share)
        => double.IsNaN(share) ? "n/a" : share.ToString("P0", CultureInfo.InvariantCulture);
}
