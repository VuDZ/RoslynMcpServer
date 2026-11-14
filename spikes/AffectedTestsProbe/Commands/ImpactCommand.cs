using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

/// <summary>
/// Affected tests by reverse search for edits, in one process, without a build and without the forward graph.
/// It reports what an answer costs (time and memory) and how much smaller it is than level 0.
/// Completeness is not measured: that needs the mutation oracle, which needs a build.
/// </summary>
internal static class ImpactCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var solutionPath = Path.GetFullPath(CommandArgs.Value(args, "--solution") ?? throw new InvalidOperationException("Pass --solution."));
        var baseDirectory = Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory;
        var configuration = CommandArgs.Value(args, "--configuration") ?? "Debug";
        var platform = CommandArgs.Value(args, "--platform") ?? "AnyCPU";
        var tests = CommandArgs.Values(args, "--test-project");
        var membersFile = CommandArgs.Value(args, "--members");
        var filesFile = CommandArgs.Value(args, "--files");
        var sample = CommandArgs.Integer(args, "--sample", membersFile is null && filesFile is null ? 20 : 0);
        var seed = CommandArgs.Integer(args, "--seed", 1);
        var output = Path.GetFullPath(CommandArgs.Value(args, "--out") ?? "impact-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        var report = new ImpactReport(solutionPath, tests, seed);
        var failures = new List<WorkspaceDiagnostic>();
        var watch = Stopwatch.StartNew();
        using var workspace = await SolutionLoader.OpenAsync(solutionPath, configuration, platform, failures, cancellationToken)
            .ConfigureAwait(false);
        report.Phase("load", watch);
        var solution = workspace.CurrentSolution;
        var map = new ProjectMap(solution, configuration, platform);

        watch.Restart();
        await SearchPrewarm.WarmAsync(solution, cancellationToken).ConfigureAwait(false);
        report.Phase("prewarm", watch);

        watch.Restart();
        var roots = await RootIndexBuilder.BuildAsync(solution, map, tests, cancellationToken).ConfigureAwait(false);
        report.Phase("test-roots", watch);
        report.Describe(solution, roots, failures);
        Console.Error.WriteLine($"Loaded: {solution.ProjectIds.Count} projects, {roots.Units.Count} test methods.");

        var targets = new List<(ImpactTarget Target, long LocateMs)>();
        targets.AddRange(await LocateEachAsync(
            await ReadListAsync(membersFile, cancellationToken).ConfigureAwait(false),
            (id, token) => ImpactTargets.FromMembersAsync(solution, map, [id], token),
            cancellationToken).ConfigureAwait(false));
        targets.AddRange(await LocateEachAsync(
            await ReadListAsync(filesFile, cancellationToken).ConfigureAwait(false),
            (file, token) => ImpactTargets.FromFilesAsync(solution, map, [file], baseDirectory, token),
            cancellationToken).ConfigureAwait(false));
        if (sample > 0)
        {
            watch.Restart();
            var sampled = await ImpactTargets.SampleAsync(solution, map, roots, sample, seed, cancellationToken).ConfigureAwait(false);
            report.Phase("sample", watch);
            targets.AddRange(sampled.Select(target => (target, 0L)));
        }

        for (var index = 0; index < targets.Count; index++)
        {
            var (target, locateMs) = targets[index];
            var result = await AnswerAsync(solution, map, roots, target, locateMs, cancellationToken).ConfigureAwait(false);
            report.Add(result);
            Console.Error.WriteLine(ImpactReport.ProgressLine(index + 1, targets.Count, result));
        }

        if (!CommandArgs.Has(args, "--no-quality"))
        {
            watch.Restart();
            report.SetQuality(await LoadQuality.MeasureAsync(solution, cancellationToken).ConfigureAwait(false));
            report.Phase("load-quality", watch);
        }

        var (raw, summary) = await report.WriteAsync(output, cancellationToken).ConfigureAwait(false);
        Console.WriteLine("Raw (names, keep it private): " + raw);
        Console.WriteLine("Summary (counts only, can be shared): " + summary);
        return 0;
    }

    private static async Task<ImpactResult> AnswerAsync(
        Solution solution,
        ProjectMap map,
        RootIndex roots,
        ImpactTarget target,
        long locateMs,
        CancellationToken cancellationToken)
    {
        if (target.Locations.Count == 0)
        {
            return new ImpactResult(target, "missing", null, locateMs, 0, 0, 0, [], [], MemorySnapshot.Capture());
        }

        var levelZero = new HashSet<TestUnit>();
        foreach (var location in target.Locations)
        {
            levelZero.UnionWith(LevelZero.Split(solution, map, roots, location.Symbol).InScope);
        }

        var starts = target.Locations.Select(location => location.Node).Distinct().ToArray();
        var outcome = await ReverseSearch.SearchAsync(solution, map, roots, starts, ProbeLimits.Default, cancellationToken)
            .ConfigureAwait(false);
        var predicted = outcome.Limit is null ? outcome.Units : levelZero;
        return new ImpactResult(
            target,
            outcome.Limit is null ? "reverse" : "level0",
            outcome.Limit,
            locateMs,
            outcome.ElapsedMilliseconds,
            outcome.Visited,
            outcome.Depth,
            Sorted(predicted),
            Sorted(levelZero),
            MemorySnapshot.Capture());
    }

    private static async Task<List<(ImpactTarget Target, long LocateMs)>> LocateEachAsync(
        IReadOnlyList<string> items,
        Func<string, CancellationToken, Task<List<ImpactTarget>>> locate,
        CancellationToken cancellationToken)
    {
        var located = new List<(ImpactTarget Target, long LocateMs)>();
        foreach (var item in items)
        {
            var watch = Stopwatch.StartNew();
            var targets = await locate(item, cancellationToken).ConfigureAwait(false);
            located.AddRange(targets.Select(target => (target, (long)watch.Elapsed.TotalMilliseconds)));
        }

        return located;
    }

    private static async Task<IReadOnlyList<string>> ReadListAsync(string? path, CancellationToken cancellationToken)
    {
        if (path is null)
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        return lines.Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToArray();
    }

    private static TestUnit[] Sorted(IEnumerable<TestUnit> units) =>
        units.OrderBy(unit => unit.Key, StringComparer.Ordinal).ToArray();
}
