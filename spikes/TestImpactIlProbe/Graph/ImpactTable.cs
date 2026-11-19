using System.Numerics;

namespace TestImpactIlProbe;

/// <summary>Answers "which tests does a change of these types affect" from the closures of all test classes.</summary>
internal sealed class ImpactTable
{
    public ImpactTable(DependencyGraph graph)
    {
        _graph = graph;
        foreach (var layer in Enum.GetValues<Layer>())
        {
            _rows[layer] = TestClosures.Compute(graph, layer);
        }
    }

    /// <summary>Test classes whose closure in <paramref name="layer"/> holds any of <paramref name="types"/>.</summary>
    public List<int> Affected(Layer layer, IReadOnlyCollection<int> types)
    {
        var rows = _rows[layer];
        var affected = new List<int>();
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            foreach (var type in types)
            {
                if ((row[type >> 6] & (1UL << (type & 63))) != 0)
                {
                    affected.Add(_graph.TestClasses[index]);
                    break;
                }
            }
        }

        return affected;
    }

    /// <summary>Level 0: test classes of every test assembly that references an assembly of the types.</summary>
    public List<int> LevelZero(IReadOnlyCollection<int> types)
    {
        var assemblies = types.Select(type => _graph.AssemblyOf[type]).ToHashSet();
        return [.. _graph.TestClasses.Where(test => _graph.AssemblyClosure[_graph.AssemblyOf[test]].Overlaps(assemblies))];
    }

    public TestSet Count(IReadOnlyCollection<int> tests) => new(
        tests.Sum(test => _graph.TestMethods[test]),
        tests.Count,
        tests.Select(test => _graph.AssemblyOf[test]).Distinct().Count());

    /// <summary>For every type, the number of test methods whose class reaches it in <paramref name="layer"/>.</summary>
    public int[] MethodsByType(Layer layer)
    {
        var methods = new int[_graph.Names.Length];
        var rows = _rows[layer];
        for (var index = 0; index < rows.Length; index++)
        {
            var weight = _graph.TestMethods[_graph.TestClasses[index]];
            var row = rows[index];
            for (var word = 0; word < row.Length; word++)
            {
                for (var bits = row[word]; bits != 0; bits &= bits - 1)
                {
                    methods[(word << 6) + BitOperations.TrailingZeroCount(bits)] += weight;
                }
            }
        }

        return methods;
    }

    /// <summary>Test classes whose closure holds a type that hands an assembly to reflection.</summary>
    public int ReachingScan(Layer layer) => Reaching(layer, node => _graph.Scans[node].Length > 0);

    /// <summary>Test classes whose closure in <paramref name="layer"/> holds a type that <paramref name="marked"/> accepts.</summary>
    public int Reaching(Layer layer, Func<int, bool> marked)
    {
        var mask = new ulong[(_graph.Names.Length + 63) / 64];
        for (var node = 0; node < _graph.Names.Length; node++)
        {
            if (marked(node))
            {
                mask[node >> 6] |= 1UL << (node & 63);
            }
        }

        return _rows[layer].Count(row => row.Zip(mask, (left, right) => left & right).Any(word => word != 0));
    }

    /// <summary>Closure sizes in types, one per test class.</summary>
    public int[] ClosureSizes(Layer layer) =>
        [.. _rows[layer].Select(row => row.Sum(BitOperations.PopCount))];

    private readonly DependencyGraph _graph;
    private readonly Dictionary<Layer, ulong[][]> _rows = [];
}
