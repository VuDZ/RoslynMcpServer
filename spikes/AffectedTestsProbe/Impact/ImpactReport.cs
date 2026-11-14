using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

/// <summary>
/// Collects one <c>impact</c> run and writes two files. <c>raw.json</c> has names, paths, and test names and stays
/// with the owner of the solution. <c>summary.json</c> has only counts, kinds, times, and memory, and may be shared.
/// </summary>
internal sealed class ImpactReport
{
    public ImpactReport(string solutionPath, IReadOnlyList<string> testProjects, int seed)
    {
        _solutionPath = solutionPath;
        _testProjects = testProjects;
        _seed = seed;
    }

    public IReadOnlyList<ImpactResult> Results => _results;

    public void Phase(string name, Stopwatch watch)
    {
        var elapsed = (long)watch.Elapsed.TotalMilliseconds;
        _phases.Add(new PhaseRow(name, elapsed, MemorySnapshot.Capture()));
    }

    public void Describe(Solution solution, RootIndex roots, IReadOnlyList<WorkspaceDiagnostic> failures)
    {
        var units = roots.Units;
        _solution = new
        {
            projects = solution.ProjectIds.Count,
            csharpProjects = solution.Projects.Count(project => project.Language == LanguageNames.CSharp),
            testProjects = units.Select(unit => unit.Project).Distinct().Count(),
            testClasses = roots.Classes.Count(testClass => testClass.Units.Count > 0),
            testUnits = units.Count,
            unmappedTypes = roots.Unmapped.Count,
            rootLimitations = roots.Limitations.Count,
            loadFailures = failures.Count(failure => failure.Kind == WorkspaceDiagnosticKind.Failure),
            loadWarnings = failures.Count(failure => failure.Kind == WorkspaceDiagnosticKind.Warning),
        };
        _failures = failures.Select(failure => failure.Kind + ": " + failure.Message).ToArray();
    }

    public void Add(ImpactResult result) => _results.Add(result);

    public void SetQuality(IReadOnlyList<ProjectQuality> quality) => _quality = quality;

    public async Task<(string Raw, string Summary)> WriteAsync(string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var probeCommit = await GitRevision.TryHeadAsync(AppContext.BaseDirectory, cancellationToken).ConfigureAwait(false);
        var solutionDirectory = Path.GetDirectoryName(_solutionPath) ?? Environment.CurrentDirectory;
        var solutionCommit = await GitRevision.TryHeadAsync(solutionDirectory, cancellationToken).ConfigureAwait(false);
        var summary = new
        {
            kind = "impact-summary",
            probeCommit,
            machine = Machine(),
            limits = Limits(),
            seed = _seed,
            testProjectFilters = _testProjects.Count,
            solution = _solution,
            phases = _phases,
            quality = QualitySummary(),
            totals = Totals(),
            targets = _results.Select((result, index) => SummaryRow(index + 1, result)),
        };
        var raw = new
        {
            kind = "impact-raw",
            probeCommit,
            solution = _solutionPath,
            solutionCommit,
            testProjectFilters = _testProjects,
            summary,
            loadDiagnostics = _failures,
            quality = _quality?.Where(project => project.NoFramework || project.Errors > 0),
            targets = _results.Select((result, index) => RawRow(index + 1, result)),
        };
        var rawPath = Path.Combine(directory, "raw.json");
        var summaryPath = Path.Combine(directory, "summary.json");
        await File.WriteAllTextAsync(rawPath, JsonSerializer.Serialize(raw, options), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary, options), cancellationToken).ConfigureAwait(false);
        return (rawPath, summaryPath);
    }

    public static string ProgressLine(int index, int count, ImpactResult result)
    {
        var predicted = TestSetStats.From(result.Predicted);
        var levelZero = TestSetStats.From(result.LevelZero);
        var limit = result.Limit is null ? "" : " (" + result.Limit + ")";
        return $"[{index}/{count}] {result.Target.Source}, members {result.Target.Members.Count}: {result.Answer}{limit}, "
            + $"search {result.SearchMs} ms, visited {result.Visited}, tests {predicted.Units} of level 0 {levelZero.Units}, "
            + $"classes {predicted.Classes} of {levelZero.Classes}, managed {result.After.ManagedMb} MB";
    }

    private static object Machine() => new
    {
        os = RuntimeInformation.OSDescription,
        runtime = RuntimeInformation.FrameworkDescription,
        processors = Environment.ProcessorCount,
        memoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024),
    };

    private static object Limits() => new
    {
        ProbeLimits.Default.Depth,
        ProbeLimits.Default.Members,
        perMemberSeconds = ProbeLimits.Default.PerMember.TotalSeconds,
    };

    private object? QualitySummary()
    {
        if (_quality is null)
        {
            return null;
        }

        return new
        {
            projects = _quality.Count,
            noFramework = _quality.Count(project => project.NoFramework),
            withDeclarationErrors = _quality.Count(project => project.Errors > 0),
            declarationErrors = _quality.Sum(project => project.Errors),
            topCodes = _quality
                .SelectMany(project => project.ErrorCodes)
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(group => new { code = group.Key, count = group.Sum(pair => pair.Value) })
                .OrderByDescending(item => item.count)
                .Take(10),
        };
    }

    private object Totals()
    {
        var answered = _results.Where(result => result.Answer != "missing").ToArray();
        var reverse = answered.Where(result => result.Answer == "reverse").ToArray();
        var shares = reverse
            .Where(result => result.LevelZero.Count > 0)
            .Select(result => (double)result.Predicted.Count / result.LevelZero.Count)
            .ToArray();
        var searches = answered.Select(result => result.SearchMs).ToArray();
        return new
        {
            targets = _results.Count,
            reverse = reverse.Length,
            level0 = answered.Length - reverse.Length,
            missing = _results.Count - answered.Length,
            emptyReverse = reverse.Count(result => result.Predicted.Count == 0 && result.LevelZero.Count > 0),
            firstSearchMs = answered.FirstOrDefault()?.SearchMs,
            searchMs = new { median = Percentile(searches, 0.5), p90 = Percentile(searches, 0.9), max = searches.DefaultIfEmpty().Max() },
            shareOfLevelZero = new { median = Percentile(shares, 0.5), p90 = Percentile(shares, 0.9) },
            peakWorkingSetMb = _phases.Select(phase => phase.Memory.PeakWorkingSetMb)
                .Concat(_results.Select(result => result.After.PeakWorkingSetMb))
                .DefaultIfEmpty()
                .Max(),
        };
    }

    private static object SummaryRow(int index, ImpactResult result)
    {
        var levelZero = TestSetStats.From(result.LevelZero);
        return new
        {
            index,
            source = result.Target.Source,
            members = result.Target.Members.Count,
            kinds = result.Target.Kinds
                .GroupBy(kind => kind, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            keys = result.Target.Locations.Count,
            answer = result.Answer,
            limit = result.Limit,
            locateMs = result.LocateMs,
            searchMs = result.SearchMs,
            visited = result.Visited,
            depth = result.Depth,
            predicted = TestSetStats.From(result.Predicted),
            level0 = levelZero,
            share = levelZero.Units == 0 ? (double?)null : Math.Round((double)result.Predicted.Count / levelZero.Units, 3),
            memoryAfter = result.After,
        };
    }

    private static object RawRow(int index, ImpactResult result) => new
    {
        index,
        source = result.Target.Source,
        label = result.Target.Label,
        members = result.Target.Members,
        answer = result.Answer,
        limit = result.Limit,
        searchMs = result.SearchMs,
        predicted = result.Predicted.Select(Name),
        level0 = TestSetStats.From(result.LevelZero),
    };

    private static string Name(TestUnit unit) =>
        Path.GetFileNameWithoutExtension(unit.Project.ProjectPath) + "(" + unit.Project.TargetFramework + "): " + unit;

    private static double? Percentile(IReadOnlyList<double> values, double rank)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.OrderBy(value => value).ToArray();
        var position = (int)Math.Ceiling(rank * sorted.Length) - 1;
        return Math.Round(sorted[Math.Clamp(position, 0, sorted.Length - 1)], 3);
    }

    private static long? Percentile(IReadOnlyList<long> values, double rank) =>
        (long?)Percentile(values.Select(value => (double)value).ToArray(), rank);

    private sealed record PhaseRow(string Name, long Ms, MemorySnapshot Memory);

    private readonly string _solutionPath;
    private readonly IReadOnlyList<string> _testProjects;
    private readonly int _seed;
    private readonly List<PhaseRow> _phases = new();
    private readonly List<ImpactResult> _results = new();
    private object? _solution;
    private string[] _failures = [];
    private IReadOnlyList<ProjectQuality>? _quality;
}
