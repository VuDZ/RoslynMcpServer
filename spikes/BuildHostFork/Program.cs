using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BuildHostFork.Rpc;
using BuildHostFork.Trace;
using ParallelSearch;
using ParallelSearch.Models;

namespace BuildHostFork;

/// <summary>
/// observe: loads a solution like the server does, with whichever BuildHost sits in BuildHost-netcore next to
/// this program, and writes the workspace observation, diagnostics, load time and a first search.
/// load, cancel, kill-host: failure scenarios, see <see cref="FailureScenario"/>.
/// compare: compares fork runs with original runs of one result folder; exit code 1 when they are not equivalent.
/// matrix: compares every fork variant of a matrix result folder with its original runs and writes matrix.md, see
/// <see cref="MatrixSummary"/>; exit code 1 when a variant is not equivalent.
/// check: self-checks of compare on synthetic runs.
/// trace-report, trace-summary: report of one traced load and medians over traced rounds (BuildHost/task-01); observe writes the trace when
/// ROSLYN_BUILDHOST_TRACE_DIRECTORY is set, see <see cref="ClientTraceFile"/>.
/// rpc-capture, rpc-compare, rpc-check: the complete netcore ProjectFileInfo protocol, independent of workspace projection.
/// </summary>
internal static class Program
{
    /// <summary>The BuildHost folder this copy of the spike must use, as <see cref="BuildHostSampler"/> reports it.</summary>
    public static string ExpectedHost => "netcore: " + Path.Combine(AppContext.BaseDirectory, "BuildHost-netcore");

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "rpc-capture")
        {
            var solutionPath = Path.GetFullPath(args[2]);
            MsBuildRegistration.Register(Path.GetDirectoryName(solutionPath)!);
            await CaptureRpcAsync(Path.GetFullPath(args[1]), solutionPath, Path.GetFullPath(args[3])).ConfigureAwait(false);
            return 0;
        }

        if (args.Length is 3 or 4 && args[0] == "rpc-compare" && (args.Length == 3 || args[3] == "glob-sets"))
        {
            return ProjectFileInfoComparison.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), globSets: args.Length == 4);
        }

        if (args is ["rpc-check"])
        {
            return ProjectFileInfoComparison.SelfCheck();
        }

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
            var (summary, equivalent, _) = ObservationComparer.Run(Path.GetFullPath(args[1]));
            Console.WriteLine(summary);
            return equivalent ? 0 : 1;
        }

        if (args.Length >= 3 && args[0] == "matrix")
        {
            var (summary, equivalent) = MatrixSummary.Run(Path.GetFullPath(args[1]), args[2..]);
            Console.WriteLine(summary);
            return equivalent ? 0 : 1;
        }

        if (args.Length >= 2 && args[0] == "trace-report")
        {
            var summary = TraceReport.Run(Path.GetFullPath(args[1]), redact: args.Length >= 3 && args[2] == "redact");
            Console.WriteLine(File.ReadAllText(Path.Combine(Path.GetFullPath(args[1]), "report.md")));

            // Operations served by an untraced host are listed in the report but do not fail it.
            return summary.Complete && summary.Clock.Agrees ? 0 : 1;
        }

        if (args.Length >= 2 && args[0] == "trace-summary")
        {
            Console.WriteLine(TraceRounds.Summarize(Path.GetFullPath(args[1]), redact: args.Length >= 3 && args[2] == "redact"));
            return 0;
        }

        if (args.Length >= 1 && args[0] == "check")
        {
            return ComparerSelfCheck.Run();
        }

        Console.Error.WriteLine("Usage: observe SOLUTION OUTPUT_JSON [NAME] | load|cancel|kill-host SOLUTION OUTPUT_JSON [DELAY_MS] | compare RESULT_DIRECTORY | matrix RESULT_DIRECTORY VARIANT... | trace-report TRACE_DIRECTORY [redact] | trace-summary RESULT_DIRECTORY [redact] | check | rpc-capture HOST_DLL SOLUTION OUTPUT_JSON | rpc-compare LEFT_JSON RIGHT_JSON [glob-sets] | rpc-check");
        return 2;
    }

    /// <summary>
    /// The global property in which the client tells the fork BuildHost which solution it loads (BuildHost/task-02
    /// part B). Both sides get it: the fork takes it out before any evaluation, the original keeps it as an ordinary
    /// property that no project reads, so an identical result also shows that it changes nothing.
    /// </summary>
    internal static IDictionary<string, string> SolutionProperties(string solutionPath)
        => new Dictionary<string, string> { ["RoslynMcpSolutionPath"] = Path.GetFullPath(solutionPath) };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task CaptureRpcAsync(string hostPath, string solutionPath, string outputPath)
        => ProjectFileInfoCapture.RunAsync(hostPath, solutionPath, outputPath);

    // MSBuild types must not be loaded before MSBuildLocator registration; this method is JIT-compiled after it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ObserveAsync(string solutionPath, string outputPath, string name)
    {
        var buildHost = Path.Combine(AppContext.BaseDirectory, "BuildHost-netcore", "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll");
        var diagnostics = new List<string>();
        using var process = Process.GetCurrentProcess();
        using var sampler = new BuildHostSampler();

        // With ROSLYN_BUILDHOST_TRACE_DIRECTORY set, the fork BuildHost writes its spans there and this load writes
        // the loader's operations next to them; the original BuildHost ignores the variable.
        var traceDirectory = ClientTraceFile.TraceDirectory;
        var recorder = traceDirectory is null ? null : new ProgressRecorder();
        var window = new LoadWindow();
        var watch = Stopwatch.StartNew();
        var (solution, _) = await SolutionLoader.LoadAsync(
            solutionPath, diagnostics: diagnostics, progress: recorder, window: window, properties: SolutionProperties(solutionPath)).ConfigureAwait(false);
        var loadMs = watch.Elapsed.TotalMilliseconds;

        // Client CPU and memory of the load alone; the first search is measured on its own below.
        process.Refresh();
        var clientCpuMs = process.TotalProcessorTime.TotalMilliseconds;
        var clientPeakWorkingSetMb = process.PeakWorkingSet64 / 1048576.0;
        var buildHosts = sampler.Stop();
        var search = await SearchMeasurement.MeasureAsync(solution, Path.GetDirectoryName(solutionPath)!, name, 1).ConfigureAwait(false);
        var remaining = await sampler.WaitForExitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        var report = new
        {
            Status = "ok",
            BuildHost = FileVersionInfo.GetVersionInfo(buildHost).ProductVersion,
            ExpectedHost,
            WallMs = loadMs,
            ClientCpuMs = clientCpuMs,
            ClientPeakWorkingSetMb = clientPeakWorkingSetMb,
            BuildHostProcesses = buildHosts,
            RemainingBuildHosts = remaining.Length,
            FirstSearch = search,
            Projects = ParallelLoad.SolutionObservation.Capture(solution),
            Diagnostics = diagnostics.Order(StringComparer.Ordinal).ToArray(),

            // The comparator reads only Diagnostics; this keeps the order in which the loader reported them, so the
            // logs of the fork's reordered builds can be checked against upstream by hand.
            DiagnosticsInReportOrder = diagnostics.ToArray(),
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (traceDirectory is not null && recorder is not null)
        {
            // Like the host's trace, the client's half is best effort: the observation above is already written and
            // is what the comparison uses, so a trace directory that cannot be written must not fail the run.
            var failures = diagnostics.Count(diagnostic => diagnostic.StartsWith("Failure:", StringComparison.Ordinal));
            try
            {
                ClientTraceFile.Write(traceDirectory, solutionPath, (window.OpenStart, window.OpenEnd), recorder.Operations, solution, failures);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Client trace not written: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
