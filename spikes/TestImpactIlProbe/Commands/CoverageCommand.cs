namespace TestImpactIlProbe;

/// <summary>
/// Checks empty answers against coverage of a test run: a production file that a test executes and that the
/// graph selects no test for is a dependency the graph does not see.
/// </summary>
internal static class CoverageCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(CommandArgs.Value(args, "--root") ?? throw new InvalidOperationException("Pass --root."));
        var bins = CommandArgs.Values(args, "--bin").Select(Path.GetFullPath).ToList();
        var reports = ReportFiles(CommandArgs.Values(args, "--coverage"));
        var output = Path.GetFullPath(CommandArgs.Value(args, "--out") ?? "il-coverage-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        var loaded = GraphLoader.Load(bins.Count == 0 ? [root] : bins, [root]);
        if (loaded.Graph.TestClasses.Length == 0)
        {
            Console.Error.WriteLine("No test classes found. Build the solution in Debug first, with portable PDBs next to the assemblies.");
            return 1;
        }

        var production = new ProductionCode(loaded.Graph);
        var coverage = new CoverageIndex(production.Files.Select(file => file.Path), root);
        foreach (var file in reports)
        {
            coverage.Add(CoberturaReader.Read(file));
        }

        if (coverage.Matched == 0)
        {
            Console.Error.WriteLine($"The reports name {coverage.Unmatched.Count} files and none of them is a production file of the graph, for example: {coverage.Unmatched.FirstOrDefault()}. Collect coverage from the same build and the same source directory.");
            return 1;
        }

        var files = CoverageComparison.Collect(loaded.Graph, new ImpactTable(loaded.Graph), production, coverage);
        var report = new CoverageReport(loaded, coverage, files);
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
    /// A test run with a coverage collector leaves one report per test project in a directory with a random
    /// name, so a directory stands for every Cobertura report under it.
    /// </summary>
    private static List<string> ReportFiles(IReadOnlyList<string> arguments)
    {
        var files = new List<string>();
        foreach (var argument in arguments)
        {
            if (Directory.Exists(argument))
            {
                files.AddRange(Directory.EnumerateFiles(argument, "*.cobertura.xml", SearchOption.AllDirectories));
            }
            else if (File.Exists(argument))
            {
                files.Add(argument);
            }
            else
            {
                throw new InvalidOperationException($"No coverage report at {argument}.");
            }
        }

        return files.Count > 0
            ? files
            : throw new InvalidOperationException("Pass --coverage with a Cobertura report or a directory with *.cobertura.xml files.");
    }
}
