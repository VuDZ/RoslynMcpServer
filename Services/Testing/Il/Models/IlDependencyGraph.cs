namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>
/// Top-level types of the read assemblies and the dependencies between them. Assemblies and types are
/// numbered; every array is indexed by those numbers. All files with one assembly name (the target
/// frameworks of a project) are merged into one node set: a dependency in any of them counts.
/// </summary>
internal sealed class IlDependencyGraph
{
    public required string[] Assemblies { get; init; }

    /// <summary>
    /// For an assembly of a test project, the assemblies lying next to it by the neighbor map, itself
    /// included: what a test process can load by name. For any other assembly, only itself.
    /// </summary>
    public required int[][] Neighbors { get; init; }

    public required int[][] TypesByAssembly { get; init; }

    public required IlTypeName[] Types { get; init; }

    public required IReadOnlyDictionary<IlTypeName, int> NodeByType { get; init; }

    public required int[] AssemblyOf { get; init; }

    public required int[][] Dependencies { get; init; }

    /// <summary>All base types and interfaces of a type that are in the graph, transitively.</summary>
    public required int[][] Supertypes { get; init; }

    public required int[][] Subtypes { get; init; }

    /// <summary>Assemblies a type's code hands to reflection.</summary>
    public required int[][] Scans { get; init; }

    /// <summary>How a type's code loads assemblies that nothing names in metadata.</summary>
    public required IlAssemblyLoadKinds[] Loads { get; init; }

    /// <summary>A framework can find the type without any code naming it.</summary>
    public required bool[] FrameworkFacing { get; init; }

    /// <summary>Test methods of a test class, inherited ones included; zero for any other type.</summary>
    public required int[] TestMethods { get; init; }

    public required int[] TestClasses { get; init; }

    /// <summary>Method bodies that could not be read. Their dependencies are missing from the graph.</summary>
    public required int UnreadableBodies { get; init; }
}
