using System.Text.Json;
using System.Xml.Linq;

namespace ParallelLoad;

internal static class ProjectPartition
{
    public static int Export(string[] args)
    {
        if (args is not ["comparison-partitions", "--solution", var solutionPath, "--output", var output])
        {
            throw new ArgumentException("Usage: comparison-partitions --solution PATH --output PATH");
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        string Relative(string path) => Path.GetRelativePath(directory, path).Replace('\\', '/');
        var order = XDocument.Load(solutionPath).Descendants("Project")
            .Select(element => element.Attribute("Path")!.Value.Replace('\\', '/')).ToArray();
        var partitions = new Dictionary<string, string[][]>();
        foreach (var strategy in new[] { "round-robin", "cluster" })
        {
            foreach (var workers in new[] { 2, 4, 8 })
            {
                partitions.Add($"{strategy}-{workers}", Create(solutionPath, workers, strategy)
                    .Select(group => group.Select(Relative).ToArray()).ToArray());
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { SolutionOrder = order, Partitions = partitions },
            new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    public static string[][] Create(string solutionPath, int workers, string strategy)
    {
        // This experiment accepts the two XML solutions in its protocol; no evaluated graph is used.
        var directory = Path.GetDirectoryName(solutionPath)!;
        var projects = XDocument.Load(solutionPath).Descendants("Project")
            .Select(element => Path.GetFullPath(element.Attribute("Path")!.Value, directory)).ToArray();
        var groups = Enumerable.Range(0, workers).Select(_ => new List<string>()).ToArray();
        if (strategy == "round-robin")
        {
            for (var index = 0; index < projects.Length; index++)
            {
                groups[index % workers].Add(projects[index]);
            }
        }
        else
        {
            foreach (var component in FindComponents(projects).OrderByDescending(component => component.Length))
            {
                groups.MinBy(group => group.Count)!.AddRange(component);
            }
        }

        return groups.Select(group => group.ToArray()).ToArray();
    }

    public static string WriteFilter(string solutionPath, string[] projects, string directory, int group)
    {
        var path = Path.Combine(directory, $"group-{group}.slnf");
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            solution = new
            {
                path = solutionPath,
                projects = projects.Select(project => Path.GetRelativePath(solutionDirectory, project)).ToArray(),
            },
        }));
        return path;
    }

    private static IEnumerable<string[]> FindComponents(string[] projects)
    {
        var adjacency = projects.ToDictionary(project => project, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects.Where(project => Path.GetExtension(project).Equals(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var reference in XDocument.Load(project).Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (include is null || include.Contains("$("))
                {
                    continue;
                }

                foreach (var item in include.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var target = Path.GetFullPath(item, Path.GetDirectoryName(project)!);
                    if (adjacency.ContainsKey(target))
                    {
                        adjacency[project].Add(target);
                        adjacency[target].Add(project);
                    }
                }
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            if (!visited.Add(project))
            {
                continue;
            }

            var component = new List<string>();
            var pending = new Stack<string>();
            pending.Push(project);
            while (pending.TryPop(out var current))
            {
                component.Add(current);
                foreach (var target in adjacency[current].Where(visited.Add))
                {
                    pending.Push(target);
                }
            }

            yield return component.ToArray();
        }
    }
}
