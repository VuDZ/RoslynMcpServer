using System.Text.Json;

namespace TestImpactIlProbe;

/// <summary>
/// Writes the comparison with coverage: a summary with counts only, which can leave the machine, and a raw
/// file that names the missed files, which stays with the owner of the code.
/// </summary>
internal sealed class CoverageReport
{
    public CoverageReport(LoadedGraph loaded, CoverageIndex coverage, IReadOnlyList<FileAnswer> files)
    {
        _loaded = loaded;
        _coverage = coverage;
        _files = files;
        _summary = CoverageComparison.Summarize(files);
    }

    public IEnumerable<string> Headline()
    {
        yield return $"Coverage reports: {_coverage.Reports}. Production files: {_files.Count}, named by the reports: {_coverage.Matched}.";
        yield return $"Level 0 has tests for {_summary.WithLevelZero.Files} files; tests execute {_summary.WithLevelZero.Covered} of them.";
        foreach (var (layer, answers) in _summary.Layers)
        {
            var empty = answers.EmptyAnswer;
            yield return $"{MeasureReport.Key(layer),-23} empty answers {empty.Files}: executed by tests {empty.Covered}, not executed {empty.NotCovered}, not in the reports {empty.NotInReport}. Missed {answers.MissedShareOfCovered:P1} of the executed files.";
        }

        var untested = _summary.InAssembliesWithoutTests;
        yield return $"Assemblies no test assembly references: {untested.Files} files, executed by tests {untested.Covered}.";
        foreach (var (layer, answers) in _summary.LayersInAssembliesWithoutTests.Where(layer => layer.Value.NonEmptyAnswer.Files > 0))
        {
            yield return $"{MeasureReport.Key(layer),-23} selects tests for {answers.NonEmptyAnswer.Files} of them, leaves {answers.EmptyAnswer.Covered} executed files without an answer.";
        }
    }

    public async Task<(string Raw, string Summary)> WriteAsync(string directory, string root, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var probeCommit = await GitRevision.TryHeadAsync(AppContext.BaseDirectory, cancellationToken).ConfigureAwait(false);
        var graph = _loaded.Graph;
        var summary = new
        {
            kind = "il-coverage-summary",
            probeCommit,
            input = _loaded.Input,
            graph = new
            {
                testAssemblies = graph.TestAssembly.Count(test => test),
                testClasses = graph.TestClasses.Length,
                productionFiles = _files.Count,
            },
            coverage = new
            {
                reports = _coverage.Reports,
                productionFilesInReports = _coverage.Matched,
                otherFilesInReports = _coverage.Unmatched.Count,
            },
            filesWithLevelZero = new
            {
                coverage = _summary.WithLevelZero,
                layers = _summary.Layers.ToDictionary(layer => MeasureReport.Key(layer.Key), layer => layer.Value),
            },
            filesInAssembliesWithoutTests = new
            {
                coverage = _summary.InAssembliesWithoutTests,
                layers = _summary.LayersInAssembliesWithoutTests.ToDictionary(layer => MeasureReport.Key(layer.Key), layer => layer.Value),
            },
        };
        var raw = new
        {
            kind = "il-coverage-raw",
            probeCommit,
            root,
            rootCommit = await GitRevision.TryHeadAsync(root, cancellationToken).ConfigureAwait(false),
            summary,
            missed = Enum.GetValues<Layer>().ToDictionary(
                MeasureReport.Key,
                layer => ByAssembly(CoverageComparison.Missed(_files.Where(file => file.HasLevelZero), layer), root)),
            missedInAssembliesWithoutTests = Enum.GetValues<Layer>().ToDictionary(
                MeasureReport.Key,
                layer => ByAssembly(CoverageComparison.Missed(_files.Where(file => !file.HasLevelZero), layer), root)),
            otherFilesInReports = _coverage.Unmatched.Order(StringComparer.OrdinalIgnoreCase).Take(NamesPerList),
        };
        var rawPath = Path.Combine(directory, "coverage-raw.json");
        var summaryPath = Path.Combine(directory, "coverage-summary.json");
        await File.WriteAllTextAsync(rawPath, JsonSerializer.Serialize(raw, options), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary, options), cancellationToken).ConfigureAwait(false);
        return (rawPath, summaryPath);
    }

    /// <summary>Assemblies with the most files first: a missed dependency usually loses a whole module.</summary>
    private static IEnumerable<object> ByAssembly(IEnumerable<FileAnswer> files, string root) => files
        .GroupBy(file => file.Assembly, StringComparer.Ordinal)
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => new
        {
            assembly = group.Key,
            count = group.Count(),
            files = group.Select(file => Path.GetRelativePath(root, file.Path)).Order(StringComparer.OrdinalIgnoreCase),
        });

    private const int NamesPerList = 300;

    private readonly LoadedGraph _loaded;
    private readonly CoverageIndex _coverage;
    private readonly IReadOnlyList<FileAnswer> _files;
    private readonly CoverageSummary _summary;
}
