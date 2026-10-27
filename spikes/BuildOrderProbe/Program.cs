using System.Runtime.CompilerServices;
using System.Text.Json;
using BuildOrderProbe.Models;
using ParallelSearch;

namespace BuildOrderProbe;

/// <summary>
/// run: evaluates and builds a solution's projects in one <see cref="LoadOrder"/> and writes a <see cref="ProbeRun"/>.
/// compare: medians per order over the runs of a result folder, and whether every run built the same results.
/// evaluate: the root evaluations of the projects listed in a file, one full path per line, on dedicated threads with
/// no builds, the host's way in one shared project collection (shared) or one per thread (separate), or through
/// ProjectCollection.LoadProject in one shared collection (load-project); writes a <see cref="ScalingRun"/>.
/// scaling-compare: speedups and slowdowns over the evaluate runs of a result folder.
/// check-globs: reproduces why recording item elements before a build can change the later FileGlobs response.
/// sdk-cache: loads a solution with references first and times each kind of host evaluation in one evaluation context
/// mode, optionally attributing SDK resolution time from MSBuild's events; writes a <see cref="SdkCacheRun"/>.
/// sdk-cache-compare: medians, equivalence and attribution over the sdk-cache runs of a result folder.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "check-globs")
        {
            var solutionPath = Path.GetFullPath(args[1]);
            var outputPath = Path.GetFullPath(args[2]);
            MsBuildRegistration.Register(Path.GetDirectoryName(solutionPath)!);
            var result = CheckGlobs();
            var json = JsonSerializer.Serialize(result, _options);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, json);
            Console.WriteLine(json);
            return 0;
        }

        if (args.Length >= 4 && args[0] == "sdk-cache" && Enum.TryParse<SdkCacheMode>(args[3], ignoreCase: true, out var mode))
        {
            var solutionPath = Path.GetFullPath(args[1]);
            var events = args.Length >= 5 && args[4] == "events";
            MsBuildRegistration.Register(Path.GetDirectoryName(solutionPath)!);
            var run = SdkCache(solutionPath, mode, events);
            File.WriteAllText(Path.GetFullPath(args[2]), JsonSerializer.Serialize(run, _options));
            var phases = string.Join(", ", run.Phases.Select(phase => $"{phase.Key} {phase.Value.Count}x {phase.Value.Ms:0} ms"));
            Console.WriteLine($"sdk-cache {run.Mode}{(events ? " events" : "")}: wall {run.WallMs:0} ms; {phases}");
            return 0;
        }

        if (args.Length >= 2 && args[0] == "sdk-cache-compare")
        {
            var (summary, identical) = SdkCacheComparer.Run(Path.GetFullPath(args[1]));
            Console.WriteLine(summary);
            return identical ? 0 : 1;
        }

        if (args.Length >= 4 && args[0] == "run" && TryParseOrder(args[1], out var order))
        {
            var solutionPath = Path.GetFullPath(args[2]);
            var workers = args.Length >= 5 ? int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : Math.Max(1, Environment.ProcessorCount - 1);
            var window = args.Length >= 6 ? int.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : 0;
            MsBuildRegistration.Register(Path.GetDirectoryName(solutionPath)!);
            var run = Run(solutionPath, order, workers, window);
            File.WriteAllText(Path.GetFullPath(args[3]), JsonSerializer.Serialize(run, _options));
            Console.WriteLine($"{run.Order}: wall {run.WallMs:0} ms, {run.Builds} builds {run.BuildMs:0} ms, {run.RootEvaluations} root evaluations {run.RootEvaluationMs:0} ms, {run.EvaluationsDuringBuilds} evaluations during builds");
            return 0;
        }

        if (args.Length >= 6 && args[0] == "evaluate" && args[5] is "shared" or "separate" or "load-project")
        {
            var solutionPath = Path.GetFullPath(args[1]);
            var paths = File.ReadAllLines(Path.GetFullPath(args[2])).Where(line => line.Length > 0).ToArray();
            var workers = int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
            MsBuildRegistration.Register(Path.GetDirectoryName(solutionPath)!);
            var run = Evaluate(solutionPath, paths, workers, separateCollections: args[5] == "separate", useLoadProject: args[5] == "load-project");
            File.WriteAllText(Path.GetFullPath(args[3]), JsonSerializer.Serialize(run, _options));
            Console.WriteLine($"evaluate {args[5]} x{run.Workers}: wall {run.WallMs:0} ms, {run.Evaluations} evaluations {run.Steps.Sum(step => step.DurationMs):0} ms");
            return 0;
        }

        if (args.Length >= 2 && args[0] == "scaling-compare")
        {
            Console.WriteLine(ScalingComparer.Run(Path.GetFullPath(args[1])));
            return 0;
        }

        if (args.Length >= 2 && args[0] == "compare")
        {
            var (summary, identical) = ProbeComparer.Run(Path.GetFullPath(args[1]));
            Console.WriteLine(summary);
            return identical ? 0 : 1;
        }

        Console.Error.WriteLine("Usage: run client|references-first|evaluate-ahead|evaluate-ahead-paused|evaluate-closure|evaluate-closure-preorder|evaluate-solution-preorder SOLUTION OUTPUT_JSON [WORKERS] [WINDOW] | compare RESULT_DIRECTORY | " +
            "evaluate SOLUTION PATHS_FILE OUTPUT_JSON WORKERS shared|separate|load-project | scaling-compare RESULT_DIRECTORY | check-globs SOLUTION OUTPUT_JSON | " +
            "sdk-cache SOLUTION OUTPUT_JSON isolated|project|solution [events] | sdk-cache-compare RESULT_DIRECTORY");
        return 2;
    }

    // MSBuild types must not be loaded before MSBuildLocator registration; this method is JIT-compiled after it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ProbeRun Run(string solutionPath, LoadOrder order, int workers, int window)
        => OrderedLoad.Run(solutionPath, order, workers, window);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ScalingRun Evaluate(string solutionPath, IReadOnlyList<string> paths, int workers, bool separateCollections, bool useLoadProject)
        => EvaluationScaling.Run(solutionPath, paths, workers, separateCollections, useLoadProject);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SdkCacheRun SdkCache(string solutionPath, SdkCacheMode mode, bool events)
        => SdkCacheProbe.Run(solutionPath, mode, events);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RecordItemElementsRun CheckGlobs()
        => RecordItemElementsProbe.Run();

    private static bool TryParseOrder(string text, out LoadOrder order)
    {
        order = text switch
        {
            "client" => LoadOrder.Client,
            "references-first" => LoadOrder.ReferencesFirst,
            "evaluate-ahead" => LoadOrder.EvaluateAhead,
            "evaluate-ahead-paused" => LoadOrder.EvaluateAheadPaused,
            "evaluate-closure" => LoadOrder.EvaluateClosure,
            "evaluate-closure-preorder" => LoadOrder.EvaluateClosurePreorder,
            "evaluate-solution-preorder" => LoadOrder.EvaluateSolutionPreorder,
            _ => (LoadOrder)(-1),
        };
        return Enum.IsDefined(order);
    }

    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };
}
