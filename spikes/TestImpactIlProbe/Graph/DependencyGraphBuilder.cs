namespace TestImpactIlProbe;

/// <summary>Merges the facts of all read assemblies into one <see cref="DependencyGraph"/>.</summary>
internal sealed class DependencyGraphBuilder
{
    private DependencyGraphBuilder(IReadOnlyList<AssemblyFacts> assemblies)
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
                _forwarders[new TypeName(assembly.Name, forwarder.Key)] = forwarder.Value;
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

    /// <param name="directories">For every build output directory, the names of the assembly files in it.</param>
    public static DependencyGraph Build(
        IReadOnlyList<AssemblyFacts> assemblies,
        IReadOnlyCollection<HashSet<string>> directories)
    {
        var builder = new DependencyGraphBuilder(assemblies);
        return builder.Merge(assemblies, directories);
    }

    private DependencyGraph Merge(IReadOnlyList<AssemblyFacts> assemblies, IReadOnlyCollection<HashSet<string>> directories)
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
            var runnable = !_nodes[node].Any(type => type.IsAbstract) && !name.Contains('`');
            testMethods[node] = runnable ? ownTests[node] + supertypes[node].Sum(super => ownTests[super]) : 0;
        }

        var dependencies = _nodes.Select((flavors, node) => Resolve(flavors.SelectMany(type => type.Dependencies), node)).ToArray();
        AddPreambles(assemblies, dependencies, assemblyOf, testMethods);
        var testAssembly = new bool[_assemblyNames.Length];
        foreach (var node in Enumerable.Range(0, count).Where(node => testMethods[node] > 0))
        {
            testAssembly[assemblyOf[node]] = true;
        }

        return new DependencyGraph
        {
            Assemblies = _assemblyNames,
            AssemblyClosure = AssemblyClosure(assemblies),
            Deployed = Deployed(directories, testAssembly),
            TypesByAssembly = Group(assemblyOf, _assemblyNames.Length),
            Names = [.. _nodes.Select(flavors => flavors[0].Name.Name)],
            AssemblyOf = assemblyOf,
            Dependencies = dependencies,
            Supertypes = supertypes,
            Subtypes = Invert(supertypes),
            Scans = [.. _nodes.Select(flavors => flavors
                .SelectMany(type => type.Scans)
                .Where(_assemblyIndex.ContainsKey)
                .Select(name => _assemblyIndex[name])
                .Distinct()
                .ToArray())],
            Loads = [.. _nodes.Select(flavors => flavors.Aggregate(AssemblyLoadKinds.None, (kinds, type) => kinds | type.Loads))],
            FrameworkFacing = FrameworkFacing(direct, supertypes),
            Documents = [.. _nodes.Select(flavors => flavors
                .SelectMany(type => type.Documents)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray())],
            TestMethods = testMethods,
            TestClasses = [.. Enumerable.Range(0, count).Where(node => testMethods[node] > 0)],
            TestAssembly = testAssembly,
            OpaqueLoads = _nodes.Sum(flavors => flavors.Max(type => type.OpaqueLoads)),
            UnreadableBodies = assemblies.Sum(assembly => assembly.UnreadableBodies),
        };
    }

    private int Resolve(TypeName name)
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

    private int[] Resolve(IEnumerable<TypeName> names, int self = -1) =>
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

    private int OwnTestMethods(List<TypeFacts> flavors) =>
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
    private bool IsTestAttribute(TypeName attribute, int depth)
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

    private bool[] FrameworkFacing(int[][] direct, int[][] supertypes)
    {
        var count = _nodes.Count;
        bool External(TypeName name) => Resolve(name) < 0 && !IsCore(name.Name);
        var bySupertype = _nodes.Select(flavors => flavors.SelectMany(type => type.Supertypes).Any(External)).ToArray();
        var inherited = Enumerable.Range(0, count)
            .Select(node => bySupertype[node] || supertypes[node].Any(super => bySupertype[super]))
            .ToArray();

        // [Export] and [ApiController] mark a type for a framework. So does a solution attribute derived from
        // such an attribute, which is how a large codebase usually wraps them.
        bool Marks(TypeName attribute) => Resolve(attribute) is var node && node >= 0 ? inherited[node] : !IsCore(attribute.Name);
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

    private void AddPreambles(IReadOnlyList<AssemblyFacts> assemblies, int[][] dependencies, int[] assemblyOf, int[] testMethods)
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

    private HashSet<int>[] AssemblyClosure(IReadOnlyList<AssemblyFacts> assemblies)
    {
        var direct = _assemblyNames.Select(_ => new HashSet<int>()).ToArray();
        foreach (var assembly in assemblies)
        {
            foreach (var reference in assembly.References.Where(_assemblyIndex.ContainsKey))
            {
                direct[_assemblyIndex[assembly.Name]].Add(_assemblyIndex[reference]);
            }
        }

        var asArrays = direct.Select(set => set.ToArray()).ToArray();
        return [.. Enumerable.Range(0, asArrays.Length).Select(index => new HashSet<int>(Transitive(index, asArrays)) { index })];
    }

    /// <summary>
    /// A test process resolves an assembly name against the directory it runs from, and a build copies there
    /// everything the test project references, whether or not the compiler kept the reference in metadata.
    /// A test assembly that another project references is copied into that project's output as well; its
    /// neighbors there are counted too, which errs toward selecting more tests.
    /// </summary>
    private HashSet<int>[] Deployed(IReadOnlyCollection<HashSet<string>> directories, bool[] testAssembly)
    {
        var deployed = _assemblyNames.Select((_, index) => new HashSet<int> { index }).ToArray();
        foreach (var directory in directories)
        {
            var present = directory.Where(_assemblyIndex.ContainsKey).Select(name => _assemblyIndex[name]).ToArray();
            foreach (var assembly in present.Where(assembly => testAssembly[assembly]))
            {
                deployed[assembly].UnionWith(present);
            }
        }

        return deployed;
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
    private readonly Dictionary<TypeName, int> _nodeIndex = [];
    private readonly Dictionary<TypeName, string> _forwarders = [];
    private readonly Dictionary<TypeName, bool> _testAttributes = [];
    private readonly List<List<TypeFacts>> _nodes = [];
}
