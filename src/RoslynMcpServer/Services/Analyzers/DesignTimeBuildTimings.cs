using System.Text.Json;
using Microsoft.Build.Framework;

namespace RoslynMcpServer.Services.Analyzers;

/// <summary>Observes the existing replay; inclusive durations overlap and are not additive wall time.</summary>
internal sealed class DesignTimeBuildTimings
{
    public int? MaxFormatVersion => _binlogs.Select(file => file.FormatVersion).Max();

    public void RecordBinlog(int ordinal, int? formatVersion, IReadOnlyCollection<string> failures)
    {
        _binlogs.Add(new Binlog(ordinal, formatVersion, _projectStarts.GetValueOrDefault(ordinal), failures.ToArray()));
    }

    public void Observe(int binlog, BuildEventArgs args)
    {
        if (args.Timestamp != DateTime.MinValue)
        {
            if (_first is null || args.Timestamp < _first)
            {
                _first = args.Timestamp;
            }
            if (_last is null || args.Timestamp > _last)
            {
                _last = args.Timestamp;
            }
        }

        var context = args.BuildEventContext;
        if (context is null)
        {
            return;
        }

        switch (args)
        {
            case ProjectStartedEventArgs project:
                _projectStarts[binlog] = _projectStarts.GetValueOrDefault(binlog) + 1;
                Start("project", 0, project.ProjectFile, project.ProjectFile);
                break;
            case ProjectFinishedEventArgs project:
                Finish("project", 0, project.Succeeded);
                break;
            case ProjectEvaluationStartedEventArgs evaluation:
                Start("evaluation", context.EvaluationId, evaluation.ProjectFile, "evaluation");
                break;
            case ProjectEvaluationFinishedEventArgs:
                Finish("evaluation", context.EvaluationId, true);
                break;
            case TargetStartedEventArgs target:
                Start("target", context.TargetId, target.ProjectFile, target.TargetName);
                break;
            case TargetFinishedEventArgs target:
                Finish("target", context.TargetId, target.Succeeded);
                break;
            case TaskStartedEventArgs task:
                Start("task", context.TaskId, task.ProjectFile, task.TaskName);
                break;
            case TaskFinishedEventArgs task:
                Finish("task", context.TaskId, task.Succeeded);
                break;
        }

        void Start(string kind, int item, string? project, string? name)
        {
            var key = (binlog, kind, AnalyzerProvenanceProjectContextKey.From(context), item);
            if (!_pending.TryAdd(key, new Pending(project ?? "", name ?? "", args.Timestamp)))
            {
                _invalidIntervals++;
            }
        }

        void Finish(string kind, int item, bool succeeded)
        {
            var key = (binlog, kind, AnalyzerProvenanceProjectContextKey.From(context), item);
            if (!_pending.Remove(key, out var started))
            {
                _unmatchedFinishes++;
                return;
            }

            var elapsedMs = (args.Timestamp - started.Time).TotalMilliseconds;
            if (elapsedMs < 0 || started.Time == DateTime.MinValue)
            {
                _invalidIntervals++;
                return;
            }

            var aggregateKey = (kind, started.Project, started.Name);
            if (!_totals.TryGetValue(aggregateKey, out var total))
            {
                total = new Total();
                _totals.Add(aggregateKey, total);
            }

            total.Count++;
            total.TotalMs += elapsedMs;
            total.MaxMs = Math.Max(total.MaxMs, elapsedMs);
            if (!succeeded)
            {
                total.Failed++;
            }
        }
    }

    public string ToJson(double openMs, IReadOnlyCollection<string> replayFailures)
    {
        double? spanMs = _first is { } first && _last is { } last ? (last - first).TotalMilliseconds : null;
        var remainder = spanMs is { } span && span <= openMs ? openMs - span : (double?)null;
        var evaluations = _totals.Where(pair => pair.Key.Kind == "evaluation").Select(pair => pair.Value).ToArray();
        return JsonSerializer.Serialize(new
        {
            status = replayFailures.Count > 0 || _pending.Count > 0 || _unmatchedFinishes > 0 || _invalidIntervals > 0
                ? "incomplete" : spanMs is null ? "unavailable" : "complete",
            openMs,
            binlogSpanMs = spanMs,
            unattributedOpenMs = remainder,
            remainderReason = remainder is not null ? null : spanMs is null ? "no_events" : "binlog_span_exceeds_open",
            replayFailures,
            binlogs = _binlogs.Select(file => new
            {
                ordinal = file.Ordinal,
                formatVersion = file.FormatVersion,
                projectStarts = file.ProjectStarts,
                failures = file.Failures,
            }),
            unmatchedStarts = _pending.Count,
            unmatchedFinishes = _unmatchedFinishes,
            invalidIntervals = _invalidIntervals,
            meaning = "Inclusive durations overlap across nested/concurrent contexts; sums are not wall time. Root evaluations may be absent from binlog. Project rows combine all configurations and target frameworks of the same path.",
            loggedEvaluations = evaluations.Sum(value => value.Count),
            loggedEvaluationMs = evaluations.Sum(value => value.TotalMs),
            maxLoggedEvaluationMs = evaluations.Length == 0 ? (double?)null : evaluations.Max(value => value.MaxMs),
            projects = _totals.Where(pair => pair.Key.Kind == "project")
                .OrderByDescending(pair => pair.Value.TotalMs).ThenBy(pair => pair.Key.Project, StringComparer.Ordinal)
                .Select(pair => new
                {
                    project = pair.Key.Project,
                    builds = pair.Value.Count,
                    failedBuilds = pair.Value.Failed,
                    totalMs = pair.Value.TotalMs,
                    maxMs = pair.Value.MaxMs,
                }),
            targets = Top("target"),
            tasks = Top("task"),
        });
    }

    private object[] Top(string kind) => _totals.Where(pair => pair.Key.Kind == kind)
        .GroupBy(pair => pair.Key.Name, StringComparer.Ordinal)
        .Select(group => new
        {
            name = group.Key,
            calls = group.Sum(pair => pair.Value.Count),
            totalMs = group.Sum(pair => pair.Value.TotalMs),
            maxMs = group.Max(pair => pair.Value.MaxMs),
        })
        .OrderByDescending(value => value.totalMs).ThenBy(value => value.name, StringComparer.Ordinal)
        .Take(10).Cast<object>().ToArray();

    private sealed record Pending(string Project, string Name, DateTime Time);

    private sealed record Binlog(int Ordinal, int? FormatVersion, int ProjectStarts, string[] Failures);

    private sealed class Total
    {
        public int Count { get; set; }
        public int Failed { get; set; }
        public double TotalMs { get; set; }
        public double MaxMs { get; set; }
    }

    private readonly Dictionary<(int Binlog, string Kind, AnalyzerProvenanceProjectContextKey Context, int Item), Pending> _pending = [];
    private readonly Dictionary<(string Kind, string Project, string Name), Total> _totals = [];
    private DateTime? _first;
    private DateTime? _last;
    private int _unmatchedFinishes;
    private int _invalidIntervals;
    private readonly List<Binlog> _binlogs = [];
    private readonly Dictionary<int, int> _projectStarts = [];
}
