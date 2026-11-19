using System.IO.Enumeration;

namespace TestImpactIlProbe;

/// <summary>Finds assembly files under build output directories.</summary>
internal static class AssemblyFiles
{
    /// <summary>
    /// Returns one path per distinct file. A build copies every dependency into every output directory, so
    /// a large solution holds tens of thousands of identical files; a copy keeps name, size and write time.
    /// The copies are still remembered by directory: they tell which assemblies a process started there can load.
    /// </summary>
    public static AssemblyFileSet Find(IEnumerable<string> directories)
    {
        var distinct = new Dictionary<(string Name, long Length, DateTimeOffset Written), string>();
        var byDirectory = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var seen = 0;
        foreach (var directory in directories)
        {
            var files = new FileSystemEnumerable<(string Path, string Name, long Length, DateTimeOffset Written)>(
                directory,
                (ref FileSystemEntry entry) => (entry.ToFullPath(), entry.FileName.ToString(), entry.Length, entry.LastWriteTimeUtc),
                new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                    !entry.IsDirectory
                    && entry.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && !entry.FileName.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase),
                ShouldRecursePredicate = (ref FileSystemEntry entry) => !IsSkipped(entry.FileName),
            };
            foreach (var file in files)
            {
                seen++;
                distinct.TryAdd((file.Name.ToLowerInvariant(), file.Length, file.Written), file.Path);
                var directoryPath = Path.GetDirectoryName(file.Path) ?? string.Empty;
                if (!byDirectory.TryGetValue(directoryPath, out var names))
                {
                    byDirectory[directoryPath] = names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                names.Add(Path.GetFileNameWithoutExtension(file.Name));
            }
        }

        return new AssemblyFileSet([.. distinct.Values.Order(StringComparer.Ordinal)], seen, byDirectory);
    }

    // obj holds the intermediate copy of an assembly that bin already has, possibly from an older build.
    private static bool IsSkipped(ReadOnlySpan<char> directory) =>
        directory is "obj" or ".git" or ".vs" or "node_modules";
}
