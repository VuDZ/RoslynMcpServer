using LoadTimeline.Models;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace LoadTimeline;

/// <summary>
/// Replays the binlogs of one load into evaluation and project-build intervals. Timestamps come from the
/// process that wrote each binlog, so intervals of different binlogs are not compared with each other.
/// </summary>
internal sealed class TimelineReader
{
    public List<TimelineInterval> Intervals { get; } = [];

    /// <summary>First and last event time of each binlog, by binlog ordinal.</summary>
    public Dictionary<int, (DateTime First, DateTime Last)> Spans { get; } = [];

    /// <summary>Target name to (calls, inclusive milliseconds); inclusive times overlap.</summary>
    public Dictionary<string, (int Calls, double Ms)> Targets { get; } = new(StringComparer.Ordinal);

    /// <summary>Records the reader skipped; a non-zero count makes the timeline incomplete.</summary>
    public int RecoverableReadErrors { get; private set; }

    public void Read(string binlogPath, int ordinal, string root)
    {
        var evaluations = new Dictionary<int, (string Project, DateTime Start)>();
        var projects = new Dictionary<(int Node, int Context), (string Project, string Context, bool TopLevel, DateTime Start)>();
        var targets = new Dictionary<(int Node, int Context, int Target), (string Name, DateTime Start)>();
        // BuildHost-net472 can write a newer format than this reader; skipped records are counted, not hidden.
        var replay = new BinaryLogReplayEventSource { AllowForwardCompatibility = true };
        replay.RecoverableReadError += _ => RecoverableReadErrors++;
        replay.AnyEventRaised += (_, args) =>
        {
            NoteTime(ordinal, args.Timestamp);
            var context = args.BuildEventContext;
            if (context is null)
            {
                return;
            }

            switch (args)
            {
                case ProjectEvaluationStartedEventArgs started:
                    evaluations[context.EvaluationId] = (Relative(started.ProjectFile, root), started.Timestamp);
                    break;
                case ProjectEvaluationFinishedEventArgs finished when evaluations.Remove(context.EvaluationId, out var evaluation):
                    Intervals.Add(new TimelineInterval("evaluation", ordinal, evaluation.Project, "", false, evaluation.Start, finished.Timestamp));
                    break;
                case ProjectStartedEventArgs started:
                    projects[(context.NodeId, context.ProjectContextId)] = (
                        Relative(started.ProjectFile, root),
                        DescribeContext(started, root),
                        IsTopLevel(started),
                        started.Timestamp);
                    break;
                case ProjectFinishedEventArgs finished when projects.Remove((context.NodeId, context.ProjectContextId), out var project):
                    Intervals.Add(new TimelineInterval("build", ordinal, project.Project, project.Context, project.TopLevel, project.Start, finished.Timestamp));
                    break;
                case TargetStartedEventArgs started:
                    targets[(context.NodeId, context.ProjectContextId, context.TargetId)] = (started.TargetName, started.Timestamp);
                    break;
                case TargetFinishedEventArgs finished when targets.Remove((context.NodeId, context.ProjectContextId, context.TargetId), out var target):
                    var (calls, ms) = Targets.GetValueOrDefault(target.Name);
                    Targets[target.Name] = (calls + 1, ms + (finished.Timestamp - target.Start).TotalMilliseconds);
                    break;
            }
        };
        replay.Replay(binlogPath);
    }

    private void NoteTime(int ordinal, DateTime time)
    {
        if (time == DateTime.MinValue)
        {
            return;
        }

        Spans[ordinal] = Spans.TryGetValue(ordinal, out var span)
            ? (time < span.First ? time : span.First, time > span.Last ? time : span.Last)
            : (time, time);
    }

    // A request from the BuildHost itself has no parent project; MSBuild task calls into other projects do.
    private static bool IsTopLevel(ProjectStartedEventArgs started) =>
        started.ParentProjectBuildEventContext is null
        || started.ParentProjectBuildEventContext.ProjectContextId == BuildEventContext.InvalidProjectContextId;

    // Global properties can hold absolute paths (SolutionDir and the like); the report travels, so the root goes.
    private static string DescribeContext(ProjectStartedEventArgs started, string root)
    {
        var properties = started.GlobalProperties is null
            ? ""
            : string.Join(";", started.GlobalProperties.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key + "=" + pair.Value));
        return (properties + " | " + started.TargetNames).Replace(root, "<root>", StringComparison.OrdinalIgnoreCase);
    }

    private static string Relative(string? path, string root) =>
        string.IsNullOrEmpty(path) ? "(none)" : Path.GetRelativePath(root, path);
}
