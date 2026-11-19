using System.Runtime.InteropServices;
using System.Text.Json;

namespace TestImpactIlProbe;

/// <summary>
/// Builds the two outputs of a measurement: a summary with counts only, which can leave the machine, and a raw
/// file with paths and type names, which stays with the owner of the code.
/// </summary>
internal sealed class MeasureReport
{
    public MeasureReport(LoadedGraph loaded, ImpactTable table, ProductionCode production)
    {
        _loaded = loaded;
        _graph = loaded.Graph;
        _table = table;
        _production = production;
        _allMethods = _graph.TestClasses.Sum(test => _graph.TestMethods[test]);
        _levelZeroMethods = new int[_graph.Assemblies.Length];
        foreach (var test in _graph.TestClasses)
        {
            foreach (var assembly in _graph.AssemblyClosure[_graph.AssemblyOf[test]])
            {
                _levelZeroMethods[assembly] += _graph.TestMethods[test];
            }
        }
    }

    public TargetResult Answer(string source, string path, IReadOnlyList<int> types) => new(
        source,
        path,
        types,
        _table.LevelZero(types),
        Enum.GetValues<Layer>().ToDictionary(layer => layer, layer => _table.Affected(layer, types)));

    public async Task<(string Raw, string Summary)> WriteAsync(
        string directory,
        string root,
        int seed,
        IReadOnlyList<TargetResult> targets,
        IReadOnlyList<string> missing,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var probeCommit = await GitRevision.TryHeadAsync(AppContext.BaseDirectory, cancellationToken).ConfigureAwait(false);
        var summary = new
        {
            kind = "il-impact-summary",
            probeCommit,
            machine = new
            {
                os = RuntimeInformation.OSDescription,
                runtime = RuntimeInformation.FrameworkDescription,
                processors = Environment.ProcessorCount,
                memoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024),
            },
            seed,
            input = _loaded.Input,
            graph = GraphCounts(),
            phases = _loaded.Phases,
            loadsByName = LoadCounts(),
            closures = Enum.GetValues<Layer>().ToDictionary(Key, ClosureCounts),
            byType = ByType(),
            byFile = ByFile(),
            missingFiles = missing.Count,
            targets = targets.Select((target, index) => SummaryRow(index + 1, target)),
        };
        var raw = new
        {
            kind = "il-impact-raw",
            probeCommit,
            root,
            rootCommit = await GitRevision.TryHeadAsync(root, cancellationToken).ConfigureAwait(false),
            unreadablePdbs = _loaded.UnreadablePdbs,
            testAssemblies = _graph.Assemblies.Where((_, index) => _graph.TestAssembly[index]),
            missingFiles = missing,
            summary,
            targets = targets.Select((target, index) => RawRow(index + 1, target)),
        };
        var rawPath = Path.Combine(directory, "raw.json");
        var summaryPath = Path.Combine(directory, "summary.json");
        await File.WriteAllTextAsync(rawPath, JsonSerializer.Serialize(raw, options), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary, options), cancellationToken).ConfigureAwait(false);
        return (rawPath, summaryPath);
    }

    /// <summary>One line per layer for the console: the median share of level 0 over all production files.</summary>
    public IEnumerable<string> Headline()
    {
        var files = FileShares();
        yield return $"Test classes: {_graph.TestClasses.Length}, test methods: {_allMethods}, production files: {files.Count}.";
        foreach (var layer in Enum.GetValues<Layer>())
        {
            var shares = Distribution.Of(files.Select(file => file.Shares[layer]));
            yield return $"{Key(layer),-23} share of level 0 by file: median {shares.Median:P1}, p90 {shares.P90:P1}, no tests {shares.Zero}, at least 80% {shares.AtLeast80Percent}.";
        }
    }

    internal static string Key(Layer layer) => layer switch
    {
        Layer.References => "references",
        Layer.Discovery => "discovery",
        Layer.WholeAssembly => "wholeAssembly",
        Layer.ReferencesWithLoads => "referencesWithLoads",
        Layer.DiscoveryWithLoads => "discoveryWithLoads",
        _ => "discoveryWithNamedLoads",
    };

    private object GraphCounts() => new
    {
        types = _graph.Names.Length,
        edges = _graph.Edges,
        testAssemblies = _graph.TestAssembly.Count(test => test),
        testClasses = _graph.TestClasses.Length,
        testMethods = _allMethods,
        productionAssemblies = _graph.TestAssembly.Count(test => !test),
        productionAssembliesWithoutTests = Enumerable.Range(0, _graph.Assemblies.Length)
            .Count(assembly => !_graph.TestAssembly[assembly] && _levelZeroMethods[assembly] == 0),
        productionTypes = _production.Types.Count,
        productionFiles = _production.Files.Count,
        typesHandingAssembliesToReflection = _graph.Scans.Count(scans => scans.Length > 0),
        frameworkFacingTypes = _graph.FrameworkFacing.Count(facing => facing),
        opaqueAssemblyLoads = _graph.OpaqueLoads,
        unreadableBodies = _graph.UnreadableBodies,
    };

    private object ClosureCounts(Layer layer)
    {
        var sizes = _table.ClosureSizes(layer).Select(size => (double)size / _graph.Names.Length);
        return new
        {
            shareOfGraphTypes = Distribution.Of(sizes),
            testClassesReachingReflectionOverAssembly = _table.ReachingScan(layer),
            testClassesReachingLoadByName = _table.Reaching(layer, node => _graph.Loads[node] != AssemblyLoadKinds.None),
        };
    }

    /// <summary>
    /// For each way of loading assemblies by name: the types that use it and the test classes that reach
    /// such a type through references alone. It tells which call makes the rule select every test.
    /// </summary>
    private Dictionary<string, object> LoadCounts()
    {
        var counts = new Dictionary<string, object>();
        foreach (var kind in Enum.GetValues<AssemblyLoadKinds>().Where(kind => kind != AssemblyLoadKinds.None))
        {
            counts[JsonNamingPolicy.CamelCase.ConvertName(kind.ToString())] = new
            {
                types = _graph.Loads.Count(loads => loads.HasFlag(kind)),
                testClassesReaching = _table.Reaching(Layer.References, node => _graph.Loads[node].HasFlag(kind)),
            };
        }

        return counts;
    }

    private object ByType()
    {
        var tested = _production.Types.Where(type => _levelZeroMethods[_graph.AssemblyOf[type]] > 0).ToList();
        var layers = new Dictionary<string, object>();
        foreach (var layer in Enum.GetValues<Layer>())
        {
            var methods = _table.MethodsByType(layer);
            layers[Key(layer)] = new
            {
                ofLevelZero = Distribution.Of(tested.Select(type => (double)methods[type] / _levelZeroMethods[_graph.AssemblyOf[type]])),
                ofAllTests = Distribution.Of(tested.Select(type => (double)methods[type] / _allMethods)),
            };
        }

        return new
        {
            types = tested.Count,
            typesInAssembliesWithoutTests = _production.Types.Count - tested.Count,
            levelZeroOfAllTests = Distribution.Of(tested.Select(type => (double)_levelZeroMethods[_graph.AssemblyOf[type]] / _allMethods)),
            layers,
        };
    }

    private object ByFile()
    {
        var files = FileShares();
        return new
        {
            files = files.Count,
            filesInAssembliesWithoutTests = _production.Files.Count - files.Count,
            answeredInAssembliesWithoutTests = AnsweredWithoutLevelZero(),
            levelZeroOfAllTests = Distribution.Of(files.Select(file => (double)file.LevelZero / _allMethods)),
            layers = Enum.GetValues<Layer>().ToDictionary(Key, layer => new
            {
                ofLevelZero = Distribution.Of(files.Select(file => file.Shares[layer])),
                ofAllTests = Distribution.Of(files.Select(file => file.Shares[layer] * file.LevelZero / _allMethods)),
            }),
        };
    }

    private List<(int LevelZero, Dictionary<Layer, double> Shares)> FileShares()
    {
        if (_fileShares is not null)
        {
            return _fileShares;
        }

        var rows = new (int LevelZero, Dictionary<Layer, double> Shares)?[_production.Files.Count];
        Parallel.For(0, rows.Length, index =>
        {
            var types = _production.Files[index].Types;
            var levelZero = LevelZeroMethods(types);
            if (levelZero > 0)
            {
                rows[index] = (levelZero, Enum.GetValues<Layer>().ToDictionary(
                    layer => layer,
                    layer => (double)_table.Count(_table.Affected(layer, types)).Methods / levelZero));
            }
        });
        _fileShares = [.. rows.Where(row => row is not null).Select(row => row!.Value)];
        return _fileShares;
    }

    /// <summary>
    /// Files that level 0 has no tests for and a layer selects tests for. Level 0 follows references in
    /// metadata; a load by name reaches an assembly that is only copied next to the test assembly.
    /// </summary>
    private Dictionary<string, int> AnsweredWithoutLevelZero()
    {
        var untested = _production.Files.Where(file => LevelZeroMethods(file.Types) == 0).ToList();
        return Enum.GetValues<Layer>().ToDictionary(
            Key,
            layer => untested.AsParallel().Count(file => _table.Affected(layer, file.Types).Count > 0));
    }

    private int LevelZeroMethods(IReadOnlyList<int> types)
    {
        // A file linked into several projects is rare; one assembly is a lookup.
        var first = _graph.AssemblyOf[types[0]];
        return types.All(type => _graph.AssemblyOf[type] == first)
            ? _levelZeroMethods[first]
            : _table.Count(_table.LevelZero(types)).Methods;
    }

    private object SummaryRow(int index, TargetResult target)
    {
        var levelZero = _table.Count(target.LevelZero);
        return new
        {
            index,
            target.Source,
            types = target.Types.Count,
            assemblies = target.Types.Select(type => _graph.AssemblyOf[type]).Distinct().Count(),
            levelZero,
            layers = target.Affected.ToDictionary(layer => Key(layer.Key), layer =>
            {
                var selected = _table.Count(layer.Value);
                return new
                {
                    selected.Methods,
                    selected.Classes,
                    selected.Assemblies,
                    shareOfLevelZero = levelZero.Methods == 0 ? 0 : Math.Round((double)selected.Methods / levelZero.Methods, 4),
                };
            }),
        };
    }

    private object RawRow(int index, TargetResult target) => new
    {
        index,
        target.Source,
        file = target.Path,
        types = target.Types.Select(Describe),
        levelZeroClasses = target.LevelZero.Count,
        layers = target.Affected.ToDictionary(layer => Key(layer.Key), layer => new
        {
            classes = layer.Value.Count,
            names = layer.Value.Take(NamesPerLayer).Select(Describe),
        }),
    };

    private string Describe(int node) => $"{_graph.Names[node]}, {_graph.Assemblies[_graph.AssemblyOf[node]]}";

    private const int NamesPerLayer = 300;

    private readonly LoadedGraph _loaded;
    private readonly DependencyGraph _graph;
    private readonly ImpactTable _table;
    private readonly ProductionCode _production;
    private readonly int _allMethods;
    private readonly int[] _levelZeroMethods;
    private List<(int LevelZero, Dictionary<Layer, double> Shares)>? _fileShares;
}
