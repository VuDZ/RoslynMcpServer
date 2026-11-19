namespace TestImpactIlProbe;

/// <summary>A graph together with the record of how it was obtained.</summary>
/// <param name="Directories">For every build output directory, the names of the assembly files in it.</param>
internal sealed record LoadedGraph(
    DependencyGraph Graph,
    InputCounts Input,
    List<Phase> Phases,
    List<string> UnreadablePdbs,
    IReadOnlyDictionary<string, HashSet<string>> Directories);
