using System.Globalization;
using System.Text;
using LoadTimeline.Models;

namespace LoadTimeline;

/// <summary>
/// Where the time of one binlog goes: top-level design-time builds and evaluations, their union, and the
/// gaps that neither covers. A gap is time without any logged build or evaluation in that process; it is
/// not called IPC here, because nothing in a binlog measures the client side or the transport.
/// </summary>
internal static class TimelineReport
{
    public static string Write(TimelineReader reader, double? clientLoadMs)
    {
        var md = new StringBuilder();
        md.AppendLine("# Load timeline (BuildHost/task-00a)");
        md.AppendLine();
        if (clientLoadMs is not null)
        {
            md.AppendLine($"- client OpenSolutionAsync: {Ms(clientLoadMs.Value)} ms");
        }

        md.AppendLine($"- binlogs: {reader.Spans.Count}; recoverable read errors (skipped records): {reader.RecoverableReadErrors}");
        md.AppendLine();

        foreach (var (binlog, span) in reader.Spans.OrderBy(pair => pair.Key))
        {
            var intervals = reader.Intervals.Where(interval => interval.Binlog == binlog).ToList();
            AppendBinlog(md, binlog, span, intervals);
        }

        md.AppendLine("## Targets (all binlogs, inclusive, overlapping)");
        md.AppendLine();
        md.AppendLine("| target | calls | inclusive ms |");
        md.AppendLine("|---|---|---|");
        foreach (var (name, (calls, ms)) in reader.Targets.OrderByDescending(pair => pair.Value.Ms).Take(15))
        {
            md.AppendLine($"| `{name}` | {calls} | {Ms(ms)} |");
        }

        return md.ToString();
    }

    /// <summary>
    /// Per top-level build in time order: binlog, project file name, the time since the previous top-level
    /// build of that binlog ended (or since its first event), and the build itself. A join key for the probe.
    /// </summary>
    public static string GapsBeforeBuilds(TimelineReader reader)
    {
        var lines = new List<string> { "binlog,project,gapBeforeMs,buildMs" };
        foreach (var (binlog, span) in reader.Spans.OrderBy(pair => pair.Key))
        {
            var previousEnd = span.First;
            foreach (var build in reader.Intervals.Where(item => item.Binlog == binlog && item.Kind == "build" && item.TopLevel).OrderBy(item => item.Start))
            {
                lines.Add($"{binlog},{FileName(build.Project)},{Ms((build.Start - previousEnd).TotalMilliseconds)},{Ms(build.Ms)}");
                previousEnd = build.End > previousEnd ? build.End : previousEnd;
            }
        }

        return string.Join('\n', lines);
    }

    private static void AppendBinlog(StringBuilder md, int binlog, (DateTime First, DateTime Last) span, List<TimelineInterval> intervals)
    {
        var spanMs = (span.Last - span.First).TotalMilliseconds;
        var topLevel = intervals.Where(interval => interval.Kind == "build" && interval.TopLevel).ToList();
        var nested = intervals.Where(interval => interval.Kind == "build" && !interval.TopLevel).ToList();
        var evaluations = intervals.Where(interval => interval.Kind == "evaluation").ToList();
        var busy = Union(topLevel.Concat(evaluations));
        var gaps = Gaps(span, busy);

        md.AppendLine($"## Binlog {binlog}");
        md.AppendLine();
        md.AppendLine($"- span: {Ms(spanMs)} ms");
        md.AppendLine($"- top-level builds: {topLevel.Count}, inclusive {Ms(topLevel.Sum(item => item.Ms))} ms; nested (MSBuild task) builds: {nested.Count}, inclusive {Ms(nested.Sum(item => item.Ms))} ms");
        md.AppendLine($"- evaluations: {evaluations.Count}, inclusive {Ms(evaluations.Sum(item => item.Ms))} ms; distinct projects evaluated: {evaluations.Select(item => item.Project).Distinct().Count()}");
        md.AppendLine($"- union of top-level builds and evaluations: {Ms(busy.Sum(item => (item.End - item.Start).TotalMilliseconds))} ms");
        md.AppendLine($"- gaps: {Ms(gaps.Sum(gap => gap.Ms))} ms in {gaps.Count} gaps; >= 50 ms: {gaps.Count(gap => gap.Ms >= 50)} gaps, {Ms(gaps.Where(gap => gap.Ms >= 50).Sum(gap => gap.Ms))} ms");
        if (gaps.Count > 0)
        {
            md.AppendLine($"- before the first build or evaluation: {Ms(gaps[0].Start == span.First ? gaps[0].Ms : 0)} ms; after the last: {Ms(gaps[^1].End == span.Last ? gaps[^1].Ms : 0)} ms");
        }

        md.AppendLine();
        md.AppendLine("| largest gaps | ms | after | before |");
        md.AppendLine("|---|---|---|---|");
        foreach (var gap in gaps.OrderByDescending(gap => gap.Ms).Take(15))
        {
            var before = intervals.Where(item => item.End <= gap.Start).MaxBy(item => item.End);
            var after = intervals.Where(item => item.Start >= gap.End).MinBy(item => item.Start);
            md.AppendLine($"| +{Ms((gap.Start - span.First).TotalMilliseconds)} ms | {Ms(gap.Ms)} | {Label(before)} | {Label(after)} |");
        }

        md.AppendLine();
        md.AppendLine("| repeated top-level builds (same project, properties and targets) | count | inclusive ms |");
        md.AppendLine("|---|---|---|");
        foreach (var group in topLevel.GroupBy(item => (item.Project, item.Context)).Where(group => group.Count() > 1)
            .OrderByDescending(group => group.Sum(item => item.Ms)).Take(10))
        {
            md.AppendLine($"| `{group.Key.Project}` | {group.Count()} | {Ms(group.Sum(item => item.Ms))} |");
        }

        AppendNestedByTargets(md, nested);

        md.AppendLine();
        md.AppendLine("| most evaluated projects | evaluations | inclusive ms |");
        md.AppendLine("|---|---|---|");
        foreach (var group in evaluations.GroupBy(item => item.Project).OrderByDescending(group => group.Count()).Take(10))
        {
            md.AppendLine($"| `{group.Key}` | {group.Count()} | {Ms(group.Sum(item => item.Ms))} |");
        }

        md.AppendLine();
    }

    // A nested build is an MSBuild task call into another project; the ProjectReference protocol asks for a few
    // targets (GetTargetFrameworks, GetTargetPath and the like), so a project start is not a full build. An empty
    // target list means the default targets, which would be a full build.
    private static void AppendNestedByTargets(StringBuilder md, List<TimelineInterval> nested)
    {
        md.AppendLine();
        md.AppendLine("| nested builds by requested targets (BuildHost/task-01) | count | distinct projects | inclusive ms |");
        md.AppendLine("|---|---|---|---|");
        foreach (var group in nested.GroupBy(item => RequestedTargets(item.Context)).OrderByDescending(group => group.Count()).Take(15))
        {
            md.AppendLine($"| `{group.Key}` | {group.Count()} | {group.Select(item => item.Project).Distinct().Count()} | {Ms(group.Sum(item => item.Ms))} |");
        }
    }

    private static string RequestedTargets(string context)
    {
        var separator = context.LastIndexOf(" | ", StringComparison.Ordinal);
        var targets = separator < 0 ? "" : context[(separator + 3)..].Trim();
        return targets.Length == 0 ? "(default targets)" : targets;
    }

    private static List<(DateTime Start, DateTime End)> Union(IEnumerable<TimelineInterval> intervals)
    {
        var merged = new List<(DateTime Start, DateTime End)>();
        foreach (var interval in intervals.OrderBy(item => item.Start))
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, interval.End > merged[^1].End ? interval.End : merged[^1].End);
            }
            else
            {
                merged.Add((interval.Start, interval.End));
            }
        }

        return merged;
    }

    private static List<(DateTime Start, DateTime End, double Ms)> Gaps((DateTime First, DateTime Last) span, List<(DateTime Start, DateTime End)> busy)
    {
        var gaps = new List<(DateTime Start, DateTime End, double Ms)>();
        var cursor = span.First;
        foreach (var (start, end) in busy)
        {
            if (start > cursor)
            {
                gaps.Add((cursor, start, (start - cursor).TotalMilliseconds));
            }

            if (end > cursor)
            {
                cursor = end;
            }
        }

        if (span.Last > cursor)
        {
            gaps.Add((cursor, span.Last, (span.Last - cursor).TotalMilliseconds));
        }

        return gaps;
    }

    private static string Label(TimelineInterval? interval) =>
        interval is null ? "-" : $"{interval.Kind} `{FileName(interval.Project)}`";

    // A Windows binlog is also read on Linux, where Path.GetFileName does not split at '\'.
    private static string FileName(string path) => path.Split('\\', '/')[^1];

    private static string Ms(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);
}
