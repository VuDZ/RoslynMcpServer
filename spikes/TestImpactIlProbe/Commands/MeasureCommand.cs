using System.Diagnostics;

namespace TestImpactIlProbe;

/// <summary>
/// Measures what a class-level dependency graph read from build output costs and how many tests it selects
/// for an edit of a production file, against level 0 (every test assembly that references the edited one).
/// </summary>
internal static class MeasureCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(CommandArgs.Value(args, "--root") ?? throw new InvalidOperationException("Pass --root."));
        var bins = CommandArgs.Values(args, "--bin").Select(Path.GetFullPath).ToList();
        var sample = CommandArgs.Integer(args, "--sample", 40);
        var seed = CommandArgs.Integer(args, "--seed", 1);
        var output = Path.GetFullPath(CommandArgs.Value(args, "--out") ?? "il-impact-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        var loaded = GraphLoader.Load(bins.Count == 0 ? [root] : bins, [root]);
        if (loaded.Graph.TestClasses.Length == 0)
        {
            Console.Error.WriteLine("No test classes found. Build the solution in Debug first, with portable PDBs next to the assemblies.");
            return 1;
        }

        var watch = Stopwatch.StartNew();
        var table = new ImpactTable(loaded.Graph);
        GraphLoader.Record(loaded.Phases, "closures", watch);
        var production = new ProductionCode(loaded.Graph);
        var report = new MeasureReport(loaded, table, production);

        var missing = new List<string>();
        var targets = await CollectTargetsAsync(args, loaded.Graph, production, report, root, missing, cancellationToken).ConfigureAwait(false);
        targets.AddRange(Sample(production, report, sample, seed));

        foreach (var line in report.Headline())
        {
            Console.WriteLine(line);
        }

        GraphLoader.Record(loaded.Phases, "statistics", watch);
        var (raw, summary) = await report.WriteAsync(output, root, seed, targets, missing, cancellationToken).ConfigureAwait(false);
        Console.WriteLine("Raw (names, keep it private): " + raw);
        Console.WriteLine("Summary (counts only, can be shared): " + summary);
        return 0;
    }

    private static async Task<List<TargetResult>> CollectTargetsAsync(
        string[] args,
        DependencyGraph graph,
        ProductionCode production,
        MeasureReport report,
        string root,
        List<string> missing,
        CancellationToken cancellationToken)
    {
        var targets = new List<TargetResult>();
        foreach (var file in await ReadListAsync(CommandArgs.Value(args, "--files"), cancellationToken).ConfigureAwait(false))
        {
            var found = Find(production, root, file);
            if (found is null)
            {
                missing.Add(file);
                continue;
            }

            targets.Add(report.Answer("file", found.Value.Path, found.Value.Types));
        }

        foreach (var name in await ReadListAsync(CommandArgs.Value(args, "--types"), cancellationToken).ConfigureAwait(false))
        {
            var nodes = Enumerable.Range(0, graph.Names.Length).Where(node => graph.Names[node] == name).ToList();
            if (nodes.Count == 0)
            {
                missing.Add(name);
                continue;
            }

            targets.Add(report.Answer("type", name, nodes));
        }

        return targets;
    }

    private static (string Path, IReadOnlyList<int> Types)? Find(ProductionCode production, string root, string file)
    {
        var full = Path.GetFullPath(Path.Combine(root, file));
        var suffix = "/" + file.Replace('\\', '/').TrimStart('/');
        foreach (var candidate in production.Files)
        {
            if (string.Equals(candidate.Path, full, StringComparison.OrdinalIgnoreCase)
                || candidate.Path.Replace('\\', '/').EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Random production files that level 0 has tests for: a file without them shows nothing.</summary>
    private static List<TargetResult> Sample(ProductionCode production, MeasureReport report, int count, int seed)
    {
        var random = new Random(seed);
        var files = production.Files.ToList();
        var chosen = new List<TargetResult>();
        while (chosen.Count < count && files.Count > 0)
        {
            var index = random.Next(files.Count);
            var answer = report.Answer("sample", files[index].Path, files[index].Types);
            files.RemoveAt(index);
            if (answer.LevelZero.Count > 0)
            {
                chosen.Add(answer);
            }
        }

        return chosen;
    }

    private static async Task<IReadOnlyList<string>> ReadListAsync(string? path, CancellationToken cancellationToken)
    {
        if (path is null)
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        return [.. lines.Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'))];
    }
}
