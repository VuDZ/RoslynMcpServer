namespace TestImpactIlProbe;

/// <summary>Assembly files found in build output.</summary>
/// <param name="Distinct">One path per distinct file.</param>
/// <param name="Seen">The number of files before identical copies were dropped.</param>
/// <param name="Directories">For every directory with assemblies, the names of the files in it without the extension.</param>
internal sealed record AssemblyFileSet(List<string> Distinct, int Seen, IReadOnlyDictionary<string, HashSet<string>> Directories);
