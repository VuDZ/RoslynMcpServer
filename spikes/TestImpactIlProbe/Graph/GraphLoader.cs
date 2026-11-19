using System.Diagnostics;

namespace TestImpactIlProbe;

/// <summary>Finds built assemblies under directories, reads those built from local sources and builds the graph.</summary>
internal static class GraphLoader
{
    public static LoadedGraph Load(IReadOnlyList<string> binDirectories, IReadOnlyList<string> sourceRoots)
    {
        var phases = new List<Phase>();
        var paths = new DocumentPaths(sourceRoots);
        var watch = Stopwatch.StartNew();
        var files = AssemblyFiles.Find(binDirectories);
        Record(phases, "find-files", watch);

        var identities = files.Distinct
            .AsParallel()
            .Select(file => AssemblyReader.Identify(file, paths))
            .Where(identity => identity is not null)
            .Select(identity => identity!)
            .ToList();
        Record(phases, "identify", watch);

        // The same build of an assembly lies in many output directories; another target framework or
        // configuration of it has another MVID and is read as well.
        var withSources = identities
            .Where(identity => identity.Origin == AssemblyOrigin.Sources)
            .GroupBy(identity => identity.Mvid)
            .Select(group => group.OrderBy(identity => identity.Path, StringComparer.Ordinal).First())
            .ToList();
        var facts = withSources
            .AsParallel()
            .Select(identity => AssemblyReader.Read(identity.Path, paths))
            .ToList();
        Record(phases, "read", watch);

        var graph = DependencyGraphBuilder.Build(facts, [.. files.Directories.Values]);
        facts.Clear();
        Record(phases, "graph", watch);

        var known = withSources.Select(identity => identity.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unreadable = identities
            .Where(identity => identity.Origin == AssemblyOrigin.UnreadablePdb && !known.Contains(identity.Name))
            .Select(identity => identity.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var flavors = withSources.GroupBy(identity => identity.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var input = new InputCounts(
            files.Seen,
            files.Distinct.Count,
            identities.Count,
            withSources.Count,
            flavors.Count,
            flavors.Count == 0 ? 0 : flavors.Max(group => group.Count()),
            unreadable.Count);
        return new LoadedGraph(graph, input, phases, unreadable, files.Directories);
    }

    public static void Record(List<Phase> phases, string name, Stopwatch watch)
    {
        var elapsed = watch.ElapsedMilliseconds;
        phases.Add(new Phase(name, elapsed, MemorySnapshot.Capture()));
        Console.Error.WriteLine($"{name}: {elapsed} ms");
        watch.Restart();
    }
}
