using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BuildHostFork;

/// <summary>
/// Compares the fork runs of a result folder with its original runs, as spikes/ParallelLoad/compare.py does:
/// a fork value is identical to the first original run, a natural variance when another original run had
/// the same value, and unexplained otherwise. Round 0 is warmup and only counts for equivalence.
/// </summary>
internal static class ObservationComparer
{
    public static string Run(string directory)
    {
        var originals = Load(directory, "original");
        var forks = Load(directory, "fork");
        if (originals.Count == 0 || forks.Count == 0)
        {
            throw new InvalidOperationException("Both original-round*.json and fork-round*.json are needed.");
        }

        var reference = originals[0].Document.RootElement;
        var seen = new Dictionary<(string Project, string Field), HashSet<string>>();
        foreach (var original in originals)
        {
            foreach (var (project, field, value) in Fields(original.Document.RootElement))
            {
                if (!seen.TryGetValue((project, field), out var values))
                {
                    seen[(project, field)] = values = new HashSet<string>(StringComparer.Ordinal);
                }

                values.Add(value);
            }
        }

        var originalDiagnostics = originals.Select(run => Diagnostics(run.Document.RootElement)).ToList();
        var everOriginal = originalDiagnostics.SelectMany(set => set).ToHashSet(StringComparer.Ordinal);
        var alwaysOriginal = originalDiagnostics.Aggregate((left, right) => left.Intersect(right, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));
        var referenceFields = Fields(reference).ToDictionary(item => (item.Project, item.Field), item => item.Value);

        var unexplained = new List<string>();
        var natural = 0;
        foreach (var fork in forks)
        {
            var forkFields = Fields(fork.Document.RootElement).ToDictionary(item => (item.Project, item.Field), item => item.Value);
            foreach (var key in referenceFields.Keys.Union(forkFields.Keys))
            {
                forkFields.TryGetValue(key, out var forkValue);
                referenceFields.TryGetValue(key, out var referenceValue);
                if (forkValue == referenceValue)
                {
                    continue;
                }

                if (forkValue is not null && seen.TryGetValue(key, out var values) && values.Contains(forkValue))
                {
                    natural++;
                    continue;
                }

                unexplained.Add($"round {fork.Round}: {key.Item1} {key.Item2}: original {Shorten(referenceValue)}, fork {Shorten(forkValue)}");
            }

            var diagnostics = Diagnostics(fork.Document.RootElement);
            unexplained.AddRange(diagnostics.Where(message => !everOriginal.Contains(message)).Select(message => $"round {fork.Round}: diagnostic only in fork: {Shorten(message)}"));
            unexplained.AddRange(alwaysOriginal.Where(message => !diagnostics.Contains(message)).Select(message => $"round {fork.Round}: diagnostic missing in fork: {Shorten(message)}"));
        }

        var answers = originals.Concat(forks).Select(run => run.Document.RootElement.GetProperty("FirstSearch").GetProperty("resultSha256").GetString()).Distinct().Count();
        var verdict = unexplained.Count > 0 || answers != 1 ? "different" : natural > 0 ? "natural variance only" : "identical";

        var md = new StringBuilder();
        md.AppendLine("# BuildHost fork baseline (BuildHost/task-00)");
        md.AppendLine();
        md.AppendLine($"- original BuildHost: {originals[0].Document.RootElement.GetProperty("BuildHost").GetString()}; fork: {forks[0].Document.RootElement.GetProperty("BuildHost").GetString()}");
        md.AppendLine($"- runs: original {originals.Count}, fork {forks.Count} (round 0 is warmup; timings use rounds 1+)");
        md.AppendLine($"- projects in the first original run: {reference.GetProperty("Projects").EnumerateObject().Count()}");
        md.AppendLine($"- equivalence: **{verdict}**; natural-variance fields: {natural}; unexplained: {unexplained.Count}; distinct search answers: {answers}");
        md.AppendLine();
        md.AppendLine("| metric (median) | original | fork | fork / original |");
        md.AppendLine("|---|---|---|---|");
        AppendMetric(md, "load ms", originals, forks, run => run.GetProperty("WallMs").GetDouble());
        AppendMetric(md, "first search ms", originals, forks, run => run.GetProperty("FirstSearch").GetProperty("wallMs").GetDouble());
        AppendMetric(md, "client CPU ms", originals, forks, run => run.GetProperty("ClientCpuMs").GetDouble());
        AppendMetric(md, "client peak working set MB", originals, forks, run => run.GetProperty("ClientPeakWorkingSetMb").GetDouble());
        AppendMetric(md, "BuildHost processes", originals, forks, run => run.GetProperty("BuildHostProcesses").GetProperty("count").GetInt32());
        AppendMetric(md, "BuildHost CPU ms (lower bound)", originals, forks, run => run.GetProperty("BuildHostProcesses").GetProperty("cpuMs").GetDouble());
        AppendMetric(md, "BuildHost peak working set MB", originals, forks, run => run.GetProperty("BuildHostProcesses").GetProperty("peakWorkingSetMb").GetDouble());
        if (unexplained.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("Unexplained differences (first 30):");
            md.AppendLine();
            foreach (var line in unexplained.Take(30))
            {
                md.AppendLine("- " + line);
            }
        }

        var text = md.ToString();
        File.WriteAllText(Path.Combine(directory, "summary.md"), text);
        return text;
    }

    private static List<(int Round, JsonDocument Document)> Load(string directory, string side) =>
        Directory.GetFiles(directory, side + "-round*.json")
            .Select(path => (Round: int.Parse(Path.GetFileNameWithoutExtension(path)[(side.Length + "-round".Length)..], CultureInfo.InvariantCulture), Path: path))
            .OrderBy(item => item.Round)
            .Select(item => (item.Round, JsonDocument.Parse(File.ReadAllText(item.Path))))
            .ToList();

    private static IEnumerable<(string Project, string Field, string Value)> Fields(JsonElement run)
    {
        foreach (var project in run.GetProperty("Projects").EnumerateObject())
        {
            foreach (var field in project.Value.EnumerateObject())
            {
                yield return (project.Name, field.Name, field.Value.GetString() ?? "");
            }
        }
    }

    private static HashSet<string> Diagnostics(JsonElement run) =>
        run.GetProperty("Diagnostics").EnumerateArray().Select(item => item.GetString() ?? "").ToHashSet(StringComparer.Ordinal);

    private static void AppendMetric(StringBuilder md, string name, List<(int Round, JsonDocument Document)> originals,
        List<(int Round, JsonDocument Document)> forks, Func<JsonElement, double> read)
    {
        var original = Median(originals.Where(run => run.Round > 0).Select(run => read(run.Document.RootElement)));
        var fork = Median(forks.Where(run => run.Round > 0).Select(run => read(run.Document.RootElement)));
        var ratio = original is > 0 && fork is not null ? (fork / original)?.ToString("0.000", CultureInfo.InvariantCulture) : "n/a";
        md.AppendLine($"| {name} | {Format(original)} | {Format(fork)} | {ratio} |");
    }

    private static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }

        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static string Format(double? value) => value is null ? "n/a" : Math.Round(value.Value).ToString(CultureInfo.InvariantCulture);

    private static string Shorten(string? value) => value is null ? "(missing)" : value.Length > 160 ? value[..160] + "…" : value;
}
