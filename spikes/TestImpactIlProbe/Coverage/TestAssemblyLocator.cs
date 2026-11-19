namespace TestImpactIlProbe;

/// <summary>Finds the build output directory to run the tests of an assembly from.</summary>
internal static class TestAssemblyLocator
{
    /// <summary>
    /// A test assembly is copied into the output of every project that references it. Its own output is the
    /// directory with the fewest assemblies, and a runnable one has a runtime configuration next to it.
    /// </summary>
    /// <param name="pathFilter">A part of the path the directory must have, such as a target framework.</param>
    public static string? Find(
        IReadOnlyDictionary<string, HashSet<string>> directories,
        string assembly,
        string? pathFilter,
        Func<string, bool> fileExists)
    {
        var candidates = directories
            .Where(directory => directory.Value.Contains(assembly))
            .Where(directory => pathFilter is null || directory.Key.Contains(pathFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(directory => directory.Value.Count)
            .ThenByDescending(directory => directory.Key, StringComparer.Ordinal)
            .Select(directory => directory.Key)
            .ToList();
        return candidates.FirstOrDefault(directory => fileExists(Path.Combine(directory, assembly + ".runtimeconfig.json")))
            ?? candidates.FirstOrDefault();
    }
}
