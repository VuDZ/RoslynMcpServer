using System.Globalization;
using System.Text;
using System.Text.Json;
using BuildOrderProbe.Models;

namespace BuildOrderProbe;

/// <summary>
/// Summarizes the <see cref="SdkCacheProbe"/> runs of a result folder: timed runs <c>sdk-cache-&lt;mode&gt;-round&lt;N&gt;.json</c>
/// (round 0 is warmup) and attribution runs <c>sdk-cache-&lt;mode&gt;-events.json</c>. Medians of each kind of step per
/// mode against isolated; whether every run produced the fingerprints of the first measured isolated run, with
/// isolated round 2 as the natural variance; and, from the attribution runs, the share of SDK resolution in each kind
/// of step and the time per resolver. Writes summary.md without paths and mismatches-private.json with them.
/// </summary>
internal static class SdkCacheComparer
{
    public static (string Summary, bool Identical) Run(string directory)
    {
        var timed = Load(directory, "sdk-cache-*-round*.json")
            .Where(run => !run.File.EndsWith("-round0.json", StringComparison.Ordinal))
            .ToArray();
        var isolated = timed.Where(run => run.Run.Mode == nameof(SdkCacheMode.Isolated)).OrderBy(run => run.File, StringComparer.Ordinal).ToArray();
        if (isolated.Length == 0)
        {
            throw new InvalidDataException($"No measured isolated run in {directory}.");
        }

        var reference = isolated[0];
        var attributions = Load(directory, "sdk-cache-*-events.json").ToArray();

        var text = new StringBuilder();
        text.AppendLine("# SDK resolution cache (BuildHost/task-04)").AppendLine();
        text.AppendLine($"- MSBuild {reference.Run.MsBuildVersion}, {reference.Run.LogicalProcessors} logical processors; " +
            $"{reference.Run.Projects} projects, {reference.Run.MultiTargetingProjects} multi-targeting; " +
            $"measured runs {timed.Length}, round 0 is warmup");
        var mismatches = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var identical = AppendEquivalence(text, timed, reference, mismatches);
        AppendTimes(text, timed);
        AppendAttribution(text, attributions);

        var summary = text.ToString();
        File.WriteAllText(Path.Combine(directory, "summary.md"), summary);
        File.WriteAllText(Path.Combine(directory, "mismatches-private.json"), JsonSerializer.Serialize(mismatches, _options));
        return (summary, identical);
    }

    private static bool AppendEquivalence(StringBuilder text, IReadOnlyList<(string File, SdkCacheRun Run)> timed, (string File, SdkCacheRun Run) reference, IDictionary<string, IReadOnlyList<string>> mismatches)
    {
        var identical = true;
        text.AppendLine().AppendLine("| run | differing fingerprints against isolated round 1, by kind |").AppendLine("|---|---|");
        foreach (var (file, run) in timed.OrderBy(run => run.File, StringComparer.Ordinal))
        {
            if (file == reference.File)
            {
                continue;
            }

            var keys = reference.Run.Fingerprints.Keys.Union(run.Fingerprints.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key => reference.Run.Fingerprints.GetValueOrDefault(key) != run.Fingerprints.GetValueOrDefault(key))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            identical &= keys.Length == 0;
            mismatches[file] = keys;
            var byKind = keys.GroupBy(key => key[..key.IndexOf('|')], StringComparer.Ordinal)
                .Select(group => $"{group.Key} {group.Count()}");
            text.AppendLine($"| {Path.GetFileNameWithoutExtension(file)} | {(keys.Length == 0 ? "**identical**" : string.Join(", ", byKind))} |");
        }

        text.AppendLine().AppendLine($"Result: **{(identical ? "identical" : "different")}**; fingerprints of {reference.Run.Fingerprints.Count} steps per run.");
        return identical;
    }

    private static void AppendTimes(StringBuilder text, IReadOnlyList<(string File, SdkCacheRun Run)> timed)
    {
        string[] phases = [SdkCacheProbe.RootEvaluation, SdkCacheProbe.TargetFrameworkEvaluation, SdkCacheProbe.Globs, SdkCacheProbe.DesignTimeBuild];
        var modes = timed.GroupBy(run => run.Run.Mode).OrderBy(group => Enum.Parse<SdkCacheMode>(group.Key)).ToArray();
        var isolated = modes.Single(group => group.Key == nameof(SdkCacheMode.Isolated)).Select(run => run.Run).ToArray();

        text.AppendLine().AppendLine("Medians of the measured rounds, ms; in parentheses the ratio to isolated.").AppendLine();
        text.AppendLine($"| mode | wall | {string.Join(" | ", phases)} | wall per round |");
        text.AppendLine($"|---|---|{string.Concat(phases.Select(_ => "---|"))}---|");
        foreach (var mode in modes)
        {
            var runs = mode.Select(run => run.Run).ToArray();
            var cells = phases.Select(phase => Cell(Median(runs.Select(run => PhaseMs(run, phase))), Median(isolated.Select(run => PhaseMs(run, phase)))));
            text.AppendLine($"| {mode.Key} | {Cell(Median(runs.Select(run => run.WallMs)), Median(isolated.Select(run => run.WallMs)))} | " +
                $"{string.Join(" | ", cells)} | {string.Join(", ", runs.Select(run => Format(run.WallMs)))} |");
        }

        var counts = isolated[0].Phases.Select(phase => $"{phase.Key} {phase.Value.Count}");
        text.AppendLine().AppendLine($"Steps per run: {string.Join(", ", counts)}.");
    }

    private static void AppendAttribution(StringBuilder text, IReadOnlyList<(string File, SdkCacheRun Run)> attributions)
    {
        if (attributions.Count == 0)
        {
            return;
        }

        text.AppendLine().AppendLine("## SDK resolution inside each kind of step (attribution runs, slower than timed runs)").AppendLine();
        text.AppendLine("| mode | step | step ms | SDK resolution ms | share |").AppendLine("|---|---|---|---|---|");
        foreach (var (_, run) in attributions.OrderBy(run => Enum.Parse<SdkCacheMode>(run.Run.Mode)))
        {
            foreach (var (phase, total) in run.Phases)
            {
                var sdk = run.SdkResolutionMs.GetValueOrDefault(phase);
                text.AppendLine($"| {run.Mode} | {phase} ({total.Count}) | {Format(total.Ms)} | {Format(sdk)} | {Percent(sdk / total.Ms)} |");
            }
        }

        text.AppendLine().AppendLine("| mode | resolutions (from the service's cache) | resolver ms |").AppendLine("|---|---|---|");
        foreach (var (_, run) in attributions.OrderBy(run => Enum.Parse<SdkCacheMode>(run.Run.Mode)))
        {
            var resolvers = run.ResolverMs.OrderByDescending(resolver => resolver.Value).Select(resolver => $"{resolver.Key} {Format(resolver.Value)}");
            text.AppendLine($"| {run.Mode} | {run.SdkResolutions} ({run.CachedSdkResolutions}) | {string.Join(", ", resolvers)} |");
        }
    }

    private static IEnumerable<(string File, SdkCacheRun Run)> Load(string directory, string pattern)
        => Directory.GetFiles(directory, pattern)
            .Select(path => (Path.GetFileName(path), JsonSerializer.Deserialize<SdkCacheRun>(File.ReadAllText(path))!));

    private static double PhaseMs(SdkCacheRun run, string phase)
        => run.Phases.TryGetValue(phase, out var total) ? total.Ms : double.NaN;

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Where(value => !double.IsNaN(value)).Order().ToArray();
        return sorted.Length == 0 ? double.NaN
            : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static string Cell(double value, double isolated)
        => double.IsNaN(value) ? "n/a" : $"{Format(value)} (×{(value / isolated).ToString("0.000", CultureInfo.InvariantCulture)})";

    private static string Format(double value)
        => double.IsNaN(value) ? "n/a" : value.ToString("0", CultureInfo.InvariantCulture);

    private static string Percent(double value)
        => double.IsNaN(value) || double.IsInfinity(value) ? "n/a" : (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + " %";

    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };
}
