using System.Text.Json;
using System.Text.Json.Nodes;

namespace BuildHostFork;

/// <summary>
/// Runs <see cref="ObservationComparer"/> on synthetic result folders whose verdict is known, including the cases
/// the independent reviews of task-00 found a comparer to accept: a fork equal only to the warmup run, a value seen
/// only in a late original run, diagnostic variance reported as identical, a project that appears in original
/// round 2 with other fields in the fork, a JSON null equal to an empty string, and results without process fields.
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
            (side, round, run) => run["Diagnostics"] = side == "fork" || round == 2 ? new JsonArray(Warning) : new JsonArray());
        failures += Expect(
            "diagnostic only in the fork",
            "different",
            false,
            Same("A"),
            Same("A"),
            (side, round, run) => run["Diagnostics"] = side == "fork" && round == 1 ? new JsonArray(Warning) : new JsonArray());
        failures += Expect("project that appears in original round 2, same fields in the fork", "natural variance only", true, ["A", null, "A", "A"], Same("A"));
        failures += Expect("project that appears in original round 2, other fields in the fork", "different", false, ["A", null, "A", "A"], Same("B"));
        failures += Expect(
            "field present in the fork only",
            "different",
            false,
            Same("A"),
            Same("A"),
            (side, round, run) => AddFieldIfFork(side, run, "Extra", "\"x\""));
        failures += Expect(
            "JSON null in the fork against an empty string",
            "different",
            false,
            Same("A"),
            Same("A"),
            (side, round, run) => run["Projects"]![ProjectKey]!["Field"] = side == "fork" ? null : "");
        failures += Expect("one measured round, same value", "identical", true, ["A", "A"], ["A", "A"]);
        failures += Expect("one measured round, other value", "different", false, ["A", "A"], ["B", "B"]);
        failures += Expect(
            "search answer differs only in the original warmup",
            "identical",
            false,
            Same("A"),
            Same("A"),
            (side, round, run) => SetAnswerIf(side == "original" && round == 0, run));
        failures += Expect(
            "search answer of a fork round differs",
            "different",
            false,
            Same("A"),
            Same("A"),
            (side, round, run) => SetAnswerIf(side == "fork" && round == 2, run));
        failures += Expect(
            "fork run not finished",
            "identical",
            false,
            Same("A"),
            Same("A"),
            (side, round, run) => run["Status"] = side == "fork" && round == 1 ? "failed" : "ok");
        failures += Expect(
            "BuildHost left running in the fork",
            "identical",
            false,
            Same("A"),
            Same("A"),
            (side, round, run) => run["RemainingBuildHosts"] = side == "fork" ? 1 : 0);
        failures += Expect(
            "fork BuildHost from another folder",
            "identical",
            false,
            Same("A"),
            Same("A"),
            (side, round, run) =>
            {
                if (side == "fork")
                {
                    run["BuildHostProcesses"]!["hosts"] = new JsonArray("netcore: /elsewhere/BuildHost-netcore");
                }
            });
        failures += Expect(
            "unreadable child process in the fork",
            "identical",
            false,
            Same("A"),
            Same("A"),
            (side, round, run) => run["BuildHostProcesses"]!["unidentified"] = side == "fork" ? 1 : 0);
        failures += Expect("results of an older spike without process fields", "identical", false, Same("A"), Same("A"), (side, round, run) => RemoveProcessFields(run));
        Console.WriteLine(failures == 0 ? "All comparer self-checks passed." : $"{failures} comparer self-check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static int Expect(
        string name,
        string verdict,
        bool equivalent,
        string?[] originalValues,
        string?[] forkValues,
        Action<string, int, JsonObject>? adjust = null)
    {
        var directory = Directory.CreateTempSubdirectory("buildhostfork-check-").FullName;
        try
        {
            Write(directory, "original", originalValues, adjust);
            Write(directory, "fork", forkValues, adjust);
            var (summary, actualEquivalent, _) = ObservationComparer.Run(directory);
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

    // One project with one field per round, absent where the value is null; round 0 is warmup.
    private static void Write(string directory, string side, string?[] values, Action<string, int, JsonObject>? adjust)
    {
        var expectedHost = $"netcore: /{side}/BuildHost-netcore";
        for (var round = 0; round < values.Length; round++)
        {
            var projects = new JsonObject();
            if (values[round] is { } value)
            {
                projects[ProjectKey] = new JsonObject { ["Field"] = JsonSerializer.Serialize(value) };
            }

            var run = new JsonObject
            {
                ["Status"] = "ok",
                ["BuildHost"] = side,
                ["ExpectedHost"] = expectedHost,
                ["WallMs"] = 1000.0,
                ["ClientCpuMs"] = 1000.0,
                ["ClientPeakWorkingSetMb"] = 100.0,
                ["BuildHostProcesses"] = new JsonObject
                {
                    ["count"] = 1,
                    ["cpuMs"] = 500.0,
                    ["peakWorkingSetMb"] = 50.0,
                    ["hosts"] = new JsonArray(expectedHost),
                    ["unidentified"] = 0,
                },
                ["RemainingBuildHosts"] = 0,
                ["FirstSearch"] = new JsonObject { ["resultSha256"] = "same", ["wallMs"] = 100.0 },
                ["Projects"] = projects,
                ["Diagnostics"] = new JsonArray(),
            };
            adjust?.Invoke(side, round, run);
            File.WriteAllText(Path.Combine(directory, $"{side}-round{round}.json"), run.ToJsonString());
        }
    }

    private static void AddFieldIfFork(string side, JsonObject run, string field, string value)
    {
        if (side == "fork")
        {
            run["Projects"]![ProjectKey]![field] = value;
        }
    }

    private static void SetAnswerIf(bool condition, JsonObject run)
    {
        if (condition)
        {
            run["FirstSearch"]!["resultSha256"] = "other";
        }
    }

    private static void RemoveProcessFields(JsonObject run)
    {
        run.Remove("ExpectedHost");
        run.Remove("RemainingBuildHosts");
        var processes = run["BuildHostProcesses"]!.AsObject();
        processes.Remove("hosts");
        processes.Remove("unidentified");
    }

    private static string?[] Same(string value) => [value, value, value, value];

    private const string ProjectKey = "/p/P.CSPROJ|P";

    private const string Warning = "Warning: sometimes";
}
