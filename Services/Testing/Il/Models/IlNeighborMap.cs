namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>
/// For every test project, the solution assemblies a copy of which lies in the directory of its output
/// assembly: what its test process can load by name. Assemblies are named by the output path of their own
/// project; the copies are never read.
/// </summary>
internal sealed class IlNeighborMap
{
    public static IlNeighborMap Empty { get; } = new(new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>
    /// Output path of a test project to the output paths of the solution assemblies next to it, itself
    /// excluded. A test project without neighbors has an empty list.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> NeighborsByTestOutputPath { get; }

    public IlNeighborMap(IReadOnlyDictionary<string, IReadOnlyList<string>> neighborsByTestOutputPath)
    {
        NeighborsByTestOutputPath = neighborsByTestOutputPath;
    }
}
