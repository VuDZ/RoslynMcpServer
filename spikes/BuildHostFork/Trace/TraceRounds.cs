using System.Text;

namespace BuildHostFork.Trace;

/// <summary>
/// trace-summary: medians over the measured traced rounds of a baseline run (trace/fork-round1 and later), with the
/// warmup round shown on its own. Each round is a fresh client and BuildHost process, so every round pays JIT and
/// MSBuild startup; the warmup round alone also starts with cold file caches.
/// </summary>
internal static class TraceRounds
{
    public static string Summarize(string resultDirectory, bool redact)
    {
        var rounds = Directory.GetDirectories(Path.Combine(resultDirectory, "trace"), "fork-round*")
            .Select(directory => (Round: int.Parse(Path.GetFileName(directory)["fork-round".Length..], System.Globalization.CultureInfo.InvariantCulture), Directory: directory))
            .OrderBy(item => item.Round)
            .Select(item => (item.Round, Summary: TraceReport.Run(item.Directory, redact)))
            .ToArray();
        var measured = rounds.Where(item => item.Round > 0).Select(item => item.Summary).ToArray();
        var warmup = rounds.Where(item => item.Round == 0).Select(item => item.Summary).FirstOrDefault();
        if (measured.Length == 0)
        {
            throw new InvalidDataException($"No measured traced rounds under {resultDirectory}.");
        }

        var text = new StringBuilder();
        text.AppendLine("# Traced loads, fork side (BuildHost/task-01)").AppendLine();
        text.AppendLine($"- measured rounds: {measured.Length}; wall median {TraceReport.Format(Median(measured.Select(summary => summary.WallMs)))} ms" +
            (warmup is null ? "" : $"; warmup round {TraceReport.Format(warmup.WallMs)} ms"));
        text.AppendLine($"- clock check over all rounds: {rounds.Sum(item => item.Summary.Clock.Matched)} matched, {rounds.Sum(item => item.Summary.Clock.Unmatched)} unmatched, " +
            $"{rounds.Sum(item => item.Summary.Clock.Violations)} violations; min lead {TraceReport.Precise(rounds.Min(item => item.Summary.Clock.MinLeadMs))} ms, min tail {TraceReport.Precise(rounds.Min(item => item.Summary.Clock.MinTailMs))} ms");
        text.AppendLine($"- {measured[0].Projects} projects, {measured[0].Documents} documents, {measured[0].WorkspaceFailures} workspace failures, {measured[0].Hosts} traced host(s) per load").AppendLine();
        text.AppendLine("| category | median ms | share of median wall | min–max ms | warmup ms |").AppendLine("|---|---|---|---|---|");
        var wall = Median(measured.Select(summary => summary.WallMs));
        var categories = measured.SelectMany(summary => summary.Breakdown.Keys).Distinct().ToArray();
        foreach (var category in categories.OrderByDescending(category => Median(measured.Select(summary => summary.Breakdown.GetValueOrDefault(category)))))
        {
            var values = measured.Select(summary => summary.Breakdown.GetValueOrDefault(category)).ToArray();
            var median = Median(values);
            text.AppendLine($"| {category} | {TraceReport.Format(median)} | {TraceReport.Percent(median / wall)} | {TraceReport.Format(values.Min())}–{TraceReport.Format(values.Max())} | " +
                $"{(warmup is null ? "n/a" : TraceReport.Format(warmup.Breakdown.GetValueOrDefault(category)))} |");
        }

        text.AppendLine().AppendLine("Per-round reports with projects, repeats and the graph: trace/fork-round<N>/report.md.");
        File.WriteAllText(Path.Combine(resultDirectory, "trace-summary.md"), text.ToString());
        return text.ToString();
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
