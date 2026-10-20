using System.Text.Json;
using System.Xml.Linq;
using ParallelLoad.Models;

namespace ParallelLoad;

internal static class CoreFirstPartition
{
    public static int Export(string[] args)
    {
        var options = RunOptions.Parse(["core-first", .. args.Skip(1)]);
        var start = TimeProvider.System.GetTimestamp();
        var plan = Create(options);
        var planningMs = TimeProvider.System.GetElapsedTime(start).TotalMilliseconds;
        object result = options.RefusalPolicy
            ? new { Plan = plan, PlannerDecision = RefusalPolicy.Decide(options, plan, planningMs), PlanningMs = planningMs }
            : plan;
        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
        File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"core-plan {options.Partition} N={options.Workers} core={plan.Core.Length} balance={plan.BalanceRatio:R} status={plan.PlanningStatus}");
        return plan.PlanningStatus == "ok" ? 0 : 2;
    }

    public static CoreFirstPlan Create(RunOptions options, string[]? subset = null)
    {
        var directory = Path.GetDirectoryName(options.SolutionPath)!;
        var graph = options.Graph is "snapshot" or "snapshot-load"
            ? ReadSnapshot(options.SnapshotPath!, directory)
            : ReadXml(options.SolutionPath);
        if (options.Graph == "snapshot-load")
        {
            AddNonCompilationEdges(graph);
        }
        if (options.Graph == "xml" && options.SnapshotPath is not null)
        {
            // The same-series baseline establishes loadability; XML remains the sole edge source in this mode.
            var loadable = ReadSnapshot(options.SnapshotPath, directory).Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            graph = graph.Where(pair => loadable.Contains(pair.Key)).ToDictionary(pair => pair.Key,
                pair => pair.Value.Where(loadable.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        }

        var canonicalPaths = XDocument.Load(options.SolutionPath).Descendants("Project")
            .Select(element => Path.GetFullPath(element.Attribute("Path")!.Value, directory))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(path => path, StringComparer.OrdinalIgnoreCase);
        string Canonical(string path) => canonicalPaths.GetValueOrDefault(path, path);
        // Snapshot keys uppercase Windows paths. Reuse the solution's request spelling to avoid changing inferred project names.
        graph = graph.ToDictionary(pair => Canonical(pair.Key),
            pair => pair.Value.Select(Canonical).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        if (subset is not null)
        {
            var selected = subset.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (selected.Any(path => !graph.ContainsKey(path)) || selected.Any(path => graph[path].Any(edge => !selected.Contains(edge))))
            {
                throw new InvalidDataException("A recursive core must be a closed subset of the original graph.");
            }

            graph = graph.Where(pair => selected.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        }
        var costs = graph.Keys.ToDictionary(path => path, _ => 1d, StringComparer.OrdinalIgnoreCase);
        var costSource = "project-count";
        if (options.ProfilePath is not null)
        {
            var profile = JsonSerializer.Deserialize<CostProfile>(File.ReadAllText(options.ProfilePath))!;
            if (profile.Status != "ok")
            {
                throw new InvalidDataException("The cost profile is unsuccessful.");
            }

            var profileCosts = profile.Projects.GroupBy(project => ResolvePath(project.FilePath, directory), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Sum(project => project.WeightMs ?? double.NaN), StringComparer.OrdinalIgnoreCase);
            if (costs.Keys.Any(path => !profileCosts.TryGetValue(path, out var cost) || !double.IsFinite(cost) || cost < 0))
            {
                throw new InvalidDataException("The cost profile does not cover the loadable graph with finite costs.");
            }

            costs = costs.Keys.ToDictionary(path => path, path => profileCosts[path], StringComparer.OrdinalIgnoreCase);
            costSource = "task-05.1-weight-ms";
        }

        return options.FastPlanner
            ? FastCoreFirstPlanner.Create(graph, costs, options.Workers, options.Graph, costSource, options.Partition)
            : Create(graph, costs, options.Workers, options.Graph, costSource, options.Partition, options.BalanceEpsilon);
    }

    public static CoreFirstPlan Create(
        Dictionary<string, HashSet<string>> graph,
        Dictionary<string, double> costs,
        int workers,
        string graphSource = "synthetic",
        string costSource = "project-count",
        string partition = "greedy",
        double epsilon = 0.15)
    {
        if (workers < 1 || graph.Count == 0 || partition is not ("greedy" or "cluster") ||
            !double.IsFinite(epsilon) || epsilon < 0 || graph.Values.SelectMany(edges => edges).Any(path => !graph.ContainsKey(path)) ||
            graph.Keys.Any(path => !costs.TryGetValue(path, out var cost) || !double.IsFinite(cost) || cost < 0))
        {
            throw new InvalidDataException("Planning requires a closed nonempty graph, finite costs, and positive workers.");
        }

        var referenced = graph.Values.SelectMany(edges => edges).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roots = graph.Keys.Where(path => !referenced.Contains(path)).Order(StringComparer.Ordinal).ToList();
        var covered = Closure(roots, graph);
        foreach (var path in graph.Keys.Order(StringComparer.Ordinal))
        {
            // Rootless cycles need an explicit request to preserve complete coverage.
            if (!covered.Contains(path))
            {
                roots.Add(path);
                covered.UnionWith(Closure([path], graph));
            }
        }

        var requests = Enumerable.Range(0, workers).Select(_ => new List<string>()).ToArray();
        var closures = Enumerable.Range(0, workers).Select(_ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)).ToArray();
        foreach (var root in roots.OrderByDescending(root => Closure([root], graph).Sum(path => costs[path])).ThenBy(path => path, StringComparer.Ordinal))
        {
            var closure = Closure([root], graph);
            var group = Enumerable.Range(0, workers).MinBy(index => closures[index].Union(closure).Sum(path => costs[path]));
            requests[group].Add(root);
            closures[group].UnionWith(closure);
        }

        var requestsArray = requests.Select(paths => paths.ToArray()).ToArray();
        return partition == "cluster"
            ? CoreFirstCluster.Create(graph, costs, requestsArray, graphSource, costSource, epsilon)
            : BuildPlan(graph, costs, requestsArray, graphSource, costSource, epsilon);
    }

    internal static CoreFirstPlan BuildPlan(
        Dictionary<string, HashSet<string>> graph,
        Dictionary<string, double> costs,
        string[][] requests,
        string graphSource,
        string costSource,
        double epsilon,
        Dictionary<string, HashSet<string>>? rootClosures = null)
    {
        var workers = requests.Length;
        var closures = requests.Select(paths => rootClosures is null ? Closure(paths, graph) :
            paths.SelectMany(path => rootClosures[path]).ToHashSet(StringComparer.OrdinalIgnoreCase)).ToArray();
        var core = closures.SelectMany(paths => paths).GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directories = graph.Keys.GroupBy(path => Path.GetDirectoryName(path)!, StringComparer.OrdinalIgnoreCase).ToArray();
        bool changed;
        do
        {
            var previousCount = core.Count;
            core.UnionWith(Closure(core, graph));
            foreach (var siblings in directories)
            {
                var owners = Enumerable.Range(0, workers).Where(index => siblings.Any(path => closures[index].Contains(path))).ToArray();
                // Separate project files can share obj. Promoting the directory and its dependencies avoids concurrent builds.
                if (siblings.Any(core.Contains) || owners.Length > 1)
                {
                    core.UnionWith(siblings);
                }
            }

            changed = core.Count != previousCount;
        } while (changed);

        var groups = closures.Select(paths => paths.Except(core, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray()).ToArray();
        var downwardClosed = core.All(path => graph[path].All(core.Contains));
        var sharedDirectoriesSafe = directories.All(siblings => siblings.All(core.Contains) ||
            groups.Count(group => siblings.Any(path => group.Contains(path, StringComparer.OrdinalIgnoreCase))) <= 1);
        if (!downwardClosed || !sharedDirectoriesSafe || core.Count + groups.Sum(group => group.Length) != graph.Count)
        {
            throw new InvalidOperationException("The core-first plan violates closure, directory ownership, or graph coverage.");
        }

        var groupCosts = groups.Select(paths => paths.Sum(path => costs[path])).ToArray();
        var average = groupCosts.Sum() / workers;
        var ratio = average > 0 ? groupCosts.Max() / average : 1;
        return new CoreFirstPlan(graphSource, costSource, core.Order(StringComparer.Ordinal).ToArray(),
            requests.Select(paths => paths.Where(path => !core.Contains(path)).ToArray()).ToArray(), groups,
            core.Sum(path => costs[path]), costs.Values.Sum(), groupCosts, downwardClosed, sharedDirectoriesSafe)
        {
            BalanceEpsilon = epsilon, BalanceRatio = ratio, BalanceSatisfied = ratio <= 1 + epsilon + 1e-12,
        };
    }

    private static void AddNonCompilationEdges(Dictionary<string, HashSet<string>> graph)
    {
        foreach (var path in graph.Keys)
        {
            if (!File.Exists(path) || !IsManagedProject(path))
            {
                continue;
            }

            foreach (var reference in XDocument.Load(path).Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
            {
                var outputAssembly = reference.Attribute("ReferenceOutputAssembly")?.Value ??
                    reference.Elements().FirstOrDefault(element => element.Name.LocalName == "ReferenceOutputAssembly")?.Value;
                if (!string.Equals(outputAssembly?.Trim(), "false", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var include in (reference.Attribute("Include")?.Value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!include.Contains("$("))
                    {
                        var target = Path.GetFullPath(include, Path.GetDirectoryName(path)!);
                        if (graph.ContainsKey(target))
                        {
                            graph[path].Add(target);
                        }
                    }
                }
            }
        }
    }

    private static Dictionary<string, HashSet<string>> ReadXml(string solutionPath)
    {
        var directory = Path.GetDirectoryName(solutionPath)!;
        var graph = XDocument.Load(solutionPath).Descendants("Project")
            .Select(element => Path.GetFullPath(element.Attribute("Path")!.Value, directory))
            .Where(path => IsManagedProject(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var path in graph.Keys)
        {
            foreach (var element in XDocument.Load(path).Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
            {
                foreach (var item in (element.Attribute("Include")?.Value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (item.Contains("$("))
                    {
                        continue;
                    }

                    var target = Path.GetFullPath(item, Path.GetDirectoryName(path)!);
                    if (graph.ContainsKey(target))
                    {
                        graph[path].Add(target);
                    }
                }
            }
        }

        return graph;
    }

    private static Dictionary<string, HashSet<string>> ReadSnapshot(string snapshotPath, string directory)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(snapshotPath));
        if (document.RootElement.GetProperty("Status").GetString() != "ok")
        {
            throw new InvalidDataException("The snapshot load was unsuccessful.");
        }

        var projects = document.RootElement.GetProperty("Projects").EnumerateObject().ToArray();
        var keyPaths = projects.ToDictionary(project => project.Name,
            project => ResolvePath(project.Name.Split('|')[0], directory), StringComparer.OrdinalIgnoreCase);
        var graph = keyPaths.Values.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            var references = JsonSerializer.Deserialize<string[]>(project.Value.GetProperty("ProjectReferences").GetString()!)!;
            foreach (var reference in references)
            {
                using var targetDocument = JsonDocument.Parse(reference);
                var target = targetDocument.RootElement.GetProperty("Target").GetString()!;
                if (!keyPaths.TryGetValue(target, out var path))
                {
                    // Historical snapshots can redact outer keys while retaining absolute targets in nested JSON strings.
                    path = ResolvePath(target.Split('|')[0], directory);
                    if (!graph.ContainsKey(path))
                    {
                        throw new InvalidDataException($"The snapshot graph references a missing project key: {target}");
                    }
                }

                graph[keyPaths[project.Name]].Add(path);
            }
        }

        return graph;
    }

    private static string ResolvePath(string path, string directory)
    {
        var prefix = "<solution>";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[prefix.Length..].TrimStart('/', '\\');
        }

        return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar), directory);
    }

    private static bool IsManagedProject(string path) => Path.GetExtension(path).ToLowerInvariant() is ".csproj" or ".vbproj";

    internal static HashSet<string> Closure(IEnumerable<string> roots, Dictionary<string, HashSet<string>> graph)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(roots);
        while (pending.TryPop(out var path))
        {
            if (!visited.Add(path))
            {
                continue;
            }

            foreach (var target in graph[path])
            {
                pending.Push(target);
            }
        }

        return visited;
    }
}
