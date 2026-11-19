using System.Diagnostics;

namespace TestImpactIlProbe;

/// <summary>
/// Runs every test class of the graph on its own under a coverage collector and keeps, per class, the files
/// it executed. That is the reference the answers are compared with: which test classes really use a file.
/// </summary>
internal static class ClassCoverageCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(CommandArgs.Value(args, "--root") ?? throw new InvalidOperationException("Pass --root."));
        var bins = CommandArgs.Values(args, "--bin").Select(Path.GetFullPath).ToList();
        var output = Path.GetFullPath(CommandArgs.Value(args, "--out") ?? throw new InvalidOperationException("Pass --out."));
        var template = TestRunTemplate.Choose(CommandArgs.Value(args, "--runner"), CommandArgs.Value(args, "--run"));
        var only = CommandArgs.Value(args, "--only");
        var pathFilter = CommandArgs.Value(args, "--path-filter");
        var limit = CommandArgs.Integer(args, "--limit", int.MaxValue);
        var sample = CommandArgs.Integer(args, "--sample", int.MaxValue);
        var seed = CommandArgs.Integer(args, "--seed", 1);
        var timeout = TimeSpan.FromSeconds(CommandArgs.Integer(args, "--timeout", 900));

        var loaded = GraphLoader.Load(bins.Count == 0 ? [root] : bins, [root]);
        var graph = loaded.Graph;
        var all = graph.TestClasses
            .Select(node => (Assembly: graph.Assemblies[graph.AssemblyOf[node]], Name: graph.Names[node]))
            .Where(test => only is null || (test.Assembly + "/" + test.Name).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(test => test.Assembly, StringComparer.Ordinal)
            .ThenBy(test => test.Name, StringComparer.Ordinal)
            .ToList();
        var classes = Sample(all, sample, seed).Take(limit).ToList();
        if (classes.Count == 0)
        {
            Console.Error.WriteLine("No test classes to run. Build the solution in Debug first, and check --only.");
            return 1;
        }

        Directory.CreateDirectory(output);
        var run = new RunSettings(template, loaded.Directories, pathFilter, output, timeout);
        var withoutIdleRun = await RunIdleAsync(run, classes.Select(test => test.Assembly).Distinct(), cancellationToken).ConfigureAwait(false);
        var failed = 0;
        for (var index = 0; index < classes.Count; index++)
        {
            var (assembly, name) = classes[index];
            var target = ClassReports.PathFor(output, assembly, name);
            if (File.Exists(target))
            {
                continue;
            }

            var outcome = await RunAsync(run, assembly, name, target, ClassReports.LogFor(output, assembly, name), cancellationToken).ConfigureAwait(false);
            failed += File.Exists(target) ? 0 : 1;
            Console.WriteLine($"[{index + 1}/{classes.Count}] {assembly} {name}: {outcome}");
        }

        Console.WriteLine($"Test classes: {classes.Count} of {all.Count}, without a report: {failed}; test assemblies without an idle run: {withoutIdleRun}. Reports: {output}");
        Console.WriteLine("Run the command again to retry what has no report; the classes and assemblies that have it are skipped.");
        return 0;
    }

    /// <summary>
    /// A large solution has thousands of test classes and one run per class takes hours. A random part of
    /// them still tells how many executing classes an answer keeps, because an answer is compared only with
    /// the measured classes. The same seed gives the same classes, so an interrupted run continues.
    /// </summary>
    private static List<T> Sample<T>(List<T> ordered, int count, int seed)
    {
        if (count >= ordered.Count)
        {
            return ordered;
        }

        var random = new Random(seed);
        var chosen = Enumerable.Range(0, ordered.Count).OrderBy(_ => random.Next()).Take(count).Order();
        return [.. chosen.Select(index => ordered[index])];
    }

    /// <summary>
    /// Runs each test assembly once with a class name no test has. A run of one class starts with a search
    /// for the tests of the whole assembly, and the search executes code of its own: xUnit, for one, computes
    /// the theory data of every class. The idle run records those files, so that the comparison can tell a
    /// class that uses a file from a class that merely shares an assembly with one.
    /// </summary>
    /// <returns>The number of assemblies left without the report.</returns>
    private static async Task<int> RunIdleAsync(RunSettings run, IEnumerable<string> assemblies, CancellationToken cancellationToken)
    {
        var failed = 0;
        foreach (var assembly in assemblies)
        {
            var target = ClassReports.IdlePathFor(run.Output, assembly);
            if (File.Exists(target))
            {
                continue;
            }

            var log = ClassReports.IdleLogFor(run.Output, assembly);
            var outcome = await RunAsync(run, assembly, ClassReports.IdleClass, target, log, cancellationToken).ConfigureAwait(false);
            failed += File.Exists(target) ? 0 : 1;
            Console.WriteLine($"[idle] {assembly}: {outcome}");
        }

        return failed;
    }

    private static async Task<string> RunAsync(
        RunSettings run,
        string assembly,
        string testClass,
        string target,
        string log,
        CancellationToken cancellationToken)
    {
        var directory = TestAssemblyLocator.Find(run.Directories, assembly, run.PathFilter, File.Exists);
        if (directory is null)
        {
            return "no directory with the test assembly";
        }

        // One place for the collector's report: the previous run must not be taken for this one.
        var report = Path.Combine(run.Output, "current-run.cobertura.xml");
        Remove(report);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var timeout = run.Timeout;
        var watch = Stopwatch.StartNew();
        var exitCode = await ShellCommand.RunAsync(
            run.Template.Expand(Path.Combine(directory, assembly + ".dll"), testClass, report),
            directory,
            log,
            timeout,
            cancellationToken).ConfigureAwait(false);
        var seconds = watch.Elapsed.TotalSeconds;

        var reports = Directory.Exists(report)
            ? Directory.EnumerateFiles(report, "*.cobertura.xml", SearchOption.AllDirectories).ToList()
            : File.Exists(report) ? [report] : [];
        if (reports.Count == 0)
        {
            return exitCode is null
                ? $"stopped after {timeout.TotalSeconds:F0} s, no report"
                : $"no report, exit code {exitCode}, {seconds:F1} s";
        }

        var read = reports.Select(CoberturaReader.Read).ToList();
        CoberturaWriter.WriteExecutedFiles(target, read);
        Remove(report);

        // Failed tests give a non-zero exit code and still executed code, so the report counts either way.
        var executed = read.SelectMany(one => one.Files.Where(file => file.Value).Select(file => file.Key)).Distinct().Count();
        return $"{executed} files executed, exit code {exitCode?.ToString() ?? "none (stopped)"}, {seconds:F1} s";
    }

    private static void Remove(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>What every run of the command shares: how a class is run and where the reports go.</summary>
    /// <param name="Directories">For every build output directory, the names of the assembly files in it.</param>
    private sealed record RunSettings(
        TestRunTemplate Template,
        IReadOnlyDictionary<string, HashSet<string>> Directories,
        string? PathFilter,
        string Output,
        TimeSpan Timeout);
}
