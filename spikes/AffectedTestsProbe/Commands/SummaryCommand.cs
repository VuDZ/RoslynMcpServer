using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

internal static class SummaryCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var path = Required(args);
        var configuration = CommandArgs.Value(args, "--configuration") ?? "Debug";
        var platform = CommandArgs.Value(args, "--platform") ?? "AnyCPU";
        var tests = CommandArgs.Values(args, "--test-project");
        using var workspace = await SolutionLoader.OpenAsync(path, configuration, platform, cancellationToken).ConfigureAwait(false);
        if (!workspace.CurrentSolution.Projects.Any())
        {
            Console.Error.WriteLine("No projects were loaded.");
            return 1;
        }

        var started = Stopwatch.GetTimestamp();
        var map = new ProjectMap(workspace.CurrentSolution, configuration, platform);
        var facts = await FactExtractor.ExtractAsync(workspace.CurrentSolution, map, cancellationToken).ConfigureAwait(false);
        var roots = await RootIndexBuilder.BuildAsync(workspace.CurrentSolution, map, tests, cancellationToken).ConfigureAwait(false);
        var reach = ForwardReachability.Compute(facts, roots, map);
        var elapsed = Stopwatch.GetElapsedTime(started);
        var commit = await GitRevision.TryHeadAsync(Path.GetDirectoryName(Path.GetFullPath(path))!, cancellationToken).ConfigureAwait(false);
        var disagreements = CountDisagreements(reach);
        var json = JsonSerializer.Serialize(new
        {
            solution = Path.GetFullPath(path),
            commit,
            configuration,
            platform,
            testProjects = tests,
            tests = roots.Units.Count,
            unmapped = roots.Unmapped.Count,
            nodes = facts.Facts.Count,
            edges = facts.Facts.Values.Sum(fact => fact.Calls.Count),
            chaMembers = reach.Cha.Count,
            rtaMembers = reach.Rta.Count,
            chaRtaDisagreements = disagreements,
            elapsedMs = (long)elapsed.TotalMilliseconds,
            peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64,
            rootLimitations = roots.Limitations,
            reachLimitations = reach.Limitations,
        }, new JsonSerializerOptions { WriteIndented = true });
        await WriteAsync(args, json, cancellationToken).ConfigureAwait(false);
        return roots.Units.Count == 0 ? 1 : 0;
    }

    private static int CountDisagreements(Reachability reach)
    {
        var keys = reach.Cha.Keys.Union(reach.Rta.Keys);
        var count = 0;
        foreach (var key in keys)
        {
        var left = reach.Cha.TryGetValue(key, out var cha) ? cha : new HashSet<TestUnit>();
        var right = reach.Rta.TryGetValue(key, out var rta) ? rta : new HashSet<TestUnit>();
            if (!left.SetEquals(right))
            {
                count++;
            }
        }

        return count;
    }

    private static string Required(string[] args) =>
        CommandArgs.Value(args, "--solution") ?? throw new InvalidOperationException("Pass --solution.");

    private static async Task WriteAsync(string[] args, string json, CancellationToken cancellationToken)
    {
        var output = CommandArgs.Value(args, "--out");
        if (output is null)
        {
            Console.WriteLine(json);
            return;
        }

        await File.WriteAllTextAsync(output, json, cancellationToken).ConfigureAwait(false);
        Console.Error.WriteLine(output);
    }
}
