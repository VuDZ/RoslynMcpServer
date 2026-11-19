namespace TestImpactIlProbe;

/// <summary>
/// Top-level types of the assemblies built from local sources and the dependencies between them. All target
/// frameworks of an assembly are merged into one node set: a dependency in any of them counts.
/// </summary>
internal sealed class DependencyGraph
{
    public required string[] Assemblies { get; init; }

    /// <summary>For each assembly, the assemblies it references, transitively, itself included.</summary>
    public required HashSet<int>[] AssemblyClosure { get; init; }

    /// <summary>
    /// For a test assembly, the assemblies that lie next to it in build output, itself included: what a test
    /// process can load by name. For any other assembly, only itself.
    /// </summary>
    public required HashSet<int>[] Deployed { get; init; }

    public required int[][] TypesByAssembly { get; init; }

    public required string[] Names { get; init; }

    public required int[] AssemblyOf { get; init; }

    public required int[][] Dependencies { get; init; }

    /// <summary>All base types and interfaces of a type that are in the graph, transitively.</summary>
    public required int[][] Supertypes { get; init; }

    public required int[][] Subtypes { get; init; }

    /// <summary>Assemblies a type's code hands to reflection.</summary>
    public required int[][] Scans { get; init; }

    /// <summary>How a type's code loads assemblies that nothing names in metadata.</summary>
    public required AssemblyLoadKinds[] Loads { get; init; }

    /// <summary>A framework can find the type without any code naming it.</summary>
    public required bool[] FrameworkFacing { get; init; }

    public required string[][] Documents { get; init; }

    /// <summary>Test methods of a test class, inherited ones included; zero for any other type.</summary>
    public required int[] TestMethods { get; init; }

    public required int[] TestClasses { get; init; }

    public required bool[] TestAssembly { get; init; }

    public required int OpaqueLoads { get; init; }

    public required int UnreadableBodies { get; init; }

    public int Edges => Dependencies.Sum(dependencies => dependencies.Length);
}
