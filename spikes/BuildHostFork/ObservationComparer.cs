using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BuildHostFork;

/// <summary>
/// Compares the fork runs of a result folder with its original runs by the rules of spikes/ParallelLoad/compare.py.
/// The reference is the first measured original run (round 1; round 0 is warmup). Natural variance is only what
/// differs between original rounds 1 and 2: a difference from the reference is natural when the field, or the
/// presence of the diagnostic, took the same value in one of those two rounds, and unexplained otherwise. Original
/// rounds 3+ are checked against the same rule, so an original that is not stable shows up. Stricter than
/// compare.py: no case-only path exemption, the fields of a project present on one side only are compared too, one
/// search answer for every run, and every run must be finished with BuildHost processes recorded, from that side's
/// own folder and exited after the first search.
/// </summary>
internal static class ObservationComparer
{
    /// <param name="directory">The folder with fork-round*.json; summary.md is written there.</param>
    /// <param name="originalDirectory">The folder with original-round*.json when it is not <paramref name="directory"/>:
    /// a matrix run keeps one set of original runs for all its fork variants.</param>
    /// <returns>The summary, also written to summary.md, whether the fork is shown equivalent, and the verdict on the
    /// fork's answer alone: identical, natural variance only, or different.</returns>
    public static (string Summary, bool Equivalent, string Verdict) Run(string directory, string? originalDirectory = null)
    {
        var originals = Load(originalDirectory ?? directory, "original");
        var forks = Load(directory, "fork");
        var measuredOriginals = originals.Where(run => run.Round > 0).ToList();
        var measuredForks = forks.Where(run => run.Round > 0).ToList();
        if (measuredOriginals.Count == 0 || measuredForks.Count == 0)
        {
            throw new InvalidOperationException("Measured original-round*.json and fork-round*.json (round 1+) are needed.");
        }

        var reference = measuredOriginals[0];
        var natural = measuredOriginals.Count > 1 ? Differences(reference.Root, measuredOriginals[1].Root) : [];
        var allowed = natural.ToDictionary(item => (item.Key, item.Category), item => new[] { item.Before, item.After });
        var original = Classify(reference, measuredOriginals.Skip(2), allowed);
        var fork = Classify(reference, measuredForks, allowed);
        var answers = new Answers(SearchAnswers(originals), SearchAnswers(forks));
        var runIssues = originals.Select(run => ("original", run)).Concat(forks.Select(run => ("fork", run))).SelectMany(item => RunIssues(item.Item1, item.run)).ToList();

        var verdict = fork.Unexplained.Count > 0 || answers.ForkDiffers ? "different" : fork.Natural > 0 ? "natural variance only" : "identical";
        var reasons = new List<string>();
        if (verdict == "different")
        {
            reasons.Add("the fork differs from the original");
        }

        if (original.Unexplained.Count > 0)
        {
            reasons.Add("the original is not stable between its own runs");
        }

        if (answers.Original.Count > 1)
        {
            reasons.Add("the search answer of the original is not stable between its own runs");
        }

        if (runIssues.Count > 0)
        {
            reasons.Add("run or BuildHost process check failed");
        }

        var text = Summarize(originals, forks, reference, natural, original, fork, answers, verdict, runIssues, reasons);
        File.WriteAllText(Path.Combine(directory, "summary.md"), text);
        return (text, reasons.Count == 0, verdict);
    }

    internal static List<(int Round, JsonElement Root)> Load(string directory, string side) =>
        Directory.GetFiles(directory, side + "-round*.json")
            .Select(path => (Round: int.Parse(Path.GetFileNameWithoutExtension(path)[(side.Length + "-round".Length)..], CultureInfo.InvariantCulture), Path: path))
            .OrderBy(item => item.Round)
            .Select(item => (item.Round, JsonDocument.Parse(File.ReadAllText(item.Path)).RootElement))
            .ToList();

    internal static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }

        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    internal static string Format(double? value) => value is null ? "n/a" : Math.Round(value.Value).ToString(CultureInfo.InvariantCulture);

    // compare.py differences(): changed fields of projects, added or removed projects, and diagnostics in only one of
    // the runs. Before and After are the JSON values (null when absent), or presence for project keys and diagnostics.
    // compare.py skips the fields of a project present in one run only, so a project that appears between original
    // rounds 1 and 2 would admit any field values in the fork; here they are compared against absent values.
    private static List<Difference> Differences(JsonElement left, JsonElement right)
    {
        var result = new List<Difference>();
        var leftProjects = Projects(left);
        var rightProjects = Projects(right);
        foreach (var key in leftProjects.Keys.Union(rightProjects.Keys).Order(StringComparer.Ordinal))
        {
            var leftFields = leftProjects.GetValueOrDefault(key);
            var rightFields = rightProjects.GetValueOrDefault(key);
            if (leftFields is null || rightFields is null)
            {
                result.Add(new Difference(key, "ProjectKeys", Presence(leftFields is not null), Presence(rightFields is not null)));
            }

            var fields = (leftFields?.Keys ?? Enumerable.Empty<string>()).Union(rightFields?.Keys ?? Enumerable.Empty<string>()).Order(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                var before = leftFields?.GetValueOrDefault(field);
                var after = rightFields?.GetValueOrDefault(field);
                if (before != after)
                {
                    result.Add(new Difference(key, field, before, after));
                }
            }
        }

        var leftDiagnostics = Diagnostics(left);
        var rightDiagnostics = Diagnostics(right);
        foreach (var message in leftDiagnostics.Union(rightDiagnostics).Where(message => leftDiagnostics.Contains(message) != rightDiagnostics.Contains(message)).Order(StringComparer.Ordinal))
        {
            result.Add(new Difference(message, "Diagnostics", Presence(leftDiagnostics.Contains(message)), Presence(rightDiagnostics.Contains(message))));
        }

        return result;
    }

    // compare.py compare(): a difference from the reference is natural when its new value is one of the two values
    // that field had between original rounds 1 and 2.
    private static Classification Classify(
        (int Round, JsonElement Root) reference,
        IEnumerable<(int Round, JsonElement Root)> runs,
        Dictionary<(string Key, string Category), string?[]> allowed)
    {
        var natural = 0;
        var unexplained = new List<string>();
        foreach (var run in runs)
        {
            foreach (var difference in Differences(reference.Root, run.Root))
            {
                if (allowed.TryGetValue((difference.Key, difference.Category), out var values) && values.Contains(difference.After))
                {
                    natural++;
                    continue;
                }

                unexplained.Add($"round {run.Round}: {difference.Key} {difference.Category}: reference {Shorten(difference.Before)}, this run {Shorten(difference.After)}");
            }
        }

        return new Classification(natural, unexplained);
    }

    private static HashSet<string> SearchAnswers(List<(int Round, JsonElement Root)> runs) =>
        runs.Select(run => run.Root.GetProperty("FirstSearch").GetProperty("resultSha256").GetString() ?? "(none)").ToHashSet(StringComparer.Ordinal);

    // Results written by the spike before the review lack the process fields. Their absence is an issue, not a
    // passed check: the old sampler counted processes by name, so it proves neither their folder nor their exit.
    private static IEnumerable<string> RunIssues(string side, (int Round, JsonElement Root) run)
    {
        var label = $"{side} round {run.Round}";
        var status = run.Root.TryGetProperty("Status", out var statusValue) ? statusValue.GetString() : null;
        if (status != "ok")
        {
            yield return $"{label}: status {status ?? "(missing)"}, not a finished load";
        }

        var processes = run.Root.GetProperty("BuildHostProcesses");
        if (!run.Root.TryGetProperty("RemainingBuildHosts", out var remaining)
            || !run.Root.TryGetProperty("ExpectedHost", out var expected)
            || !processes.TryGetProperty("unidentified", out var unidentified)
            || !processes.TryGetProperty("hosts", out var hosts))
        {
            yield return $"{label}: BuildHost processes not recorded (results of an older spike), not checked";
            yield break;
        }

        if (remaining.GetInt32() > 0)
        {
            yield return $"{label}: {remaining.GetInt32()} BuildHost process(es) still running 10 s after the first search";
        }

        if (unidentified.GetInt32() > 0)
        {
            yield return $"{label}: {unidentified.GetInt32()} child dotnet process(es) with an unreadable command line";
        }

        var netcore = hosts.EnumerateArray().Select(host => host.GetString() ?? "").Where(host => host.StartsWith("netcore: ", StringComparison.Ordinal)).ToList();
        if (netcore.Count == 0)
        {
            yield return $"{label}: no netcore BuildHost process was observed";
        }

        foreach (var host in netcore.Where(host => !string.Equals(host, expected.GetString(), StringComparison.OrdinalIgnoreCase)))
        {
            yield return $"{label}: BuildHost from another folder: {host}";
        }
    }

    private static string Summarize(
        List<(int Round, JsonElement Root)> originals,
        List<(int Round, JsonElement Root)> forks,
        (int Round, JsonElement Root) reference,
        List<Difference> natural,
        Classification original,
        Classification fork,
        Answers answers,
        string verdict,
        List<string> runIssues,
        List<string> reasons)
    {
        var md = new StringBuilder();
        md.AppendLine("# BuildHost fork baseline (BuildHost/task-00)");
        md.AppendLine();
        md.AppendLine($"- original BuildHost: {originals[0].Root.GetProperty("BuildHost").GetString()}; fork: {forks[0].Root.GetProperty("BuildHost").GetString()}");
        md.AppendLine($"- runs: original {originals.Count}, fork {forks.Count}; round 0 is warmup: only its search answer and BuildHost processes are checked, equivalence and timings use rounds 1+");
        md.AppendLine($"- reference: original round {reference.Round}, {reference.Root.GetProperty("Projects").EnumerateObject().Count()} projects");
        md.AppendLine(natural.Count > 0 || originals.Count(run => run.Round > 0) > 1
            ? $"- natural variance (original round 1 against round 2): {natural.Count} difference(s)"
            : "- natural variance: one measured original round, so every difference is unexplained");
        md.AppendLine($"- original rounds 3+ against the reference: natural {original.Natural}, unexplained {original.Unexplained.Count}");
        md.AppendLine($"- fork rounds 1+ against the reference: **{verdict}**; natural {fork.Natural}, unexplained {fork.Unexplained.Count}");
        md.AppendLine($"- distinct search answers in all runs: original {answers.Original.Count}, fork {answers.Forks.Count}{(answers.ForkDiffers ? "; a fork answer is not among the original ones" : "")}");
        md.AppendLine($"- runs finished, BuildHost processes recorded, from own folder, none left 10 s after the first search: {(runIssues.Count == 0 ? "ok" : runIssues.Count + " issue(s)")}");
        md.AppendLine($"- result: **{(reasons.Count == 0 ? "equivalent" : "not shown equivalent: " + string.Join("; ", reasons))}**");
        md.AppendLine("- only the captured fields are compared; see the limits of SolutionObservation in the task-00 report");
        md.AppendLine(ReportOrderLine(originals, forks, reference));
        md.AppendLine();
        md.AppendLine("| metric (median of rounds 1+) | original | fork | fork / original |");
        md.AppendLine("|---|---|---|---|");
        AppendMetric(md, "load ms", originals, forks, run => run.GetProperty("WallMs").GetDouble());
        AppendMetric(md, "first search ms", originals, forks, run => run.GetProperty("FirstSearch").GetProperty("wallMs").GetDouble());
        AppendMetric(md, "client CPU ms", originals, forks, run => run.GetProperty("ClientCpuMs").GetDouble());
        AppendMetric(md, "client peak working set MB", originals, forks, run => run.GetProperty("ClientPeakWorkingSetMb").GetDouble());
        AppendMetric(md, "BuildHost processes", originals, forks, run => run.GetProperty("BuildHostProcesses").GetProperty("count").GetInt32());
        AppendMetric(md, "BuildHost CPU ms (lower bound)", originals, forks, run => run.GetProperty("BuildHostProcesses").GetProperty("cpuMs").GetDouble());
        AppendMetric(md, "largest BuildHost peak working set MB", originals, forks, run => run.GetProperty("BuildHostProcesses").GetProperty("peakWorkingSetMb").GetDouble());
        md.AppendLine();
        md.AppendLine("Per-round load ms: original " + Rounds(originals, run => run.GetProperty("WallMs").GetDouble()) + "; fork " + Rounds(forks, run => run.GetProperty("WallMs").GetDouble()));
        md.AppendLine("Per-round client CPU ms: original " + Rounds(originals, run => run.GetProperty("ClientCpuMs").GetDouble()) + "; fork " + Rounds(forks, run => run.GetProperty("ClientCpuMs").GetDouble()));
        AppendList(md, "Natural variance of the original (first 30)", natural.Select(item => $"{item.Key} {item.Category}: {Shorten(item.Before)} -> {Shorten(item.After)}"));
        AppendList(md, "Unexplained differences of the original (first 30)", original.Unexplained);
        AppendList(md, "Unexplained differences of the fork (first 30)", fork.Unexplained);
        AppendList(md, "Run and BuildHost process issues (first 30)", runIssues);
        return md.ToString();
    }

    // Informational: Diagnostics is a set for the result, but a fork that reorders builds must also keep the order in
    // which the loader reports them.
    private static string ReportOrderLine(
        List<(int Round, JsonElement Root)> originals,
        List<(int Round, JsonElement Root)> forks,
        (int Round, JsonElement Root) reference)
    {
        if (ReportOrder(reference.Root) is not { } expected)
        {
            return "- diagnostics in report order (not part of the result): not recorded";
        }

        var otherOriginals = originals.Where(run => run.Round > reference.Round).ToList();
        var measuredForks = forks.Where(run => run.Round > 0).ToList();
        var sameOriginals = otherOriginals.Count(run => ReportOrder(run.Root)?.SequenceEqual(expected, StringComparer.Ordinal) == true);
        var sameForks = measuredForks.Count(run => ReportOrder(run.Root)?.SequenceEqual(expected, StringComparer.Ordinal) == true);
        return $"- diagnostics in report order (not part of the result), same as the reference: original rounds 2+ {sameOriginals} of {otherOriginals.Count}, fork rounds 1+ {sameForks} of {measuredForks.Count}";
    }

    private static List<string>? ReportOrder(JsonElement run) =>
        run.TryGetProperty("DiagnosticsInReportOrder", out var order) ? order.EnumerateArray().Select(Value).ToList() : null;

    private static Dictionary<string, Dictionary<string, string>> Projects(JsonElement run) =>
        run.GetProperty("Projects").EnumerateObject().ToDictionary(
            project => project.Name,
            project => project.Value.EnumerateObject().ToDictionary(field => field.Name, field => Value(field.Value), StringComparer.Ordinal),
            StringComparer.Ordinal);

    private static HashSet<string> Diagnostics(JsonElement run) =>
        run.GetProperty("Diagnostics").EnumerateArray().Select(Value).ToHashSet(StringComparer.Ordinal);

    // SolutionObservation writes every value as a string. Anything else keeps its raw JSON with a prefix, so a JSON
    // null cannot equal an empty string or the text "null".
    private static string Value(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : "json:" + value.GetRawText();

    private static string Presence(bool present) => present ? "present" : "absent";

    private static void AppendMetric(StringBuilder md, string name, List<(int Round, JsonElement Root)> originals,
        List<(int Round, JsonElement Root)> forks, Func<JsonElement, double> read)
    {
        var original = Median(originals.Where(run => run.Round > 0).Select(run => read(run.Root)));
        var fork = Median(forks.Where(run => run.Round > 0).Select(run => read(run.Root)));
        var ratio = original is > 0 && fork is not null ? (fork / original)?.ToString("0.000", CultureInfo.InvariantCulture) : "n/a";
        md.AppendLine($"| {name} | {Format(original)} | {Format(fork)} | {ratio} |");
    }

    private static void AppendList(StringBuilder md, string title, IEnumerable<string> lines)
    {
        var items = lines.Take(30).ToList();
        if (items.Count == 0)
        {
            return;
        }

        md.AppendLine();
        md.AppendLine(title + ":");
        md.AppendLine();
        foreach (var line in items)
        {
            md.AppendLine("- " + line);
        }
    }

    private static string Rounds(List<(int Round, JsonElement Root)> runs, Func<JsonElement, double> read) =>
        string.Join(", ", runs.Where(run => run.Round > 0).Select(run => Format(read(run.Root))));

    private static string Shorten(string? value) => value is null ? "(missing)" : value.Length > 160 ? value[..160] + "…" : value;

    private sealed record Difference(string Key, string Category, string? Before, string? After);

    private sealed record Classification(int Natural, List<string> Unexplained);

    private sealed record Answers(HashSet<string> Original, HashSet<string> Forks)
    {
        public bool ForkDiffers => !Forks.IsSubsetOf(Original);
    }
}
