using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>Merges the facts of all read assemblies into one <see cref="IlDependencyGraph"/>.</summary>
internal sealed class IlDependencyGraphBuilder
{
    private IlDependencyGraphBuilder(IReadOnlyList<IlAssemblyFacts> assemblies)
    {
        _assemblyNames = [.. assemblies.Select(assembly => assembly.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        for (var index = 0; index < _assemblyNames.Length; index++)
        {
            _assemblyIndex[_assemblyNames[index]] = index;
        }

        foreach (var assembly in assemblies)
        {
            foreach (var forwarder in assembly.Forwarders)
            {
                _forwarders[new IlTypeName(assembly.Name, forwarder.Key)] = forwarder.Value;
            }

            foreach (var type in assembly.Types)
            {
                if (!_nodeIndex.TryGetValue(type.Name, out var node))
                {
                    node = _nodes.Count;
                    _nodeIndex[type.Name] = node;
                    _nodes.Add([]);
                }

                _nodes[node].Add(type);
            }
        }
    }

    /// <summary>
    /// Builds the graph from values that are already read; no file is opened. A failed read is returned in
    /// <see cref="IlGraphBuildResult.Unread"/> and the graph is built from the rest.
    /// </summary>
    /// <param name="reads">
    /// One value per output assembly of a loaded project. Several values with one assembly name (the target
    /// frameworks of a project) are merged.
    /// </param>
    /// <param name="neighbors">Names assemblies by the paths of <paramref name="reads"/>.</param>
    public static IlGraphBuildResult Build(IReadOnlyList<IlAssemblyReadResult> reads, IlNeighborMap neighbors)
    {
        IlAssemblyFacts[] facts = [.. reads.Where(read => read.Facts is not null).Select(read => read.Facts!)];
        var builder = new IlDependencyGraphBuilder(facts);
        var graph = builder.Merge(facts, builder.NeighborAssemblies(reads, neighbors));
        IlUnreadAssembly[] unread = [.. reads.Where(read => read.Failure is not null).Select(read => read.Failure!)];
        return new IlGraphBuildResult(graph, new IlTestClosures(graph), unread);
    }

    /// <summary>Reads the output assembly of every project and builds the graph.</summary>
    public static IlGraphBuildResult BuildFromFiles(
        IReadOnlyList<IlAssemblyInput> assemblies,
        IlNeighborMap neighbors,
        CancellationToken cancellationToken = default)
    {
        // Results are stored by position: the order of the inputs decides the numbering of the nodes, and
        // it must not depend on which read ends first.
        var reads = new IlAssemblyReadResult[assemblies.Count];
        Parallel.For(
            0,
            reads.Length,
            new ParallelOptions { CancellationToken = cancellationToken },
            index => reads[index] = IlAssemblyReader.ReadFile(assemblies[index].OutputFilePath));
        return Build(reads, neighbors);
    }

    private IlDependencyGraph Merge(IReadOnlyList<IlAssemblyFacts> assemblies, int[][] neighbors)
    {
        var count = _nodes.Count;
        var assemblyOf = _nodes.Select(flavors => _assemblyIndex[flavors[0].Name.Assembly]).ToArray();
        var direct = _nodes.Select(flavors => Resolve(flavors.SelectMany(type => type.Supertypes))).ToArray();
        var supertypes = Enumerable.Range(0, count).Select(node => Transitive(node, direct)).ToArray();
        var ownTests = _nodes.Select(OwnTestMethods).ToArray();
        var testMethods = new int[count];
        for (var node = 0; node < count; node++)
        {
            var name = _nodes[node][0].Name.Name;
            // A runnable target framework must survive merging with an abstract variant.
            var runnable = _nodes[node].Any(type => !type.IsAbstract) && !name.Contains('`');
            testMethods[node] = runnable ? ownTests[node] + supertypes[node].Sum(super => ownTests[super]) : 0;
        }

        var dependencies = _nodes.Select((flavors, node) => Resolve(flavors.SelectMany(type => type.Dependencies), node)).ToArray();
        AddPreambles(assemblies, dependencies, assemblyOf, testMethods);
        IlTypeName[] types = [.. _nodes.Select(flavors => flavors[0].Name)];
        return new IlDependencyGraph
        {
            Assemblies = _assemblyNames,
            Neighbors = neighbors,
            TypesByAssembly = Group(assemblyOf, _assemblyNames.Length),
            Types = types,
            NodeByType = _nodeIndex,
            AssemblyOf = assemblyOf,
            Dependencies = dependencies,
            Supertypes = supertypes,
            Subtypes = Invert(supertypes),
            Scans = [.. _nodes.Select(flavors => flavors
                .SelectMany(type => type.Scans)
                .Where(_assemblyIndex.ContainsKey)
                .Select(name => _assemblyIndex[name])
                .Distinct()
                .Order()
                .ToArray())],
            Loads = [.. _nodes.Select(flavors => flavors.Aggregate(IlAssemblyLoadKinds.None, (kinds, type) => kinds | type.Loads))],
            FrameworkFacing = FrameworkFacing(supertypes),
            TestMethods = testMethods,
            TestClasses = [.. Enumerable.Range(0, count).Where(node => testMethods[node] > 0)],
            UnreadableBodies = assemblies.Sum(assembly => assembly.UnreadableBodies),
        };
    }

    private int Resolve(IlTypeName name)
    {
        for (var hop = 0; hop < ForwarderHops; hop++)
        {
            if (_nodeIndex.TryGetValue(name, out var node))
            {
                return node;
            }

            if (!_forwarders.TryGetValue(name, out var target))
            {
                break;
            }

            name = name with { Assembly = target };
        }

        return -1;
    }

    private int[] Resolve(IEnumerable<IlTypeName> names, int self = -1) =>
        [.. names.Select(Resolve).Where(node => node >= 0 && node != self).Distinct().Order()];

    private static int[] Transitive(int start, int[][] direct)
    {
        var seen = new HashSet<int> { start };
        var pending = new Stack<int>(direct[start]);
        while (pending.TryPop(out var node))
        {
            if (seen.Add(node))
            {
                foreach (var next in direct[node])
                {
                    pending.Push(next);
                }
            }
        }

        seen.Remove(start);
        return [.. seen.Order()];
    }

    private static int[][] Invert(int[][] edges)
    {
        var inverted = edges.Select(_ => new List<int>()).ToArray();
        for (var node = 0; node < edges.Length; node++)
        {
            foreach (var target in edges[node])
            {
                inverted[target].Add(node);
            }
        }

        return [.. inverted.Select(list => list.ToArray())];
    }

    private static int[][] Group(int[] assemblyOf, int assemblies)
    {
        var groups = Enumerable.Range(0, assemblies).Select(_ => new List<int>()).ToArray();
        for (var node = 0; node < assemblyOf.Length; node++)
        {
            groups[assemblyOf[node]].Add(node);
        }

        return [.. groups.Select(list => list.ToArray())];
    }

    private int OwnTestMethods(List<IlTypeFacts> flavors) =>
        flavors
            .SelectMany(type => type.MethodAttributes)
            .Where(attribute => IsTestAttribute(attribute.Attribute, depth: 0))
            .Select(attribute => attribute.Method)
            .Distinct(StringComparer.Ordinal)
            .Count();

    /// <summary>
    /// A test attribute is recognized by name: xUnit, NUnit and MSTest attributes and attributes derived from
    /// them in the solution. Frameworks are not loaded, so an attribute from a package that is derived from
    /// one of these under another name is not recognized.
    /// </summary>
    private bool IsTestAttribute(IlTypeName attribute, int depth)
    {
        if (_testAttributes.TryGetValue(attribute, out var known))
        {
            return known;
        }

        var simple = attribute.Name[(attribute.Name.LastIndexOf('.') + 1)..];
        simple = simple.EndsWith("Attribute", StringComparison.Ordinal) ? simple[..^"Attribute".Length] : simple;
        var result = _testAttributeSuffixes.Any(suffix => simple.EndsWith(suffix, StringComparison.Ordinal));
        if (!result && depth < ForwarderHops && _nodeIndex.TryGetValue(attribute, out var node))
        {
            result = _nodes[node].SelectMany(type => type.Supertypes).Any(super => IsTestAttribute(super, depth + 1));
        }

        _testAttributes[attribute] = result;
        return result;
    }

    private bool[] FrameworkFacing(int[][] supertypes)
    {
        var count = _nodes.Count;
        bool External(IlTypeName name) => Resolve(name) < 0 && !IsCore(name.Name);
        var bySupertype = _nodes.Select(flavors => flavors.SelectMany(type => type.Supertypes).Any(External)).ToArray();
        var inherited = Enumerable.Range(0, count)
            .Select(node => bySupertype[node] || supertypes[node].Any(super => bySupertype[super]))
            .ToArray();

        // [Export] and [ApiController] mark a type for a framework. So does a solution attribute derived from
        // such an attribute, which is how a large codebase usually wraps them.
        bool Marks(IlTypeName attribute) => Resolve(attribute) is var node && node >= 0 ? inherited[node] : !IsCore(attribute.Name);
        var byAttribute = _nodes.Select(flavors => flavors.SelectMany(type => type.TypeAttributes).Any(Marks)).ToArray();
        return [.. Enumerable.Range(0, count)
            .Select(node => inherited[node] || byAttribute[node] || supertypes[node].Any(super => byAttribute[super]))];
    }

    /// <summary>
    /// Base library namespaces: implementing IDisposable or carrying [Serializable] does not make a type
    /// discoverable. MEF, ASP.NET, WCF and UI frameworks live under System too and are not core.
    /// </summary>
    private static bool IsCore(string typeName)
    {
        if (typeName != "System" && !typeName.StartsWith("System.", StringComparison.Ordinal))
        {
            return false;
        }

        return !_discoveringNamespaces.Any(prefix => typeName.StartsWith(prefix, StringComparison.Ordinal));
    }

    private void AddPreambles(IReadOnlyList<IlAssemblyFacts> assemblies, int[][] dependencies, int[] assemblyOf, int[] testMethods)
    {
        var preambles = new Dictionary<int, HashSet<int>>();
        HashSet<int> Of(string assembly) =>
            preambles.TryGetValue(_assemblyIndex[assembly], out var set) ? set : preambles[_assemblyIndex[assembly]] = [];
        foreach (var assembly in assemblies)
        {
            Of(assembly.Name).UnionWith(assembly.Preamble.Select(Resolve).Where(node => node >= 0));
            foreach (var type in assembly.Types)
            {
                if (type.TypeAttributes.Any(attribute => attribute.Name == "NUnit.Framework.SetUpFixtureAttribute")
                    || type.MethodAttributes.Any(attribute => _assemblySetupAttributes.Contains(attribute.Attribute.Name)))
                {
                    Of(assembly.Name).Add(_nodeIndex[type.Name]);
                }
            }
        }

        for (var node = 0; node < dependencies.Length; node++)
        {
            if (testMethods[node] > 0 && preambles.TryGetValue(assemblyOf[node], out var preamble) && preamble.Count > 0)
            {
                dependencies[node] = [.. dependencies[node].Union(preamble).Where(other => other != node).Order()];
            }
        }
    }

    /// <summary>
    /// A test process resolves an assembly name against the directory it runs from, and a build copies there
    /// everything the test project references, whether or not the compiler kept the reference in metadata.
    /// The neighbor map says which assemblies those are; it names them by the paths of their own output
    /// files, and an assembly that was not read has no types to add.
    /// </summary>
    private int[][] NeighborAssemblies(IReadOnlyList<IlAssemblyReadResult> reads, IlNeighborMap neighbors)
    {
        var nameByPath = new Dictionary<string, string>(IlPaths.Comparer);
        foreach (var read in reads)
        {
            if (read.Facts is not null)
            {
                nameByPath[read.Path] = read.Facts.Name;
            }
        }

        var sets = _assemblyNames.Select((_, index) => new SortedSet<int> { index }).ToArray();
        foreach (var (testPath, neighborPaths) in neighbors.NeighborsByTestOutputPath)
        {
            if (!nameByPath.TryGetValue(testPath, out var test))
            {
                continue;
            }

            foreach (var path in neighborPaths)
            {
                if (nameByPath.TryGetValue(path, out var neighbor))
                {
                    sets[_assemblyIndex[test]].Add(_assemblyIndex[neighbor]);
                }
            }
        }

        return [.. sets.Select(set => set.ToArray())];
    }

    private const int ForwarderHops = 4;

    private static readonly string[] _testAttributeSuffixes =
        ["Fact", "Theory", "Test", "TestCase", "TestCaseSource", "TestMethod"];

    private static readonly string[] _assemblySetupAttributes =
    [
        "Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyInitializeAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyCleanupAttribute",
    ];

    private static readonly string[] _discoveringNamespaces =
    [
        "System.Composition.", "System.ComponentModel.Composition.", "System.Web.", "System.ServiceModel.",
        "System.Windows.", "System.CommandLine.", "System.Management.Automation.", "System.Activities.",
    ];

    private readonly string[] _assemblyNames;
    private readonly Dictionary<string, int> _assemblyIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<IlTypeName, int> _nodeIndex = [];
    private readonly Dictionary<IlTypeName, string> _forwarders = [];
    private readonly Dictionary<IlTypeName, bool> _testAttributes = [];
    private readonly List<List<IlTypeFacts>> _nodes = [];
}
