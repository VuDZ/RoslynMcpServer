using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using ParallelSearch;

namespace BuildHostFork;

/// <summary>
/// Loads a solution and either lets it end on its own (load), cancels it (cancel), or kills the BuildHost
/// (kill-host) a given time after the first BuildHost appears. The outcome is recorded instead of thrown, so a
/// broken fork shows up as a failure in the report and not as a missing run, and the BuildHost processes must
/// exit after the load ends. The script, not this class, decides whether an outcome is the expected one.
/// </summary>
internal static class FailureScenario
{
    public static async Task RunAsync(string mode, string solutionPath, string outputPath, int delayMs)
    {
        var diagnostics = new List<string>();
        using var cancellation = new CancellationTokenSource();
        using var sampler = new BuildHostSampler();
        var watch = Stopwatch.StartNew();
        var firstHostAt = sampler.FirstHost.ContinueWith(_ => watch.Elapsed.TotalMilliseconds, TaskScheduler.Default);
        var load = SolutionLoader.LoadAsync(
            solutionPath, diagnostics: diagnostics, properties: Program.SolutionProperties(solutionPath), cancellationToken: cancellation.Token);
        var disturbance = DisturbAsync(mode, sampler, load, cancellation, delayMs, watch);

        // A client that waits forever for a killed host is a result too, not a reason to hang the script.
        var finished = await Task.WhenAny(load, Task.Delay(_loadTimeout)).ConfigureAwait(false) == load;
        var loadMs = watch.Elapsed.TotalMilliseconds;
        var buildHosts = sampler.Stop();
        var remaining = await sampler.WaitForExitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        var report = new
        {
            Mode = mode,
            DelayMs = delayMs,
            Outcome = finished ? DescribeOutcome(load) : $"no result after {_loadTimeout.TotalSeconds} s",
            Error = finished && load.IsFaulted ? load.Exception!.ToString() : null,
            FirstHostAtMs = firstHostAt.IsCompletedSuccessfully ? firstHostAt.Result : (double?)null,
            DisturbedAtMs = disturbance.IsCompleted ? await disturbance.ConfigureAwait(false) : null,
            LoadMs = loadMs,
            Projects = load.IsCompletedSuccessfully ? load.Result.Solution.ProjectIds.Count : (int?)null,
            Documents = load.IsCompletedSuccessfully ? load.Result.Solution.Projects.Sum(project => project.DocumentIds.Count) : (int?)null,
            ExpectedHost = Program.ExpectedHost,
            BuildHostProcesses = buildHosts,
            RemainingBuildHosts = remaining.Length,
            Diagnostics = Snapshot(diagnostics),
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    // Returns when the load was disturbed, in ms from its start, or null when it ended first.
    private static async Task<double?> DisturbAsync(
        string mode,
        BuildHostSampler sampler,
        Task load,
        CancellationTokenSource cancellation,
        int delayMs,
        Stopwatch watch)
    {
        if (mode == "load" || await Task.WhenAny(sampler.FirstHost, load).ConfigureAwait(false) != sampler.FirstHost)
        {
            return null;
        }

        await Task.WhenAny(Task.Delay(delayMs), load).ConfigureAwait(false);
        if (load.IsCompleted)
        {
            return null;
        }

        if (mode == "cancel")
        {
            cancellation.Cancel();
            return watch.Elapsed.TotalMilliseconds;
        }

        return sampler.Kill(await sampler.FirstHost.ConfigureAwait(false)) ? watch.Elapsed.TotalMilliseconds : null;
    }

    private static string DescribeOutcome(Task<(Solution Solution, object Load)> load)
    {
        if (load.IsCompletedSuccessfully)
        {
            return "completed";
        }

        if (load.IsCanceled)
        {
            return "canceled";
        }

        var error = load.Exception!.InnerExceptions[0];
        return $"failed: {error.GetType().FullName}: {error.Message.Split('\n')[0].Trim()}";
    }

    private static string[] Snapshot(List<string> diagnostics)
    {
        // The workspace may still report diagnostics from a load that did not finish.
        lock (diagnostics)
        {
            return diagnostics.Order(StringComparer.Ordinal).ToArray();
        }
    }

    private static readonly TimeSpan _loadTimeout = TimeSpan.FromMinutes(5);
}
