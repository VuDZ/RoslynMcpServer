using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BuildHostFork;

/// <summary>
/// matrix: the result folder of Invoke-BuildHostForkMatrix.ps1 (BuildHost/task-02, stage 3), with the original runs
/// at its top and each fork variant in a subfolder of its own. Every variant is compared with the same original runs
/// and gets its own summary.md; matrix.md puts the medians of all sides side by side, as ratios to the original and
/// to the fork without optimizations, against which the thresholds of task-02 are set.
/// </summary>
internal static class MatrixSummary
{
    private const string TimeReference = "fork";

    /// <param name="directory">The matrix result folder; matrix.md is written there.</param>
    /// <param name="variants">The variant subfolders in the order of the table.</param>
    /// <returns>The text of matrix.md and whether every variant is shown equivalent to the original.</returns>
    public static (string Summary, bool Equivalent) Run(string directory, IReadOnlyList<string> variants)
    {
        var rows = new List<Row> { Measure("original", "reference", ObservationComparer.Load(directory, "original")) };

        // One variant at a time: a run of a large solution is tens of megabytes of JSON, so only its numbers are kept.
        var notEquivalent = new List<string>();
        foreach (var variant in variants)
        {
            var variantDirectory = Path.Combine(directory, variant);
            var (_, equivalent, verdict) = ObservationComparer.Run(variantDirectory, directory);
            if (!equivalent)
            {
                notEquivalent.Add(variant);
            }

            rows.Add(Measure(variant, equivalent ? verdict : $"**not shown equivalent** ({verdict})", ObservationComparer.Load(variantDirectory, "fork")));
        }

        var text = Summarize(rows, notEquivalent);
        File.WriteAllText(Path.Combine(directory, "matrix.md"), text);
        return (text, notEquivalent.Count == 0);
    }

    private static Row Measure(string side, string answer, List<(int Round, JsonElement Root)> runs)
    {
        var measured = runs.Where(run => run.Round > 0).Select(run => run.Root).ToList();
        double? Median(Func<JsonElement, double> read) => ObservationComparer.Median(measured.Select(read));
        return new Row(
            side,
            answer,
            Median(run => run.GetProperty("WallMs").GetDouble()),
            Median(run => run.GetProperty("BuildHostProcesses").GetProperty("cpuMs").GetDouble()),
            Median(run => run.GetProperty("BuildHostProcesses").GetProperty("peakWorkingSetMb").GetDouble()),
            Median(run => run.GetProperty("ClientCpuMs").GetDouble()),
            Median(run => run.GetProperty("ClientPeakWorkingSetMb").GetDouble()),
            measured.Select(run => run.GetProperty("WallMs").GetDouble()).ToList());
    }

    private static string Summarize(List<Row> rows, List<string> notEquivalent)
    {
        var original = rows[0];
        var reference = rows.FirstOrDefault(row => row.Side == TimeReference);
        var md = new StringBuilder();
        md.AppendLine("# BuildHost fork matrix (BuildHost/task-02, stage 3)");
        md.AppendLine();
        md.AppendLine($"- sides: {string.Join(", ", rows.Select(row => row.Side))}; each round runs every side once, in an order rotated by one side per round; round 0 is warmup");
        md.AppendLine("- answer: each variant against the original runs by the rules of task-00, details in <variant>/summary.md");
        md.AppendLine($"- result: **{(notEquivalent.Count == 0 ? "all variants equivalent" : "not shown equivalent: " + string.Join(", ", notEquivalent))}**");
        md.AppendLine(reference is null
            ? $"- no `{TimeReference}` side, so there are no ratios to the fork without optimizations"
            : $"- `/ {TimeReference}`: ratio to the fork without optimizations, the reference of the task-02 thresholds");
        md.AppendLine();
        md.AppendLine($"| side | answer | load ms | / original | / {TimeReference} | BuildHost CPU / {TimeReference} | BuildHost peak / {TimeReference} | client CPU / {TimeReference} | client peak / {TimeReference} |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var row in rows)
        {
            md.AppendLine($"| {row.Side} | {row.Answer} | {ObservationComparer.Format(row.LoadMs)} | {Ratio(row.LoadMs, original.LoadMs)} | " +
                $"{Ratio(row.LoadMs, reference?.LoadMs)} | {Ratio(row.HostCpuMs, reference?.HostCpuMs)} | {Ratio(row.HostPeakMb, reference?.HostPeakMb)} | " +
                $"{Ratio(row.ClientCpuMs, reference?.ClientCpuMs)} | {Ratio(row.ClientPeakMb, reference?.ClientPeakMb)} |");
        }

        md.AppendLine();
        md.AppendLine("Per-round load ms (rounds 1+):");
        md.AppendLine();
        foreach (var row in rows)
        {
            md.AppendLine($"- {row.Side}: {string.Join(", ", row.LoadsMs.Select(load => ObservationComparer.Format(load)))}");
        }

        return md.ToString();
    }

    private static string Ratio(double? value, double? baseline) =>
        value is not null && baseline is > 0 ? (value.Value / baseline.Value).ToString("0.000", CultureInfo.InvariantCulture) : "n/a";

    private sealed record Row(
        string Side,
        string Answer,
        double? LoadMs,
        double? HostCpuMs,
        double? HostPeakMb,
        double? ClientCpuMs,
        double? ClientPeakMb,
        List<double> LoadsMs);
}
