using System.Runtime.CompilerServices;
using System.Text.Json;
using BuildOrderProbe.Models;
using ParallelSearch;

namespace BuildOrderProbe;

/// <summary>
/// run: evaluates and builds a solution's projects in one <see cref="LoadOrder"/> and writes a <see cref="ProbeRun"/>.
/// compare: medians per order over the runs of a result folder, and whether every run built the same results.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length >= 4 && args[0] == "run" && TryParseOrder(args[1], out var order))
        {
            var solutionPath = Path.GetFullPath(args[2]);
            var workers = args.Length >= 5 ? int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : Math.Max(1, Environment.ProcessorCount - 1);
            MsBuildRegistration.Register(Path.GetDirectoryName(solutionPath)!);
            var run = Run(solutionPath, order, workers);
            File.WriteAllText(Path.GetFullPath(args[3]), JsonSerializer.Serialize(run, _options));
            Console.WriteLine($"{run.Order}: wall {run.WallMs:0} ms, {run.Builds} builds {run.BuildMs:0} ms, {run.RootEvaluations} root evaluations {run.RootEvaluationMs:0} ms, {run.EvaluationsDuringBuilds} evaluations during builds");
            return 0;
        }

        if (args.Length >= 2 && args[0] == "compare")
        {
            var (summary, identical) = ProbeComparer.Run(Path.GetFullPath(args[1]));
            Console.WriteLine(summary);
            return identical ? 0 : 1;
        }

        Console.Error.WriteLine("Usage: run client|references-first|evaluate-ahead SOLUTION OUTPUT_JSON [WORKERS] | compare RESULT_DIRECTORY");
        return 2;
    }

    // MSBuild types must not be loaded before MSBuildLocator registration; this method is JIT-compiled after it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ProbeRun Run(string solutionPath, LoadOrder order, int workers)
        => OrderedLoad.Run(solutionPath, order, workers);

    private static bool TryParseOrder(string text, out LoadOrder order)
    {
        order = text switch
        {
            "client" => LoadOrder.Client,
            "references-first" => LoadOrder.ReferencesFirst,
            "evaluate-ahead" => LoadOrder.EvaluateAhead,
            _ => (LoadOrder)(-1),
        };
        return Enum.IsDefined(order);
    }

    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };
}
