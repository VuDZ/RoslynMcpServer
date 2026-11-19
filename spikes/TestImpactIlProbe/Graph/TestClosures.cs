namespace TestImpactIlProbe;

/// <summary>Types each test class depends on, as one bit row per test class.</summary>
internal static class TestClosures
{
    /// <summary>Rows are in the order of <see cref="DependencyGraph.TestClasses"/>.</summary>
    public static ulong[][] Compute(DependencyGraph graph, Layer layer)
    {
        var rows = new ulong[graph.TestClasses.Length][];
        Parallel.For(
            0,
            rows.Length,
            () => new Walker(graph, layer),
            (index, _, walker) =>
            {
                rows[index] = walker.Walk(graph.TestClasses[index], trace: null);
                return walker;
            },
            _ => { });
        return rows;
    }

    /// <summary>The path a walk from <paramref name="test"/> takes to <paramref name="target"/>, or null.</summary>
    public static List<ReachStep>? Explain(DependencyGraph graph, Layer layer, int test, int target)
    {
        var trace = new (int From, ReachReason Reason)[graph.Names.Length];
        var row = new Walker(graph, layer).Walk(test, trace);
        if ((row[target >> 6] & (1UL << (target & 63))) == 0)
        {
            return null;
        }

        var path = new List<ReachStep>();
        for (var node = target; node >= 0; node = trace[node].From)
        {
            path.Add(new ReachStep(node, trace[node].Reason));
        }

        path.Reverse();
        return path;
    }

    private sealed class Walker
    {
        public Walker(DependencyGraph graph, Layer layer)
        {
            _graph = graph;
            _discovers = layer is Layer.Discovery or Layer.DiscoveryWithLoads or Layer.DiscoveryWithNamedLoads;
            _wholeAssembly = layer == Layer.WholeAssembly;
            _followsLoads = layer is Layer.ReferencesWithLoads or Layer.DiscoveryWithLoads or Layer.DiscoveryWithNamedLoads;
            _narrowsLoadedAssemblies = layer == Layer.DiscoveryWithNamedLoads;
            _reached = new int[graph.Names.Length];
            _scanned = new int[graph.Assemblies.Length];
            _touched = new int[graph.Assemblies.Length];
        }

        public ulong[] Walk(int root, (int From, ReachReason Reason)[]? trace)
        {
            _walk++;
            _row = new ulong[(_graph.Names.Length + 63) / 64];
            _trace = trace;
            var loaded = false;
            Reach(root, -1, ReachReason.Root);
            while (_pending.TryPop(out var node))
            {
                foreach (var dependency in _graph.Dependencies[node])
                {
                    Reach(dependency, node, ReachReason.Reference);
                }

                var loads = _followsLoads ? _graph.Loads[node] : AssemblyLoadKinds.None;
                if (_narrowsLoadedAssemblies && loads.HasFlag(AssemblyLoadKinds.LoadedAssemblies))
                {
                    loads &= ~AssemblyLoadKinds.LoadedAssemblies;
                    ScanLoaded(node);
                }

                if (loads != AssemblyLoadKinds.None && !loaded)
                {
                    loaded = true;
                    Load(_graph.AssemblyOf[root], node);
                }

                if (!_discovers && !_wholeAssembly)
                {
                    continue;
                }

                foreach (var assembly in _graph.Scans[node])
                {
                    ScanOnce(assembly, node);
                }

                if (_discovers)
                {
                    // A container resolves a service by a type the consumer names, so an implementation counts
                    // only once something it implements is reached.
                    foreach (var subtype in _graph.Subtypes[node])
                    {
                        if (_scanned[_graph.AssemblyOf[subtype]] == _walk)
                        {
                            Discover(subtype, node, ReachReason.ImplementsReached);
                        }
                    }
                }
            }

            return _row;
        }

        private void Scan(int assembly, int from)
        {
            foreach (var type in _graph.TypesByAssembly[assembly])
            {
                if (_wholeAssembly)
                {
                    Discover(type, from, ReachReason.WholeAssembly);
                }
                else if (_graph.FrameworkFacing[type])
                {
                    Discover(type, from, ReachReason.FoundByFramework);
                }
                else if (_graph.Supertypes[type].Any(super => _reached[super] == _walk))
                {
                    Discover(type, from, ReachReason.ImplementsReached);
                }
            }
        }

        /// <summary>
        /// Which assemblies a load by name brings in is decided at run time. Whatever the name is, it resolves
        /// to an assembly lying next to the test assembly, so all of them count.
        /// </summary>
        private void Load(int testAssembly, int from)
        {
            foreach (var assembly in _graph.Deployed[testAssembly])
            {
                foreach (var type in _graph.TypesByAssembly[assembly])
                {
                    Discover(type, from, ReachReason.LoadedByName);
                }
            }
        }

        /// <summary>
        /// Code that enumerates the loaded assemblies sees every assembly the test has brought in, now or
        /// later in the run. Each assembly with a reached type is scanned as if it were handed to reflection.
        /// </summary>
        private void ScanLoaded(int from)
        {
            if (_scansLoaded == _walk)
            {
                return;
            }

            _scansLoaded = _walk;
            for (var assembly = 0; assembly < _touched.Length; assembly++)
            {
                if (_touched[assembly] == _walk)
                {
                    ScanOnce(assembly, from);
                }
            }
        }

        private void ScanOnce(int assembly, int from)
        {
            if (_scanned[assembly] != _walk)
            {
                _scanned[assembly] = _walk;
                Scan(assembly, from);
            }
        }

        private void Discover(int node, int from, ReachReason reason)
        {
            // Test code often takes typeof(SomeTest).Assembly to read a resource. The test framework creates
            // test classes itself, so another test never reaches them through reflection.
            if (_graph.TestMethods[node] == 0)
            {
                Reach(node, from, reason);
            }
        }

        private void Reach(int node, int from, ReachReason reason)
        {
            if (_reached[node] == _walk)
            {
                return;
            }

            _reached[node] = _walk;
            _row[node >> 6] |= 1UL << (node & 63);
            _pending.Push(node);
            if (_trace is not null)
            {
                _trace[node] = (from, reason);
            }

            var assembly = _graph.AssemblyOf[node];
            _touched[assembly] = _walk;
            if (_scansLoaded == _walk)
            {
                ScanOnce(assembly, node);
            }
        }

        private readonly DependencyGraph _graph;
        private readonly bool _discovers;
        private readonly bool _wholeAssembly;
        private readonly bool _followsLoads;
        private readonly bool _narrowsLoadedAssemblies;
        private readonly int[] _reached;
        private readonly int[] _scanned;
        private readonly int[] _touched;
        private readonly Stack<int> _pending = new();
        private int _walk;
        private int _scansLoaded;
        private ulong[] _row = [];
        private (int From, ReachReason Reason)[]? _trace;
    }
}
