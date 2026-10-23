using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ParallelSearch;

namespace LoadTimeline;

/// <summary>
/// capture: opens a solution like the server does and keeps the binlog of the design-time builds.
/// analyze: turns the binlogs of a capture into a timeline report.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 3 || args[0] is not ("capture" or "analyze" or "evaluate"))
        {
            Console.Error.WriteLine("Usage: capture SOLUTION OUT_DIRECTORY | analyze OUT_DIRECTORY ROOT | evaluate SOLUTION OUT_DIRECTORY");
            return 2;
        }

        if (args[0] == "evaluate")
        {
            var solutionPath = Path.GetFullPath(args[1]);
            var outDirectory = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(outDirectory);
            MsBuildRegistration.Register(Path.GetDirectoryName(solutionPath)!);
            Evaluate(solutionPath, outDirectory);
            return 0;
        }

        if (args[0] == "capture")
        {
            var solutionPath = Path.GetFullPath(args[1]);
            var outDirectory = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(outDirectory);
            MsBuildRegistration.Register(Path.GetDirectoryName(solutionPath)!);
            await CaptureAsync(solutionPath, outDirectory).ConfigureAwait(false);
            return 0;
        }

        // Replay needs the MSBuild assemblies too; the SDK of this process is enough to read the format.
        MsBuildRegistration.Register(Path.GetFullPath(args[2]));
        Analyze(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
        return 0;
    }

    // MSBuild types must not be loaded before MSBuildLocator registration; these methods are JIT-compiled after it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task CaptureAsync(string solutionPath, string outDirectory)
    {
        var logger = new Microsoft.Build.Logging.BinaryLogger
        {
            Parameters = Path.Combine(outDirectory, "load.binlog"),
            Verbosity = Microsoft.Build.Framework.LoggerVerbosity.Diagnostic,
        };
        var watch = Stopwatch.StartNew();
        var (_, load) = await SolutionLoader.LoadAsync(solutionPath, logger).ConfigureAwait(false);
        watch.Stop();
        File.WriteAllText(
            Path.Combine(outDirectory, "capture.json"),
            JsonSerializer.Serialize(new { clientLoadMs = watch.Elapsed.TotalMilliseconds, load }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Evaluate(string solutionPath, string outDirectory)
    {
        File.WriteAllText(Path.Combine(outDirectory, "evaluation.md"), EvaluationProbe.Run(solutionPath));
        Console.WriteLine(Path.Combine(outDirectory, "evaluation.md"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Analyze(string outDirectory, string root)
    {
        var reader = new TimelineReader();
        var binlogs = Directory.GetFiles(outDirectory, "*.binlog").Order(StringComparer.Ordinal).ToArray();
        for (var index = 0; index < binlogs.Length; index++)
        {
            reader.Read(binlogs[index], index, root);
        }

        double? clientLoadMs = null;
        var capture = Path.Combine(outDirectory, "capture.json");
        if (File.Exists(capture))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(capture));
            clientLoadMs = document.RootElement.GetProperty("clientLoadMs").GetDouble();
        }

        File.WriteAllText(Path.Combine(outDirectory, "timeline.md"), TimelineReport.Write(reader, clientLoadMs));
        Console.WriteLine(Path.Combine(outDirectory, "timeline.md"));
    }
}
