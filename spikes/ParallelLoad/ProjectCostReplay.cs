using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using ParallelLoad.Models;

namespace ParallelLoad;

internal sealed class ProjectCostReplay
{
    public List<BinlogSession> Sessions { get; } = [];

    public ProjectCostReplay(string solutionDirectory)
    {
        _solutionDirectory = solutionDirectory;
    }

    public void Read(string path)
    {
        var replay = new BinaryLogReplayEventSource { AllowForwardCompatibility = true };
        ReadEvents(Path.GetFileName(path), handler =>
        {
            replay.AnyEventRaised += (_, args) => handler(args);
            replay.RecoverableReadError += error => _readErrors.Add($"ErrorType={error.ErrorType}; RecordKind={error.RecordKind}");
            replay.Replay(path);
        }, () => replay.FileFormatVersion);
    }

    public ProjectCost[] Capture(JsonElement projects)
    {
        return projects.EnumerateObject().Select(project =>
        {
            var separator = project.Name.LastIndexOf('|');
            var file = project.Name[..separator];
            var name = project.Name[(separator + 1)..];
            var relative = file["<solution>".Length..].TrimStart('/', '\\');
            var cost = _costs.GetValueOrDefault(relative.Replace('\\', '/')) ?? new AccumulatedCost();
            var references = JsonSerializer.Deserialize<string[]>(project.Value.GetProperty("ProjectReferences").GetString()!)!
                .Select(reference => JsonDocument.Parse(reference))
                .Select(document =>
                {
                    using (document)
                    {
                        return SanitizeKey(document.RootElement.GetProperty("Target").GetString()!);
                    }
                }).ToArray();
            return new ProjectCost(SanitizeKey(project.Name), relative.Replace('\\', '/'), name, references,
                cost.EvaluationMs, cost.OwnBuildMs, cost.Order, cost.Markup.Count > 0, cost.Markup.Order().ToArray())
            {
                WeightMs = cost.WeightMs, FirstMs = cost.FirstMs,
            };
        }).ToArray();
    }

    internal void ReadEvents(string file, Action<Action<BuildEventArgs>> dispatch, Func<int> formatVersion,
        params string[] recoverableErrors)
    {
        var evaluations = new Dictionary<(int Node, int Evaluation), DateTime>();
        var builds = new Dictionary<(int Node, int Project), BuildSpan>();
        var orders = new Dictionary<int, int>();
        var counts = new Dictionary<string, int>();
        DateTime? buildStart = null;
        _readErrors.Clear();
        _readErrors.AddRange(recoverableErrors);
        dispatch(args =>
        {
            var kind = args.GetType().Name;
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
            if (args is BuildStartedEventArgs)
            {
                buildStart ??= args.Timestamp;
            }

            var context = args.BuildEventContext;
            if (context is null)
            {
                return;
            }

            var evaluationKey = (context.NodeId, context.EvaluationId);
            var buildKey = (context.NodeId, context.ProjectContextId);
            switch (args)
            {
                case ProjectEvaluationStartedEventArgs started:
                    evaluations.Add(evaluationKey, started.Timestamp);
                    break;
                case ProjectEvaluationFinishedEventArgs finished:
                    if (evaluations.Remove(evaluationKey, out var start))
                    {
                        var cost = Cost(finished.ProjectFile ?? throw new InvalidDataException("Evaluation has no project path."));
                        cost.EvaluationMs = (cost.EvaluationMs ?? 0) + Duration(start, finished.Timestamp);
                    }
                    break;
                case ProjectStartedEventArgs started:
                    var projectPath = started.ProjectFile ?? throw new InvalidDataException("Build has no project path.");
                    var parent = started.ParentProjectBuildEventContext;
                    var nested = parent is not null && parent.ProjectContextId != BuildEventContext.InvalidProjectContextId;
                    var order = orders.GetValueOrDefault(context.NodeId);
                    if (!nested)
                    {
                        orders[context.NodeId] = ++order;
                        Cost(projectPath).Order ??= order;
                    }

                    builds.Add(buildKey, new BuildSpan(projectPath, started.Timestamp, nested));
                    break;
                case ProjectFinishedEventArgs finished:
                    if (builds.TryGetValue(buildKey, out var span))
                    {
                        span.End = finished.Timestamp;
                    }
                    break;
                case TargetStartedEventArgs target when IsMarkupTarget(target.TargetName):
                    Mark(builds, buildKey, "target:" + target.TargetName);
                    break;
                case TaskStartedEventArgs task when task.TaskName == "CompileXaml":
                    Mark(builds, buildKey, "task:" + task.TaskName);
                    break;
                case TaskCommandLineEventArgs command when command.CommandLine.Contains("XamlCompiler.exe", StringComparison.OrdinalIgnoreCase):
                    Mark(builds, buildKey, "command:XamlCompiler.exe");
                    break;
                case BuildMessageEventArgs message when message.Message?.Contains("XamlCompiler.exe", StringComparison.OrdinalIgnoreCase) == true:
                    Mark(builds, buildKey, "message:XamlCompiler.exe");
                    break;
            }
        });

        var topLevel = builds.Values.Where(span => !span.Nested).OrderBy(span => span.Start).ToArray();
        if (counts.GetValueOrDefault(nameof(ProjectStartedEventArgs)) == 0)
        {
            Sessions.Add(new BinlogSession(file, formatVersion(), counts, _readErrors.ToArray(), 0, 0, 0, 0));
            return;
        }
        if (counts.GetValueOrDefault(nameof(ProjectStartedEventArgs)) > 0 && _readErrors.Count > 0)
        {
            throw new InvalidDataException("Recoverable read error in a project-bearing binlog: " + string.Join("; ", _readErrors));
        }

        var previousEnd = buildStart;
        var intervals = new List<ProjectBuildInterval>();
        double firstMs = 0, weightMs = 0, ownBuildMs = 0;
        foreach (var span in topLevel)
        {
            if (span.End is null || previousEnd is null)
            {
                throw new InvalidDataException("A top-level build has no finish event or the file has no BuildStarted.");
            }

            if (span.Start < previousEnd.Value)
            {
                throw new InvalidDataException("Top-level builds overlap; sequential loading cannot be assumed.");
            }

            // Nested reference queries are caller overhead. Do not also charge their target projects.
            var cost = Cost(span.Path);
            var own = Duration(span.Start, span.End.Value);
            var gap = Duration(previousEnd.Value, span.End.Value);
            cost.OwnBuildMs = (cost.OwnBuildMs ?? 0) + own;
            ownBuildMs += own;
            cost.WeightMs ??= 0;
            intervals.Add(new ProjectBuildInterval(Path.GetRelativePath(_solutionDirectory, span.Path).Replace('\\', '/'),
                previousEnd.Value, span.Start, span.End.Value));
            if (intervals.Count == 1)
            {
                firstMs += gap;
                cost.FirstMs += gap;
            }
            else
            {
                cost.WeightMs += gap;
                weightMs += gap;
            }
            previousEnd = span.End;
        }

        if (evaluations.Count != 0)
        {
            throw new InvalidDataException("Project evaluations have unmatched start events.");
        }
        Sessions.Add(new BinlogSession(file, formatVersion(), counts, _readErrors.ToArray(),
            topLevel.Length, firstMs, weightMs, ownBuildMs) { Intervals = intervals.ToArray() });
    }

    private string SanitizeKey(string key)
    {
        var separator = key.LastIndexOf('|');
        var path = key[..separator].Replace('\\', '/');
        var root = _solutionDirectory.Replace('\\', '/');
        path = path.Replace(root, "<solution>", StringComparison.OrdinalIgnoreCase);
        if (!path.StartsWith("<solution>/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A loaded project reference lies outside the sanitized solution root.");
        }

        return ProjectMerger.Key(path, key[(separator + 1)..]);
    }

    private AccumulatedCost Cost(string path)
    {
        var relative = Path.GetRelativePath(_solutionDirectory, path).Replace('\\', '/');
        if (!_costs.TryGetValue(relative, out var cost))
        {
            _costs.Add(relative, cost = new AccumulatedCost());
        }

        return cost;
    }

    private void Mark(Dictionary<(int Node, int Project), BuildSpan> builds, (int Node, int Project) key, string evidence)
    {
        if (builds.TryGetValue(key, out var span))
        {
            Cost(span.Path).Markup.Add(evidence);
        }
    }

    private static bool IsMarkupTarget(string name) => name is
        "MarkupCompilePass1" or "MarkupCompilePass2" or "GenerateTemporaryTargetAssembly" or "XamlPreCompile";

    private static double Duration(DateTime start, DateTime end)
    {
        var duration = (end - start).TotalMilliseconds;
        return duration >= 0 ? duration : throw new InvalidDataException("An event interval has negative duration.");
    }

    private sealed class AccumulatedCost
    {
        public double? EvaluationMs { get; set; }
        public double? OwnBuildMs { get; set; }
        public double? WeightMs { get; set; }
        public double FirstMs { get; set; }
        public int? Order { get; set; }
        public HashSet<string> Markup { get; } = [];
    }

    private sealed class BuildSpan(string path, DateTime start, bool nested)
    {
        public string Path { get; } = path;
        public DateTime Start { get; } = start;
        public DateTime? End { get; set; }
        public bool Nested { get; } = nested;
    }

    private readonly string _solutionDirectory;
    private readonly List<string> _readErrors = [];
    private readonly Dictionary<string, AccumulatedCost> _costs = new(StringComparer.OrdinalIgnoreCase);
}
