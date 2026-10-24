using System.Text.Json;

namespace BuildHostFork;

/// <summary>
/// Runs <see cref="ObservationComparer"/> on synthetic result folders whose verdict is known, including the cases
/// the independent review of task-00 found the first comparer to accept: a fork equal only to the warmup run, a
/// value seen only in a late original run, and diagnostic variance reported as identical.
/// </summary>
internal static class ComparerSelfCheck
{
    public static int Run()
    {
        var failures = 0;
        failures += Expect("identical runs", "identical", true, Same("A"), Same("A"));
        failures += Expect("fork equal to the warmup only", "different", false, ["A", "B", "B", "B"], Same("A"));
        failures += Expect("natural variance between original rounds 1 and 2", "natural variance only", true, ["A", "A", "B", "A"], Same("B"));
        failures += Expect("value seen only in original round 3", "different", false, ["A", "A", "A", "C"], Same("C"));
        failures += Expect(
            "diagnostic seen in one of original rounds 1 and 2",
            "natural variance only",
            true,
            Same("A"),
            Same("A"),
            originalDiagnostics: [[], [], [Warning], []],
            forkDiagnostics: [[Warning], [Warning], [Warning], [Warning]]);
        failures += Expect("diagnostic only in the fork", "different", false, Same("A"), Same("A"), forkDiagnostics: [[], [Warning], [], []]);
        failures += Expect("BuildHost left running in the fork", "identical", false, Same("A"), Same("A"), forkRemaining: 1);
        failures += Expect("fork BuildHost from another folder", "identical", false, Same("A"), Same("A"), forkHost: "netcore: /elsewhere/BuildHost-netcore");
        Console.WriteLine(failures == 0 ? "All comparer self-checks passed." : $"{failures} comparer self-check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static int Expect(
        string name,
        string verdict,
        bool equivalent,
        string[] originalValues,
        string[] forkValues,
        string[][]? originalDiagnostics = null,
        string[][]? forkDiagnostics = null,
        int forkRemaining = 0,
        string? forkHost = null)
    {
        var directory = Directory.CreateTempSubdirectory("buildhostfork-check-").FullName;
        try
        {
            Write(directory, "original", originalValues, originalDiagnostics, 0, null);
            Write(directory, "fork", forkValues, forkDiagnostics, forkRemaining, forkHost);
            var (summary, actualEquivalent) = ObservationComparer.Run(directory);
            var passed = summary.Contains($"**{verdict}**", StringComparison.Ordinal) && actualEquivalent == equivalent;
            Console.WriteLine($"{(passed ? "pass" : "FAIL")}: {name}");
            if (!passed)
            {
                Console.WriteLine($"  expected {verdict}, equivalent {equivalent}; got equivalent {actualEquivalent}:");
                Console.WriteLine(summary);
            }

            return passed ? 0 : 1;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // One project with one field per round; round 0 is warmup.
    private static void Write(string directory, string side, string[] values, string[][]? diagnostics, int remaining, string? host)
    {
        var expectedHost = $"netcore: /{side}/BuildHost-netcore";
        for (var round = 0; round < values.Length; round++)
        {
            var run = new
            {
                Status = "ok",
                BuildHost = side,
                ExpectedHost = expectedHost,
                WallMs = 1000.0,
                ClientCpuMs = 1000.0,
                ClientPeakWorkingSetMb = 100.0,
                BuildHostProcesses = new { count = 1, cpuMs = 500.0, peakWorkingSetMb = 50.0, hosts = new[] { host ?? expectedHost }, unidentified = 0 },
                RemainingBuildHosts = remaining,
                FirstSearch = new { resultSha256 = "same", wallMs = 100.0 },
                Projects = new Dictionary<string, Dictionary<string, string>>
                {
                    ["/p/P.CSPROJ|P"] = new() { ["Field"] = JsonSerializer.Serialize(values[round]) },
                },
                Diagnostics = diagnostics?[round] ?? [],
            };
            File.WriteAllText(Path.Combine(directory, $"{side}-round{round}.json"), JsonSerializer.Serialize(run));
        }
    }

    private static string[] Same(string value) => [value, value, value, value];

    private const string Warning = "Warning: sometimes";
}
