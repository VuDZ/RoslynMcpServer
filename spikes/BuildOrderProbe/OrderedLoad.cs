using System.Collections.Concurrent;
using System.Diagnostics;
using BuildOrderProbe.Models;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;

namespace BuildOrderProbe;

/// <summary>
/// Evaluates and builds every C# and VB project of a solution, and the projects they reference, in one
/// <see cref="LoadOrder"/>, the way the BuildHost would serve the 5.9.0 loader's requests in that order.
/// </summary>
/// <remarks>
/// The traversal follows the solution's entries in order and each project's ProjectReference items of its root
/// evaluation, depth first, like the loader's reference resolution; projects the loader cannot open (other
/// extensions) are skipped. The loader takes the references of each target framework from the build, so this order
/// is an approximation of the client's; it is the same in every run, which is what the comparison needs.
/// </remarks>
internal sealed class OrderedLoad
{
    private OrderedLoad(HostSession session, LoadOrder order, int workers, IReadOnlyList<string> traversalRoots)
    {
        _session = session;
        _order = order;
        _workerSlots = new SemaphoreSlim(workers, workers);
        if (order == LoadOrder.EvaluateClosure)
        {
            _closure = new ClosureEvaluator(Evaluate, ProjectReferences, workers);
        }
        else if (order is LoadOrder.EvaluateClosurePreorder or LoadOrder.EvaluateSolutionPreorder)
        {
            _closure = new ClosureEvaluator(Evaluate, ProjectReferences, workers, traversalRoots);
        }
    }

    public static ProbeRun Run(string solutionPath, LoadOrder order, int workers, int window)
    {
        using var session = new HostSession(solutionPath);
        var solutionProjects = SolutionProjects(solutionPath).ToArray();
        var load = new OrderedLoad(session, order, workers, solutionProjects);
        var gcPause = GC.GetTotalPauseDuration();
        var gcCollections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        load._wall.Start();
        var queued = 0;
        for (var i = 0; i < solutionProjects.Length; i++)
        {
            if (order == LoadOrder.EvaluateSolutionPreorder)
            {
                for (var end = window == 0 ? solutionProjects.Length : Math.Min(solutionProjects.Length, i + window + 1); queued < end; queued++)
                {
                    load._closure!.Schedule(solutionProjects[queued]);
                }
            }

            load.Visit(solutionProjects[i]);
        }

        load._wall.Stop();
        var gcPauseMs = (GC.GetTotalPauseDuration() - gcPause).TotalMilliseconds;
        var gcCollectionsDuring = Enumerable.Range(0, 3).Select(generation => GC.CollectionCount(generation) - gcCollections[generation]).ToArray();
        load._closure?.Dispose();
        load._evaluationsOpen.Dispose();
        return new ProbeRun(
            order.ToString(),
            order is LoadOrder.EvaluateAhead or LoadOrder.EvaluateAheadPaused or LoadOrder.EvaluateClosure or LoadOrder.EvaluateClosurePreorder or LoadOrder.EvaluateSolutionPreorder
                ? workers
                : 0,
            order == LoadOrder.EvaluateSolutionPreorder ? window : 0,
            System.Runtime.GCSettings.IsServerGC,
            Environment.ProcessorCount,
            load._wall.Elapsed.TotalMilliseconds,
            load._evaluationSteps.Count,
            load._evaluationSteps.Sum(step => step.DurationMs),
            load._fingerprints.Count,
            load._buildSteps.Sum(step => step.DurationMs),
            session.EvaluationsDuringBuilds,
            load._evaluationSteps.Count(step => step.Thread < 0),
            gcPauseMs,
            gcCollectionsDuring,
            Process.GetCurrentProcess().PeakWorkingSet64 / (1024.0 * 1024.0),
            new SortedDictionary<string, string>(load._fingerprints, StringComparer.Ordinal),
            load._evaluationSteps.OrderBy(step => step.StartMs).ToArray(),
            load._buildSteps);
    }

    /// <summary>A project with TargetFrameworks and no TargetFramework, which the host builds once per target framework.</summary>
    public static bool IsMultiTargeting(Project project)
        => project.GetPropertyValue("TargetFramework").Length == 0 && project.GetPropertyValue("TargetFrameworks").Length > 0;

    private void Visit(string path)
    {
        if (!_visited.Add(path))
        {
            return;
        }

        var project = Evaluation(path).Result;
        if (project is null)
        {
            return;
        }

        var references = ProjectReferences(project);
        if (_order is LoadOrder.EvaluateAhead or LoadOrder.EvaluateAheadPaused)
        {
            foreach (var reference in references)
            {
                Evaluation(reference);
            }
        }

        if (_order == LoadOrder.Client)
        {
            Build(project);
        }

        foreach (var reference in references)
        {
            Visit(reference);
        }

        if (_order != LoadOrder.Client)
        {
            Build(project);
        }
    }

    // One root evaluation per path. Ahead of time it runs on a worker slot; otherwise on the caller's thread, so the
    // sequential orders do not pay for thread switches. The closure order has its own queue.
    private Task<Project?> Evaluation(string path)
        => _closure is not null
            ? Task.FromResult(_closure.Require(path))
            : _evaluations.GetOrAdd(path, key => _order is LoadOrder.EvaluateAhead or LoadOrder.EvaluateAheadPaused
            ? Task.Run(async () =>
            {
                await _workerSlots.WaitAsync().ConfigureAwait(false);
                try
                {
                    // Closed only while a multi-targeting project builds in EvaluateAheadPaused. The build thread
                    // never waits for an evaluation during a build, so a worker held here cannot block it.
                    _evaluationsOpen.Wait();
                    return Evaluate(key);
                }
                finally
                {
                    _workerSlots.Release();
                }
            })
            : Task.FromResult(Evaluate(key)));

    // A project MSBuild cannot load is recorded with thread -1, so the run counts it without a step on the clock.
    private Project? Evaluate(string path)
    {
        var start = _wall.Elapsed.TotalMilliseconds;
        var project = _session.Evaluate(path);
        var end = _wall.Elapsed.TotalMilliseconds;
        _evaluationSteps.Add(new TimedStep(path, project is not null && IsMultiTargeting(project), start, end, project is null ? -1 : Environment.CurrentManagedThreadId));
        return project;
    }

    private void Build(Project project)
    {
        var multiTargeting = IsMultiTargeting(project);
        var pause = multiTargeting && _order == LoadOrder.EvaluateAheadPaused;
        if (pause)
        {
            _evaluationsOpen.Reset();
        }

        var start = _wall.Elapsed.TotalMilliseconds;
        try
        {
            foreach (var (targetFramework, fingerprint) in _session.Build(project))
            {
                _fingerprints[project.FullPath + "|" + targetFramework] = fingerprint;
            }
        }
        finally
        {
            if (pause)
            {
                _evaluationsOpen.Set();
            }
        }

        _buildSteps.Add(new TimedStep(project.FullPath, multiTargeting, start, _wall.Elapsed.TotalMilliseconds, Environment.CurrentManagedThreadId));
    }

    private static IReadOnlyList<string> ProjectReferences(Project project)
        => project.GetItems("ProjectReference")
            .Select(item => Path.GetFullPath(Path.Combine(project.DirectoryPath, item.EvaluatedInclude)))
            .Where(path => IsLoadable(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> SolutionProjects(string solutionPath)
        => SolutionFile.Parse(solutionPath).ProjectsInOrder
            .Where(project => project.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat && IsLoadable(project.AbsolutePath))
            .Select(project => Path.GetFullPath(project.AbsolutePath));

    private static bool IsLoadable(string path)
        => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);

    private readonly HostSession _session;
    private readonly LoadOrder _order;
    private readonly SemaphoreSlim _workerSlots;
    private readonly ManualResetEventSlim _evaluationsOpen = new(initialState: true);
    private readonly ClosureEvaluator? _closure;
    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<Project?>> _evaluations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<TimedStep> _evaluationSteps = [];
    private readonly List<TimedStep> _buildSteps = [];
    private readonly Dictionary<string, string> _fingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _wall = new();
}
