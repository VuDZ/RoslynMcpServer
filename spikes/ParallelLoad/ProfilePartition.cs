using System.Text.Json;
using ParallelLoad.Models;

namespace ParallelLoad;

internal static class ProfilePartition
{
    public static int Run(string[] args)
    {
        var values = new Dictionary<string, string>();
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !values.TryAdd(args[index], args[index + 1]))
            {
                throw new ArgumentException("Each option must have one value and occur only once.");
            }
        }

        if (values.Count != 3 || !values.ContainsKey("--profile") || !values.ContainsKey("--workers") ||
            !values.ContainsKey("--output"))
        {
            throw new ArgumentException("Usage: plan --profile PATH --workers N --output PATH");
        }

        var profile = JsonSerializer.Deserialize<CostProfile>(File.ReadAllText(values["--profile"]))!;
        var plan = Create(profile, int.Parse(values["--workers"]));
        var output = Path.GetFullPath(values["--output"]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"plan status={plan.Status} workers={plan.Workers} repeats={plan.RepeatedProjects}");
        return 0;
    }

    public static PartitionPlan Create(CostProfile profile, int workers)
    {
        if (profile.Status != "ok" || profile.Projects.Length == 0 || workers < 1 ||
            profile.Projects.Any(project => project.WeightMs is null || !double.IsFinite(project.WeightMs.Value) || project.WeightMs < 0))
        {
            throw new ArgumentException("Planning requires a nonempty successful profile and positive workers.");
        }

        var projects = profile.Projects.ToDictionary(project => project.Key, StringComparer.OrdinalIgnoreCase);
        if (projects.Values.SelectMany(project => project.References).Any(reference => !projects.ContainsKey(reference)))
        {
            throw new InvalidDataException("The profile graph contains a missing target key.");
        }
        var referenced = projects.Values.SelectMany(project => project.References).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roots = projects.Keys.Where(key => !referenced.Contains(key)).Order(StringComparer.Ordinal).ToList();
        var covered = Closure(roots, projects);
        foreach (var key in projects.Keys.Order(StringComparer.Ordinal))
        {
            // Cycles without an incoming root still need an explicit request.
            if (!covered.Contains(key))
            {
                roots.Add(key);
                covered.UnionWith(Closure([key], projects));
            }
        }

        var bundles = BundleRoots(roots, projects);
        var requests = Enumerable.Range(0, workers).Select(_ => new List<string>()).ToArray();
        var closures = Enumerable.Range(0, workers).Select(_ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)).ToArray();
        foreach (var bundle in bundles.OrderByDescending(bundle => Weight(Closure(bundle, projects), projects)))
        {
            var closure = Closure(bundle, projects);
            // Minimize the resulting group cost; a pure marginal-cost tie sends every disjoint root to group zero.
            var group = Enumerable.Range(0, workers)
                .MinBy(index => Weight(closures[index].Union(closure), projects));
            requests[group].AddRange(bundle);
            closures[group].UnionWith(closure);
        }

        var multiplicities = closures.SelectMany(closure => closure).GroupBy(key => key, StringComparer.OrdinalIgnoreCase);
        var core = multiplicities.Where(group => group.Count() > 1).Select(group => group.Key).Order().ToArray();
        var markup = core.Where(key => projects[key].Markup).ToArray();
        if (markup.Length != 0 || closures.SelectMany(closure => closure).Distinct(StringComparer.OrdinalIgnoreCase).Count() != projects.Count)
        {
            throw new InvalidOperationException("Partition violates markup ownership or complete graph coverage.");
        }

        var model = profile.Model;
        var competition = model?.Competition.GetValueOrDefault(workers) ?? 1;
        if (competition <= 0)
        {
            throw new InvalidDataException("The model has no positive competition coefficient for this worker count.");
        }

        return new PartitionPlan(model?.Validated == true ? "validated-model" : "uncalibrated-model", workers,
            requests.Select(group => group.Select(key => projects[key].FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()).ToArray(),
            closures.Select(group => group.Order().ToArray()).ToArray(),
            closures.Select(group => (model?.HostMs ?? 0) + competition * Weight(group, projects)).ToArray(),
            closures.Sum(group => group.Count) - projects.Count, core, markup);
    }

    private static List<List<string>> BundleRoots(List<string> roots, Dictionary<string, ProjectCost> projects)
    {
        var bundles = roots.Select(root => new List<string> { root }).ToList();
        var markup = roots.Select(root => Closure([root], projects).Where(key => projects[key].Markup)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)).ToList();
        for (var index = 0; index < bundles.Count; index++)
        {
            for (var other = index + 1; other < bundles.Count; other++)
            {
                if (!markup[index].Overlaps(markup[other]))
                {
                    continue;
                }

                bundles[index].AddRange(bundles[other]);
                markup[index].UnionWith(markup[other]);
                bundles.RemoveAt(other);
                markup.RemoveAt(other);
                other = index; // Rescan: a newly merged bundle may connect to an earlier disjoint root.
            }
        }

        return bundles;
    }

    private static HashSet<string> Closure(IEnumerable<string> roots, Dictionary<string, ProjectCost> projects)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(roots);
        while (pending.TryPop(out var key))
        {
            if (!projects.TryGetValue(key, out var project) || !result.Add(key))
            {
                continue;
            }

            foreach (var reference in project.References)
            {
                pending.Push(reference);
            }
        }

        return result;
    }

    private static double Weight(IEnumerable<string> keys, Dictionary<string, ProjectCost> projects) =>
        keys.Sum(key => projects[key].WeightMs!.Value);
}
