using System.Diagnostics;
using BuildOrderProbe.Models;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Evaluation.Context;

namespace BuildOrderProbe;

/// <summary>
/// sdk-cache (BuildHost/task-04): loads a solution with references first, as the BuildHost serves it with A, and
/// times each kind of evaluation the host makes: the root evaluation; for a multi-targeting project the evaluation
/// after each TargetFramework switch and after the restore at the end; and the late evaluation that the first
/// GetAllGlobs of a project makes after its builds. Design-time builds are timed too. The
/// <see cref="SdkCacheMode"/> decides which evaluation context these evaluations get.
/// </summary>
/// <remarks>
/// SharedSDKCache shares one SDK resolver service between the evaluations of a context: its results, cached by SDK
/// name only, and the resolvers' state, such as the workload manifests the workload resolver loaded. Every
/// evaluation still gets its own file system cache (EvaluationContext.ContextForNewProject), so a file a build
/// creates is seen by a later evaluation, unlike with SharingPolicy.Shared. Because results are cached by name, a
/// shared context is only safe while every evaluation in it would resolve each SDK the same; in one project that
/// holds unless SDK references differ between target frameworks, across a solution it needs one global.json and
/// the same resolver inputs. The fingerprints show whether a mode changed what the host reads.
/// </remarks>
internal sealed class SdkCacheProbe
{
    public const string RootEvaluation = "root-evaluation";
    public const string TargetFrameworkEvaluation = "tfm-evaluation";
    public const string Globs = "globs";
    public const string DesignTimeBuild = "design-time-build";

    private SdkCacheProbe(HostSession session, SdkCacheMode mode, SdkResolutionListener? listener)
    {
        _session = session;
        _mode = mode;
        _listener = listener;
        _solutionContext = mode == SdkCacheMode.Solution ? EvaluationContext.Create(EvaluationContext.SharingPolicy.SharedSDKCache) : null;
    }

    public static SdkCacheRun Run(string solutionPath, SdkCacheMode mode, bool events)
    {
        // Created before the first MSBuild call, so the listener sees the event source from the start.
        using var listener = events ? new SdkResolutionListener() : null;
        using var session = new HostSession(solutionPath);
        var probe = new SdkCacheProbe(session, mode, listener);
        var wall = Stopwatch.StartNew();
        foreach (var path in OrderedLoad.SolutionProjects(solutionPath))
        {
            probe.Visit(path);
        }

        wall.Stop();
        return new SdkCacheRun(
            mode.ToString(),
            events,
            FileVersionInfo.GetVersionInfo(typeof(Project).Assembly.Location).FileVersion,
            Environment.ProcessorCount,
            wall.Elapsed.TotalMilliseconds,
            probe._visited.Count,
            probe._multiTargeting,
            new SortedDictionary<string, PhaseTotal>(probe._phases, StringComparer.Ordinal),
            listener?.ByPhase ?? new Dictionary<string, double>(),
            listener?.ByResolver ?? new Dictionary<string, double>(),
            listener?.Resolutions ?? 0,
            listener?.CachedResolutions ?? 0,
            Process.GetCurrentProcess().PeakWorkingSet64 / (1024.0 * 1024.0),
            new SortedDictionary<string, string>(probe._fingerprints, StringComparer.OrdinalIgnoreCase));
    }

    // The solution's entries in order, each project after the projects it references, depth first; then its builds,
    // then GetAllGlobs, which the host's extraction calls after the builds of a project.
    private void Visit(string path)
    {
        if (!_visited.Add(path))
        {
            return;
        }

        var context = _mode switch
        {
            SdkCacheMode.Project => EvaluationContext.Create(EvaluationContext.SharingPolicy.SharedSDKCache),
            SdkCacheMode.Solution => _solutionContext,
            _ => null,
        };
        var project = Timed(RootEvaluation, () => _session.Evaluate(path, context));
        if (project is null)
        {
            return;
        }

        _fingerprints["root|" + path] = EvaluationFingerprint.Of(project);
        foreach (var reference in OrderedLoad.ProjectReferences(project))
        {
            Visit(reference);
        }

        Build(project, context);
        var globs = Timed(Globs, () => context is null ? project.GetAllGlobs() : project.GetAllGlobs(context));
        _fingerprints["globs|" + path] = EvaluationFingerprint.Of(globs);
    }

    // HostSession.Build with each evaluation and build timed apart: one build of a project with one target
    // framework; for a multi-targeting one a TargetFramework switch and a build per framework, then the restore.
    private void Build(Project project, EvaluationContext? context)
    {
        var path = project.FullPath;
        var targetFramework = project.GetPropertyValue("TargetFramework");
        var targetFrameworks = project.GetPropertyValue("TargetFrameworks");
        if (!string.IsNullOrEmpty(targetFramework) || string.IsNullOrEmpty(targetFrameworks))
        {
            _fingerprints["build|" + path + "|" + targetFramework] = Timed(DesignTimeBuild, () => HostSession.BuildInstance(project));
            return;
        }

        _multiTargeting++;
        project.GlobalProperties.TryGetValue("TargetFramework", out var initial);
        foreach (var framework in targetFrameworks.Split(';'))
        {
            project.SetGlobalProperty("TargetFramework", framework);
            Timed(TargetFrameworkEvaluation, () => Reevaluate(project, context));
            _fingerprints["build|" + path + "|" + framework] = Timed(DesignTimeBuild, () => HostSession.BuildInstance(project));
        }

        if (initial is null)
        {
            project.RemoveGlobalProperty("TargetFramework");
        }
        else
        {
            project.SetGlobalProperty("TargetFramework", initial);
        }

        Timed(TargetFrameworkEvaluation, () => Reevaluate(project, context));
        _fingerprints["restored|" + path] = EvaluationFingerprint.Of(project);
    }

    private static bool Reevaluate(Project project, EvaluationContext? context)
    {
        if (context is null)
        {
            project.ReevaluateIfNecessary();
        }
        else
        {
            project.ReevaluateIfNecessary(context);
        }

        return true;
    }

    private T Timed<T>(string phase, Func<T> step)
    {
        _listener?.Phase = phase;
        var start = Stopwatch.GetTimestamp();
        try
        {
            return step();
        }
        finally
        {
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var total = _phases.GetValueOrDefault(phase) ?? new PhaseTotal(0, 0);
            _phases[phase] = new PhaseTotal(total.Count + 1, total.Ms + ms);
            _listener?.Phase = "other";
        }
    }

    private readonly HostSession _session;
    private readonly SdkCacheMode _mode;
    private readonly SdkResolutionListener? _listener;
    private readonly EvaluationContext? _solutionContext;
    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PhaseTotal> _phases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fingerprints = new(StringComparer.OrdinalIgnoreCase);
    private int _multiTargeting;
}
