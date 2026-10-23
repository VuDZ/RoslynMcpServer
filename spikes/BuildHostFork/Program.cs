using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ParallelSearch;

namespace BuildHostFork;

/// <summary>
/// observe: loads a solution like the server does, with whichever BuildHost sits in BuildHost-netcore next to
/// this program, and writes the workspace observation, diagnostics, load time and a first search.
/// load, cancel, kill-host: failure scenarios, see <see cref="FailureScenario"/>.
/// compare: compares fork runs with original runs of one result folder.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "observe")
        {
            var solutionPath = Path.GetFullPath(args[1]);
            MsBuildRegistration.Register(Path.GetDirectoryName(solutionPath)!);
            var name = args.Length >= 4 ? args[3] : "NoSuchDeclarationName7d3f2a";
            await ObserveAsync(solutionPath, Path.GetFullPath(args[2]), name).ConfigureAwait(false);
            return 0;
        }

        if (args.Length >= 3 && args[0] is "load" or "cancel" or "kill-host")
        {
            var solutionPath = Path.GetFullPath(args[1]);
            MsBuildRegistration.Register(Path.GetDirectoryName(solutionPath)!);
            var delayMs = args.Length >= 4 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 2000;
            await FailureScenario.RunAsync(args[0], solutionPath, Path.GetFullPath(args[2]), delayMs).ConfigureAwait(false);
            return 0;
        }

        if (args.Length >= 2 && args[0] == "compare")
        {
            Console.WriteLine(ObservationComparer.Run(Path.GetFullPath(args[1])));
            return 0;
        }

        Console.Error.WriteLine("Usage: observe SOLUTION OUTPUT_JSON [NAME] | load|cancel|kill-host SOLUTION OUTPUT_JSON [DELAY_MS] | compare RESULT_DIRECTORY");
        return 2;
    }

    // MSBuild types must not be loaded before MSBuildLocator registration; this method is JIT-compiled after it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ObserveAsync(string solutionPath, string outputPath, string name)
    {
        var buildHost = Path.Combine(AppContext.BaseDirectory, "BuildHost-netcore", "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll");
        var diagnostics = new List<string>();
        var sampler = new BuildHostSampler();
        var watch = Stopwatch.StartNew();
        var (solution, _) = await SolutionLoader.LoadAsync(solutionPath, diagnostics: diagnostics).ConfigureAwait(false);
        var loadMs = watch.Elapsed.TotalMilliseconds;
        var buildHosts = sampler.Stop();
        var search = await SearchMeasurement.MeasureAsync(solution, Path.GetDirectoryName(solutionPath)!, name, 1).ConfigureAwait(false);
        var remaining = await sampler.WaitForExitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        using var process = Process.GetCurrentProcess();
        var report = new
        {
            Status = "ok",
            BuildHost = FileVersionInfo.GetVersionInfo(buildHost).ProductVersion,
            WallMs = loadMs,
            ClientCpuMs = process.TotalProcessorTime.TotalMilliseconds,
            ClientPeakWorkingSetMb = process.PeakWorkingSet64 / 1048576.0,
            BuildHostProcesses = buildHosts,
            RemainingBuildHosts = remaining.Length,
            FirstSearch = search,
            Projects = ParallelLoad.SolutionObservation.Capture(solution),
            Diagnostics = diagnostics.Order(StringComparer.Ordinal).ToArray(),
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
