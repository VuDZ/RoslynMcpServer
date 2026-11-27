using System.Numerics;
using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>
/// Answers "which types does a test class depend on" for every stage of one graph. The closures of a stage
/// are computed on its first use, one bit row per test class, and kept.
/// </summary>
internal sealed class IlTestClosures
{
    /// <summary>The test classes of the graph, in the order of their nodes.</summary>
    public IReadOnlyList<IlTypeName> TestClasses { get; }

    public IlTestClosures(IlDependencyGraph graph)
    {
        _graph = graph;
        TestClasses = [.. graph.TestClasses.Select(node => graph.Types[node])];
        for (var row = 0; row < graph.TestClasses.Length; row++)
        {
            _rowByTestClass[graph.TestClasses[row]] = row;
        }

        _rows = [.. Enum.GetValues<IlDependencyStage>().Select(stage => new Lazy<ulong[][]>(() => Compute(graph, stage)))];
    }

    /// <summary>
    /// Types the test class depends on at the stage, itself included, in the order of their nodes. Empty for
    /// a type that is not a test class of the graph.
    /// </summary>
    public IReadOnlyList<IlTypeName> ReachedTypes(IlTypeName testClass, IlDependencyStage stage)
    {
        if (!TryGetRow(testClass, stage, out var row))
        {
            return [];
        }

        var reached = new List<IlTypeName>();
        for (var word = 0; word < row.Length; word++)
        {
            for (var bits = row[word]; bits != 0; bits &= bits - 1)
            {
                reached.Add(_graph.Types[(word << 6) + BitOperations.TrailingZeroCount(bits)]);
            }
        }

        return reached;
    }

    /// <summary>False when <paramref name="testClass"/> is not a test class or <paramref name="type"/> is not in the graph.</summary>
    public bool Reaches(IlTypeName testClass, IlTypeName type, IlDependencyStage stage) =>
        TryGetRow(testClass, stage, out var row)
        && _graph.NodeByType.TryGetValue(type, out var node)
        && Holds(row, node);

    /// <summary>
    /// Test classes that depend on any of <paramref name="types"/> at the stage, in the order of their nodes.
    /// Types missing from the graph are ignored.
    /// </summary>
    public IReadOnlyList<IlTypeName> AffectedTestClasses(IReadOnlyCollection<IlTypeName> types, IlDependencyStage stage)
    {
        var nodes = new List<int>();
        foreach (var type in types)
        {
            if (_graph.NodeByType.TryGetValue(type, out var node))
            {
                nodes.Add(node);
            }
        }

        var rows = _rows[(int)stage].Value;
        var affected = new List<IlTypeName>();
        for (var index = 0; index < rows.Length; index++)
        {
            if (nodes.Exists(node => Holds(rows[index], node)))
            {
                affected.Add(TestClasses[index]);
            }
        }

        return affected;
    }

    private bool TryGetRow(IlTypeName testClass, IlDependencyStage stage, out ulong[] row)
    {
        if (_graph.NodeByType.TryGetValue(testClass, out var node) && _rowByTestClass.TryGetValue(node, out var index))
        {
            row = _rows[(int)stage].Value[index];
            return true;
        }

        row = [];
        return false;
    }

    private static bool Holds(ulong[] row, int node) => (row[node >> 6] & (1UL << (node & 63))) != 0;

    /// <summary>Rows are in the order of <see cref="IlDependencyGraph.TestClasses"/>.</summary>
    private static ulong[][] Compute(IlDependencyGraph graph, IlDependencyStage stage)
    {
        var rows = new ulong[graph.TestClasses.Length][];
        Parallel.For(
            0,
            rows.Length,
            () => new Walker(graph, stage),
            (index, _, walker) =>
            {
                rows[index] = walker.Walk(graph.TestClasses[index]);
                return walker;
            },
            _ => { });
        return rows;
    }

    /// <summary>
    /// Walks the graph from one test class at a time. The marks of a walk are numbered instead of cleared,
    /// so one walker serves many test classes without allocating per walk anything but the answer row.
    /// </summary>
    private sealed class Walker
    {
        public Walker(IlDependencyGraph graph, IlDependencyStage stage)
        {
            _graph = graph;
            _discovers = stage is IlDependencyStage.Discovery or IlDependencyStage.DiscoveryWithLoads;
            _followsLoads = stage == IlDependencyStage.DiscoveryWithLoads;
            _reached = new int[graph.Types.Length];
            _scanned = new int[graph.Assemblies.Length];
        }

        public ulong[] Walk(int root)
        {
            _walk++;
            _row = new ulong[(_graph.Types.Length + 63) / 64];
            var loaded = false;
            Reach(root);
            while (_pending.TryPop(out var node))
            {
                foreach (var dependency in _graph.Dependencies[node])
                {
                    Reach(dependency);
                }

                if (_followsLoads && _graph.Loads[node] != IlAssemblyLoadKinds.None && !loaded)
                {
                    loaded = true;
                    Load(_graph.AssemblyOf[root]);
                }

                if (!_discovers)
                {
                    continue;
                }

                foreach (var assembly in _graph.Scans[node])
                {
                    ScanOnce(assembly);
                }

                // A container resolves a service by a type the consumer names, so an implementation counts
                // only once something it implements is reached.
                foreach (var subtype in _graph.Subtypes[node])
                {
                    if (_scanned[_graph.AssemblyOf[subtype]] == _walk)
                    {
                        Discover(subtype);
                    }
                }
            }

            return _row;
        }

        private void Scan(int assembly)
        {
            foreach (var type in _graph.TypesByAssembly[assembly])
            {
                if (_graph.FrameworkFacing[type] || _graph.Supertypes[type].Any(super => _reached[super] == _walk))
                {
                    Discover(type);
                }
            }
        }

        /// <summary>
        /// Which assemblies a load by name brings in is decided at run time. Whatever the name is, it resolves
        /// to an assembly lying next to the test assembly, so all of them count.
        /// </summary>
        private void Load(int testAssembly)
        {
            foreach (var assembly in _graph.Neighbors[testAssembly])
            {
                foreach (var type in _graph.TypesByAssembly[assembly])
                {
                    Discover(type);
                }
            }
        }

        private void ScanOnce(int assembly)
        {
            if (_scanned[assembly] != _walk)
            {
                _scanned[assembly] = _walk;
                Scan(assembly);
            }
        }

        private void Discover(int node)
        {
            // Test code often takes typeof(SomeTest).Assembly to read a resource. The test framework creates
            // test classes itself, so another test never reaches them through reflection.
            if (_graph.TestMethods[node] == 0)
            {
                Reach(node);
            }
        }

        private void Reach(int node)
        {
            if (_reached[node] == _walk)
            {
                return;
            }

            _reached[node] = _walk;
            _row[node >> 6] |= 1UL << (node & 63);
            _pending.Push(node);
        }

        private readonly IlDependencyGraph _graph;
        private readonly bool _discovers;
        private readonly bool _followsLoads;
        private readonly int[] _reached;
        private readonly int[] _scanned;
        private readonly Stack<int> _pending = new();
        private int _walk;
        private ulong[] _row = [];
    }

    private readonly IlDependencyGraph _graph;
    private readonly Dictionary<int, int> _rowByTestClass = [];
    private readonly Lazy<ulong[][]>[] _rows;
}
