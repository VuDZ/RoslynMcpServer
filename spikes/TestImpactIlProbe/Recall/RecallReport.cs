using System.Text.Json;

namespace TestImpactIlProbe;

/// <summary>
/// Writes the comparison with per-class coverage: a summary with counts only, which can leave the machine,
/// and a raw file that names the lost test classes and files, which stays with the owner of the code.
/// </summary>
internal sealed class RecallReport
{
    public RecallReport(LoadedGraph loaded, MeasuredClasses classes, IReadOnlyList<FileRecall> files)
    {
        _loaded = loaded;
        _classes = classes;
        _files = files;
        _selectors = RecallComparison.Summarize(files);
    }

    public IEnumerable<string> Headline()
    {
        yield return $"Test classes: {_classes.InGraph}, measured: {_classes.Measured}, executing no production file: {_classes.ExecutingNothing}, reports for unknown classes: {_classes.UnknownReports}.";
        yield return $"Test assemblies: {_classes.TestAssemblies}, with an idle run: {_classes.TestAssembliesWithIdleRun}, of them executing production files with no test selected: {_classes.TestAssembliesIdleRunExecutes}.";
        foreach (var (selector, counts) in _selectors)
        {
            yield return $"{selector,-23} keeps {counts.Recall:P1} of {counts.Pairs} file-class pairs, {counts.RecallOutsideIdleRun:P1} of {counts.PairsOutsideIdleRun} outside the idle run; files complete {counts.FilesComplete} of {counts.FilesExecuted}, losing a class {counts.FilesLosing} ({counts.FilesLosingOutsideIdleRun} outside the idle run), empty answer {counts.FilesWithEmptyAnswer}; precision {counts.Precision:P1}.";
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
            kind = "il-recall-summary",
            probeCommit,
            input = _loaded.Input,
            graph = new
            {
                testAssemblies = graph.TestAssembly.Count(test => test),
                testClasses = graph.TestClasses.Length,
                productionFiles = _files.Count,
            },
            classes = _classes,
            selectors = _selectors,
        };
        var raw = new
        {
            kind = "il-recall-raw",
            probeCommit,
            root,
            rootCommit = await GitRevision.TryHeadAsync(root, cancellationToken).ConfigureAwait(false),
            summary,
            lost = _selectors.Keys.ToDictionary(selector => selector, selector => new
            {
                byTestClass = LostByTestClass(selector, root),
                byFile = LostByFile(selector, root),
            }),
        };
        var rawPath = Path.Combine(directory, "recall-raw.json");
        var summaryPath = Path.Combine(directory, "recall-summary.json");
        await File.WriteAllTextAsync(rawPath, JsonSerializer.Serialize(raw, options), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary, options), cancellationToken).ConfigureAwait(false);
        return (rawPath, summaryPath);
    }

    /// <summary>Test classes lost for the most files first: one invisible dependency usually loses a class everywhere.</summary>
    private IEnumerable<object> LostByTestClass(string selector, string root) => _files
        .SelectMany(file => file.Selectors[selector].Lost.Select(test => (Test: test, File: file)))
        .GroupBy(lost => lost.Test)
        .Select(group => (
            Test: group.Key,
            Files: group.Select(lost => lost.File).ToList(),
            OutsideIdleRun: group.Where(lost => lost.File.Selectors[selector].LostOutsideIdleRun.Contains(lost.Test)).Select(lost => lost.File).ToList()))
        .OrderByDescending(lost => lost.OutsideIdleRun.Count)
        .ThenByDescending(lost => lost.Files.Count)
        .ThenBy(lost => lost.Test)
        .Take(NamesPerList)
        .Select(lost => new
        {
            testClass = Describe(lost.Test),
            files = lost.Files.Count,
            filesOutsideIdleRun = lost.OutsideIdleRun.Count,
            sample = lost.OutsideIdleRun
                .Concat(lost.Files.Except(lost.OutsideIdleRun))
                .Take(SamplePerName)
                .Select(file => Path.GetRelativePath(root, file.Path)),
        });

    /// <summary>
    /// Files that lose a class outside the idle run first, and those classes first in the sample: the other
    /// losses may be the search for tests of the assembly, not a dependency the graph misses.
    /// </summary>
    private IEnumerable<object> LostByFile(string selector, string root) => _files
        .Where(file => file.Selectors[selector].Lost.Count > 0)
        .OrderByDescending(file => file.Selectors[selector].LostOutsideIdleRun.Count)
        .ThenByDescending(file => file.Selectors[selector].Lost.Count)
        .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
        .Take(NamesPerList)
        .Select(file => new
        {
            file = Path.GetRelativePath(root, file.Path),
            file.Assembly,
            file.Executing,
            lost = file.Selectors[selector].Lost.Count,
            lostOutsideIdleRun = file.Selectors[selector].LostOutsideIdleRun.Count,
            sample = file.Selectors[selector].LostOutsideIdleRun
                .Concat(file.Selectors[selector].Lost.Except(file.Selectors[selector].LostOutsideIdleRun))
                .Take(SamplePerName)
                .Select(Describe),
        });

    private string Describe(int node) =>
        $"{_loaded.Graph.Names[node]}, {_loaded.Graph.Assemblies[_loaded.Graph.AssemblyOf[node]]}";

    private const int NamesPerList = 100;
    private const int SamplePerName = 10;

    private readonly LoadedGraph _loaded;
    private readonly MeasuredClasses _classes;
    private readonly IReadOnlyList<FileRecall> _files;
    private readonly Dictionary<string, RecallCounts> _selectors;
}
