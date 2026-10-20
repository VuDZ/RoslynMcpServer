using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace ParallelLoad;

internal sealed class BinlogTimingReplay
{
    public static int Run(string[] args)
    {
        if (args.Length != 7 || args[1] != "--solution-root" || args[3] != "--input" || args[5] != "--output")
        {
            throw new ArgumentException("Usage: binlog-timings --solution-root PATH --input BINLOG --output JSON");
        }
        MsBuildRegistration.Register(Environment.CurrentDirectory);
        return ReplayRegistered(args[2], args[4], args[6]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReplayRegistered(string root, string input, string output)
    {
        var reader = new BinlogTimingReplay(Path.GetFullPath(root));
        var replay = new BinaryLogReplayEventSource { AllowForwardCompatibility = true };
        replay.AnyEventRaised += (_, value) => reader.Observe(value);
        replay.RecoverableReadError += error => reader._errors.Add($"{error.ErrorType}/{error.RecordKind}");
        replay.Replay(input);
        reader._orderedSpans = reader._spans.OrderBy(span => span.Start).ToArray();
        if (reader._pending.Count > 0) { reader._errors.Add($"Unmatched intervals: {reader._pending.Count}"); }
        var report = new
        {
            Status = reader._errors.Count == 0 ? "ok" : "incomplete",
            Input = Path.GetFileName(input), replay.FileFormatVersion, Errors = reader._errors,
            SdkProperties = reader._sdkProperties,
            SdkResolverTiming = (double?)null,
            SdkResolverLimitation = "Diagnostic resolver messages have no paired duration events. Resolver time is included in evaluation; message counts do not measure its duration.",
            EvaluationProfilerAvailable = reader._profileLocations > 0,
            Meaning = "Interval union per project; target exclusive excludes tasks/evaluations/nested project builds; task exclusive excludes evaluations/nested project builds. Evaluation, target and task totals are separate layers, never sum inclusive parent/child durations. Concurrent project service durations are not wall time.",
            Projects = reader.Capture(),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"binlog-timings status={report.Status} projects={reader._spans.Select(span => span.Path).Distinct().Count()} report={output}");
        return report.Status == "ok" ? 0 : 2;
    }

    private BinlogTimingReplay(string root) => _root = root;

    private void Observe(BuildEventArgs value)
    {
        var context = value.BuildEventContext;
        if (context is null) { return; }
        _eventOrder++;
        var projectKey = (context.NodeId, context.ProjectContextId);
        switch (value)
        {
            case ProjectStartedEventArgs project:
                _projectPaths[projectKey] = Normalize(project.ProjectFile!);
                if (project.ParentProjectBuildEventContext is { } parent && parent.ProjectContextId != BuildEventContext.InvalidProjectContextId)
                {
                    _parents[projectKey] = (parent.NodeId, parent.ProjectContextId);
                }
                if (context.EvaluationId != BuildEventContext.InvalidEvaluationId)
                {
                    _evaluationProjects[(context.NodeId, context.EvaluationId)] = projectKey;
                }
                Start("project", context.NodeId, context.ProjectContextId, 0, project.ProjectFile!, project.ProjectFile!, value.Timestamp);
                if (project.Properties is not null)
                {
                    foreach (DictionaryEntry property in project.Properties)
                    {
                        var name = property.Key.ToString()!;
                        if (name is "NETCoreSdkVersion" or "MSBuildSDKsPath" or "MSBuildToolsPath" or "MSBuildVersion")
                        {
                            _sdkProperties.Add(name + "=" + property.Value);
                        }
                    }
                }
                break;
            case ProjectFinishedEventArgs:
                Finish("project", context.NodeId, context.ProjectContextId, 0, value.Timestamp);
                break;
            case ProjectEvaluationStartedEventArgs evaluation:
                Start("evaluation", context.NodeId, context.EvaluationId, 0, evaluation.ProjectFile!, "evaluation", value.Timestamp);
                break;
            case ProjectEvaluationFinishedEventArgs evaluation:
                Finish("evaluation", context.NodeId, context.EvaluationId, 0, value.Timestamp);
                CaptureProperties(evaluation.Properties);
                if (evaluation.ProfilerResult is { } profile)
                {
                    _profileLocations += profile.ProfiledLocations.Count;
                }
                break;
            case TargetStartedEventArgs target:
                Start("target", context.NodeId, context.ProjectContextId, context.TargetId,
                    target.ProjectFile!, target.TargetName, value.Timestamp);
                break;
            case TargetFinishedEventArgs:
                Finish("target", context.NodeId, context.ProjectContextId, context.TargetId, value.Timestamp);
                break;
            case TaskStartedEventArgs task:
                Start("task", context.NodeId, context.ProjectContextId, context.TaskId,
                    task.ProjectFile!, task.TaskName, value.Timestamp);
                break;
            case TaskFinishedEventArgs:
                Finish("task", context.NodeId, context.ProjectContextId, context.TaskId, value.Timestamp);
                break;
            case BuildMessageEventArgs message when message.Message?.Contains("resolver", StringComparison.OrdinalIgnoreCase) == true:
                var path = _projectPaths.GetValueOrDefault(projectKey, "<evaluation-context>");
                _resolverMessages[path] = _resolverMessages.GetValueOrDefault(path) + 1;
                break;
        }
    }

    private void Start(string kind, int node, int project, int item, string path, string name, DateTime time)
    {
        var key = (kind, node, project, item);
        if (!_pending.TryAdd(key, new Span(kind, Normalize(path), name, time, time, node, project, _eventOrder, _eventOrder)))
        {
            _errors.Add("Duplicate start: " + key);
        }
    }

    private void CaptureProperties(IEnumerable? properties)
    {
        if (properties is null) { return; }
        foreach (var property in properties)
        {
            var pair = property switch
            {
                DictionaryEntry entry => (entry.Key.ToString()!, entry.Value?.ToString()),
                KeyValuePair<string, string> entry => (entry.Key, entry.Value),
                _ => ("", ""),
            };
            if (pair.Item1 is "NETCoreSdkVersion" or "MSBuildSDKsPath" or "MSBuildToolsPath" or "MSBuildVersion")
            {
                _sdkProperties.Add(pair.Item1 + "=" + pair.Item2);
            }
        }
    }

    internal static void CheckIntervals()
    {
        var replay = new BinlogTimingReplay(Environment.CurrentDirectory);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var target = new Span("target", "A", "Target", start, start.AddMilliseconds(100), 1, 1, 1, 8);
        var task = new Span("task", "A", "CallTarget", start.AddMilliseconds(10), start.AddMilliseconds(90), 1, 1, 2, 7);
        var nestedTarget = new Span("target", "A", "Nested", start.AddMilliseconds(20), start.AddMilliseconds(80), 1, 1, 3, 6);
        var nestedTask = new Span("task", "A", "NestedTask", start.AddMilliseconds(30), start.AddMilliseconds(70), 1, 1, 4, 5);
        var concurrent = new Span("task", "B", "Unrelated", start, start.AddMilliseconds(100), 2, 2, 2, 7);
        replay._orderedSpans = [target, concurrent, task, nestedTarget, nestedTask];
        if (replay.Exclusive(target) != 20 || replay.Exclusive(task) != 20 ||
            replay.Exclusive(nestedTarget) != 20 || replay.Exclusive(nestedTask) != 40)
        {
            throw new InvalidOperationException("Exclusive timing must partition nested targets/tasks without charging concurrent contexts.");
        }
    }

    private void Finish(string kind, int node, int project, int item, DateTime time)
    {
        if (_pending.Remove((kind, node, project, item), out var span))
        {
            if (time < span.Start) { _errors.Add("Negative interval: " + span.Name); }
            else { _spans.Add(span with { End = time, EndOrder = _eventOrder }); }
        }
        else { _errors.Add($"Unmatched finish: {kind}/{node}/{project}/{item}"); }
    }

    private object[] Capture() => _spans.GroupBy(span => span.Path, StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Min(span => span.Start)).Select((group, index) => (object)new
        {
            Path = group.Key, LoadOrder = index + 1,
            EvaluationMs = Union(group.Where(span => span.Kind == "evaluation")),
            ProjectInclusiveMs = Union(group.Where(span => span.Kind == "project")),
            ResolverMessageCount = _resolverMessages.GetValueOrDefault(group.Key),
            Targets = group.Where(span => span.Kind == "target").GroupBy(span => span.Name)
                .Select(target => new { Name = target.Key, ExclusiveMs = target.Sum(Exclusive), InclusiveMs = Union(target) })
                .OrderByDescending(target => target.ExclusiveMs).ToArray(),
            Tasks = group.Where(span => span.Kind == "task").GroupBy(span => span.Name)
                .Select(task => new { Name = task.Key, ExclusiveMs = task.Sum(Exclusive), InclusiveMs = Union(task) })
                .OrderByDescending(task => task.ExclusiveMs).ToArray(),
        }).ToArray();

    private double Exclusive(Span parent)
    {
        var left = 0;
        var right = _orderedSpans.Length;
        while (left < right)
        {
            var middle = (left + right) / 2;
            if (_orderedSpans[middle].Start < parent.Start) { left = middle + 1; }
            else { right = middle; }
        }
        var children = _orderedSpans.Skip(left).TakeWhile(span => span.Start <= parent.End)
            .Where(span => span.End <= parent.End && span.StartOrder > parent.StartOrder && span.EndOrder < parent.EndOrder &&
                BelongsToParent(span, parent));
        return Math.Max(0, (parent.End - parent.Start).TotalMilliseconds - Union(children));
    }

    private static double Union(IEnumerable<Span> spans)
    {
        DateTime? end = null;
        double duration = 0;
        foreach (var span in spans.OrderBy(span => span.Start))
        {
            var start = end is { } previous && previous > span.Start ? previous : span.Start;
            if (span.End > start) { duration += (span.End - start).TotalMilliseconds; }
            if (end is null || span.End > end) { end = span.End; }
        }
        return duration;
    }

    private string Normalize(string path) => Path.GetRelativePath(_root, path).Replace('\\', '/');

    private bool BelongsToParent(Span child, Span parent)
    {
        var owner = child.Kind == "evaluation"
            ? _evaluationProjects.GetValueOrDefault((child.Node, child.Project), (child.Node, int.MinValue))
            : (child.Node, child.Project);
        var parentOwner = (parent.Node, parent.Project);
        if (child.Kind == "evaluation" && owner.Item2 == int.MinValue)
        {
            return child.Node == parent.Node && string.Equals(child.Path, parent.Path, StringComparison.OrdinalIgnoreCase);
        }
        var visited = new HashSet<(int, int)>();
        while (visited.Add(owner))
        {
            if (owner == parentOwner) { return true; }
            if (!_parents.TryGetValue(owner, out owner)) { break; }
        }
        return false;
    }

    private sealed record Span(string Kind, string Path, string Name, DateTime Start, DateTime End,
        int Node, int Project, int StartOrder, int EndOrder);

    private readonly string _root;
    private readonly List<Span> _spans = [];
    private readonly Dictionary<(string Kind, int Node, int Project, int Item), Span> _pending = [];
    private readonly Dictionary<(int Node, int Project), string> _projectPaths = [];
    private readonly Dictionary<(int Node, int Project), (int Node, int Project)> _parents = [];
    private readonly Dictionary<(int Node, int Evaluation), (int Node, int Project)> _evaluationProjects = [];
    private readonly Dictionary<string, int> _resolverMessages = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _errors = [];
    private readonly HashSet<string> _sdkProperties = [];
    private Span[] _orderedSpans = [];
    private int _profileLocations;
    private int _eventOrder;
}
