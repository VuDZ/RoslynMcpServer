using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ParallelSearch;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "check")
        {
            await SelfChecks.RunAsync().ConfigureAwait(false);
            Console.WriteLine("PASS: order, cancellation, failure, empty solution and project without documents.");
            return 0;
        }

        if (args.Length == 0 || args[0] != "measure")
        {
            Console.Error.WriteLine("Usage: measure --solution PATH --name NAME --dop N --output JSON | check");
            return 2;
        }

        var options = ParseOptions(args.Skip(1).ToArray());
        var solutionPath = Path.GetFullPath(options["solution"]);
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var msbuild = MsBuildRegistration.Register(solutionDirectory);
        var report = await MeasureAsync(
                solutionPath, solutionDirectory, options["name"], int.Parse(options["dop"]), msbuild)
            .ConfigureAwait(false);
        File.WriteAllText(options["output"], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    // MSBuild types must not be loaded before MSBuildLocator registration; this method is JIT-compiled after it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<object> MeasureAsync(
        string solutionPath,
        string solutionDirectory,
        string name,
        int degreeOfParallelism,
        string msbuild)
    {
        var loadPauseBefore = GC.GetTotalPauseDuration();
        var (solution, load) = await SolutionLoader.LoadAsync(solutionPath).ConfigureAwait(false);
        var loadGcPauseMs = (GC.GetTotalPauseDuration() - loadPauseBefore).TotalMilliseconds;
        var first = await SearchMeasurement.MeasureAsync(solution, solutionDirectory, name, degreeOfParallelism)
            .ConfigureAwait(false);
        var second = await SearchMeasurement.MeasureAsync(solution, solutionDirectory, name, degreeOfParallelism)
            .ConfigureAwait(false);
        return new
        {
            solution = Path.GetFileName(solutionPath),
            name,
            degreeOfParallelism,
            processorCount = Environment.ProcessorCount,
            // Only the SDK folder name: the result is committed, and the install path can name the user.
            msbuildSdk = Path.GetFileName(Path.TrimEndingDirectorySeparator(msbuild)),
            runtime = Environment.Version.ToString(),
            gc = GcReport.DescribeMode(),
            load,
            loadGcPauseMs,
            firstSearch = first,
            secondSearch = second,
            final = GcReport.DescribeFinalMemory(),
        };
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index + 1 < args.Length; index += 2)
        {
            options[args[index].TrimStart('-')] = args[index + 1];
        }

        foreach (var required in new[] { "solution", "name", "dop", "output" })
        {
            if (!options.ContainsKey(required))
            {
                throw new ArgumentException($"Missing --{required}.");
            }
        }

        return options;
    }
}
