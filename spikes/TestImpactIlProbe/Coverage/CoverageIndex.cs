namespace TestImpactIlProbe;

/// <summary>Coverage of production files, collected from any number of Cobertura reports.</summary>
internal sealed class CoverageIndex
{
    public int Reports { get; private set; }

    /// <summary>
    /// Files the reports name that are not production files of the graph: tests, generated code, packages
    /// with sources. If nearly every file is here, the reports and the graph disagree about paths.
    /// </summary>
    public IReadOnlyCollection<string> Unmatched => _unmatched;

    public int Matched => _states.Values.Count(state => state != FileCoverage.NotInReport);

    /// <param name="productionFiles">Paths as the graph knows them.</param>
    /// <param name="root">The directory with the sources; relative names in a report are tried against it.</param>
    public CoverageIndex(IEnumerable<string> productionFiles, string root)
    {
        _root = Normalize(root);
        foreach (var file in productionFiles)
        {
            _states[Normalize(file)] = FileCoverage.NotInReport;
        }
    }

    public void Add(CoberturaReport report)
    {
        Reports++;
        foreach (var (name, hit) in report.Files)
        {
            var key = Candidates(name, report.Sources).FirstOrDefault(_states.ContainsKey);
            if (key is null)
            {
                _unmatched.Add(name);
                continue;
            }

            // One report can say "not executed" about a file another report saw executed.
            if (hit)
            {
                _states[key] = FileCoverage.Covered;
            }
            else if (_states[key] == FileCoverage.NotInReport)
            {
                _states[key] = FileCoverage.NotCovered;
            }
        }
    }

    public FileCoverage Of(string productionFile) =>
        _states.TryGetValue(Normalize(productionFile), out var state) ? state : FileCoverage.NotInReport;

    private IEnumerable<string> Candidates(string name, IReadOnlyList<string> sources)
    {
        var normalized = Normalize(name);
        yield return normalized;

        // A deterministic build records sources as "/_/relative/path" instead of the real directory.
        var relative = normalized.StartsWith(DeterministicRoot, StringComparison.Ordinal)
            ? normalized[DeterministicRoot.Length..]
            : normalized.TrimStart('/');
        foreach (var source in sources)
        {
            yield return Join(Normalize(source), relative);
        }

        yield return Join(_root, relative);
    }

    private static string Join(string directory, string relative) => directory.TrimEnd('/') + "/" + relative;

    // Paths are compared as text, without the file system: a report written on Windows must stay readable
    // on another machine, and Path.GetFullPath would treat "E:\..." as a relative path there.
    private static string Normalize(string path) => path.Replace('\\', '/');

    private const string DeterministicRoot = "/_/";

    private readonly string _root;
    private readonly Dictionary<string, FileCoverage> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unmatched = new(StringComparer.OrdinalIgnoreCase);
}
