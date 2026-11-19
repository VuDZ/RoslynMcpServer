namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>
/// Everything the dependency graph needs from one assembly file. The value keeps no reference to the file or
/// to its image: a graph is built from a set of these values without reading any file again.
/// </summary>
internal sealed class IlAssemblyFacts
{
    /// <summary>The simple name of the assembly from its manifest, not the file name.</summary>
    public required string Name { get; init; }

    /// <summary>Method bodies that could not be read. Their dependencies are missing from the graph.</summary>
    public required int UnreadableBodies { get; init; }

    public required IReadOnlyList<IlTypeFacts> Types { get; init; }

    /// <summary>
    /// Types every test of the assembly depends on: types with a module initializer and types named in
    /// assembly-level attributes, such as an assembly fixture.
    /// </summary>
    public required IReadOnlyList<IlTypeName> Preamble { get; init; }

    /// <summary>Type full name to the simple name of the assembly the type was forwarded to.</summary>
    public required IReadOnlyDictionary<string, string> Forwarders { get; init; }
}
