namespace TestImpactIlProbe;

/// <summary>
/// Compares the answers with coverage collected per test class by <see cref="ClassCoverageCommand"/>: for
/// every production file, which of the test classes that execute it each layer selects.
/// </summary>
internal static class RecallCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(CommandArgs.Value(args, "--root") ?? throw new InvalidOperationException("Pass --root."));
        var bins = CommandArgs.Values(args, "--bin").Select(Path.GetFullPath).ToList();
        var classes = Path.GetFullPath(CommandArgs.Value(args, "--classes") ?? throw new InvalidOperationException("Pass --classes with the directory class-coverage wrote."));
        var output = Path.GetFullPath(CommandArgs.Value(args, "--out") ?? "il-recall-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        var loaded = GraphLoader.Load(bins.Count == 0 ? [root] : bins, [root]);
        var graph = loaded.Graph;
        if (graph.TestClasses.Length == 0)
        {
            Console.Error.WriteLine("No test classes found. Build the solution in Debug first, with portable PDBs next to the assemblies.");
            return 1;
        }

        var production = new ProductionCode(graph);
        var paths = production.Files.Select(file => file.Path).ToList();
        var nodes = graph.TestClasses.ToDictionary(node => (graph.Assemblies[graph.AssemblyOf[node]], graph.Names[node]));
        var executing = paths.Select(_ => new List<int>()).ToArray();
        var measured = new HashSet<int>();
        var unknown = 0;
        var silent = 0;
        foreach (var (assembly, testClass, path) in ClassReports.Find(classes))
        {
            if (!nodes.TryGetValue((assembly, testClass), out var node))
            {
                unknown++;
                continue;
            }

            measured.Add(node);
            var executed = Executed(path, paths, root);
            silent += executed.Count == 0 ? 1 : 0;
            foreach (var index in executed)
            {
                executing[index].Add(node);
            }
        }

        if (measured.Count == 0)
        {
            Console.Error.WriteLine($"No report under {classes} belongs to a test class of the graph. Run class-coverage on the same build first.");
            return 1;
        }

        var testAssemblies = measured.Select(node => graph.AssemblyOf[node]).ToHashSet();
        var (idle, idleRuns, idleRunsExecuting) = ReadIdleRuns(classes, graph, testAssemblies, paths, root);
        var files = RecallComparison.Collect(graph, new ImpactTable(graph), production, executing, idle, measured);
        var counts = new MeasuredClasses(graph.TestClasses.Length, measured.Count, silent, unknown, testAssemblies.Count, idleRuns, idleRunsExecuting);
        var report = new RecallReport(loaded, counts, files);
        foreach (var line in report.Headline())
        {
            Console.WriteLine(line);
        }

        var (raw, summary) = await report.WriteAsync(output, root, cancellationToken).ConfigureAwait(false);
        Console.WriteLine("Raw (names, keep it private): " + raw);
        Console.WriteLine("Summary (counts only, can be shared): " + summary);
        return 0;
    }

    /// <summary>
    /// Reads what each test assembly executes with no test selected. Only the assemblies of the measured
    /// classes count: an idle run says nothing about a file unless a class of that assembly was run.
    /// </summary>
    /// <returns>For each production file, the test assemblies whose idle run executes it; then the counts of runs.</returns>
    private static (HashSet<int>[] ByFile, int Runs, int RunsExecuting) ReadIdleRuns(
        string classes,
        DependencyGraph graph,
        HashSet<int> testAssemblies,
        List<string> paths,
        string root)
    {
        var byFile = paths.Select(_ => new HashSet<int>()).ToArray();
        var runs = 0;
        var runsExecuting = 0;
        foreach (var (assembly, path) in ClassReports.FindIdle(classes))
        {
            var index = Array.IndexOf(graph.Assemblies, assembly);
            if (!testAssemblies.Contains(index))
            {
                continue;
            }

            var executed = Executed(path, paths, root);
            runs++;
            runsExecuting += executed.Count == 0 ? 0 : 1;
            foreach (var file in executed)
            {
                byFile[file].Add(index);
            }
        }

        return (byFile, runs, runsExecuting);
    }

    /// <returns>Indexes of the production files a short report of <see cref="CoberturaWriter"/> names as executed.</returns>
    private static List<int> Executed(string report, List<string> paths, string root)
    {
        var coverage = new CoverageIndex(paths, root);
        coverage.Add(CoberturaReader.Read(report));
        return [.. Enumerable.Range(0, paths.Count).Where(index => coverage.Of(paths[index]) == FileCoverage.Covered)];
    }
}
