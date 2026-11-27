using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>Finds, for every test project, the solution assemblies lying next to its output assembly.</summary>
internal static class IlNeighborMapBuilder
{
    /// <summary>
    /// Lists the files of the output directory of every test project once, without descending into
    /// subdirectories, and keeps the solution assemblies whose file name is there.
    /// </summary>
    /// <remarks>
    /// A build copies what a test project references into its output directory, and a test process resolves
    /// an assembly name against that directory. Only that directory counts: the output directory of the
    /// referenced project is a different one, and a directory of another project that holds a copy of the
    /// test assembly is not where the test process starts. Files that are not output assemblies of the
    /// given projects (packages, the framework) are left out. The copies are not opened.
    /// </remarks>
    public static IlNeighborMap Build(IReadOnlyList<IlAssemblyInput> assemblies)
    {
        // Several projects can share a file name: the target frameworks of one project.
        var byFileName = assemblies.ToLookup(
            assembly => Path.GetFileName(assembly.OutputFilePath),
            assembly => assembly.OutputFilePath,
            _fileNames);

        // Test projects that build into one common directory share its listing.
        var listings = new Dictionary<string, string[]>(IlPaths.Comparer);
        var neighbors = new Dictionary<string, IReadOnlyList<string>>(IlPaths.Comparer);
        foreach (var test in assemblies.Where(assembly => assembly.IsTestProject))
        {
            var directory = Path.GetDirectoryName(test.OutputFilePath) ?? string.Empty;
            if (!listings.TryGetValue(directory, out var solutionFiles))
            {
                listings[directory] = solutionFiles = SolutionAssembliesIn(directory, byFileName);
            }

            neighbors[test.OutputFilePath] = [.. solutionFiles.Where(path => !IlPaths.Comparer.Equals(path, test.OutputFilePath))];
        }

        return new IlNeighborMap(neighbors);
    }

    /// <summary>Output paths of the given projects whose file name is in the directory, in a stable order.</summary>
    private static string[] SolutionAssembliesIn(string directory, ILookup<string, string> byFileName)
    {
        if (directory.Length == 0)
        {
            return [];
        }

        try
        {
            return
            [
                .. Directory.EnumerateFiles(directory)
                    .SelectMany(file => byFileName[Path.GetFileName(file)])
                    .Distinct(IlPaths.Comparer)
                    .Order(StringComparer.Ordinal),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A test project that is not built yet has no output directory and no neighbors.
            return [];
        }
    }

    // A build keeps the case of a file name, but a case-insensitive file system does not require it. Matching
    // without case can only add a neighbor, which widens the answer and never loses a test.
    private static readonly StringComparer _fileNames = StringComparer.OrdinalIgnoreCase;
}
