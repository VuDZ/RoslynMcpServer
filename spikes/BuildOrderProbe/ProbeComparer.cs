using System.Globalization;
using System.Text;
using System.Text.Json;
using BuildOrderProbe.Models;

namespace BuildOrderProbe;

/// <summary>
/// Compares the runs of a result folder (<c>&lt;order&gt;-round&lt;N&gt;.json</c>; round 0 is warmup). Every run must
/// build the same projects and target frameworks with the same fingerprints as the first client run; timings are
/// medians of rounds 1 and later. Writes summary.md without paths, and mismatches-private.json with the differing
/// keys when there are any.
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

        var text = new StringBuilder();
        text.AppendLine("# Build order probe (BuildHost/task-02, step 0)").AppendLine();
        text.AppendLine($"- runs: {runs.Length}, round 0 is warmup; {reference.Builds} design-time builds per run; logical processors: {reference.LogicalProcessors}");
        text.AppendLine(mismatches.Count == 0
            ? $"- results: **identical** — every run built the same {reference.Fingerprints.Count} project and target framework results as the first client run"
            : $"- results: **differ** in {mismatches.Count} run(s); keys in mismatches-private.json");
        text.AppendLine().AppendLine("| order | workers | wall ms (median) | against client | builds ms | root evaluations ms (summed) | evaluations during builds | wall per round |");
        text.AppendLine("|---|---|---|---|---|---|---|---|");
        var clientWall = Median(Measured(runs, nameof(LoadOrder.Client)).Select(run => run.WallMs));
        foreach (var order in Enum.GetNames<LoadOrder>())
        {
            var measured = Measured(runs, order);
            if (measured.Length == 0)
            {
                continue;
            }

            var wall = Median(measured.Select(run => run.WallMs));
            text.AppendLine($"| {order} | {measured[0].Workers} | {Format(wall)} | {(wall / clientWall).ToString("0.000", CultureInfo.InvariantCulture)} | " +
                $"{Format(Median(measured.Select(run => run.BuildMs)))} | {Format(Median(measured.Select(run => run.RootEvaluationMs)))} | " +
                $"{string.Join(" / ", measured.Select(run => run.EvaluationsDuringBuilds).Distinct())} | {string.Join(", ", measured.Select(run => Format(run.WallMs)))} |");
        }

        text.AppendLine().AppendLine("Builds ms and root evaluations ms add up each build and evaluation; with evaluate-ahead they overlap, and only wall is elapsed time.");
        File.WriteAllText(Path.Combine(directory, "summary.md"), text.ToString());
        if (mismatches.Count > 0)
        {
            File.WriteAllText(Path.Combine(directory, "mismatches-private.json"), JsonSerializer.Serialize(mismatches, new JsonSerializerOptions { WriteIndented = true }));
        }

        return (text.ToString(), mismatches.Count == 0);
    }

    private static ProbeRun[] Measured((string Name, ProbeRun Run)[] runs, string order)
        => runs.Where(item => item.Run.Order == order && !item.Name.EndsWith("-round0", StringComparison.Ordinal)).Select(item => item.Run).ToArray();

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? double.NaN
            : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static string Format(double value)
        => value.ToString("0", CultureInfo.InvariantCulture);
}
