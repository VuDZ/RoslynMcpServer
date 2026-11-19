namespace TestImpactIlProbe;

/// <summary>What the probe found on disk before building the graph.</summary>
internal sealed record InputCounts(
    int FilesSeen,
    int DistinctFiles,
    int ManagedAssemblies,
    int AssemblyFilesWithSources,
    int AssemblyNames,
    int MaxFlavors,
    int UnreadablePdbAssemblies);
