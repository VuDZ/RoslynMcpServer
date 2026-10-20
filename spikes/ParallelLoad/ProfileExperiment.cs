using System.Runtime.Versioning;
using System.Reflection;
using System.Text.Json;
using Microsoft.Build.Logging;
using ParallelLoad.Models;

namespace ParallelLoad;

[SupportedOSPlatform("windows")]
internal static class ProfileExperiment
{
    public static async Task<int> RunAsync(RunOptions options, string registration)
    {
        var directory = Path.GetDirectoryName(options.OutputPath)!;
        Directory.CreateDirectory(directory);
        var binlogs = Path.Combine(directory, Path.GetFileNameWithoutExtension(options.OutputPath) + "-binlogs");
        if (options.Mode == "profile" && Directory.Exists(binlogs))
        {
            throw new IOException("Use a fresh output path: the binlog directory already exists.");
        }

        var loadPath = Path.ChangeExtension(options.OutputPath, ".load.json");
        var loadResult = 0;
        if (options.Mode == "profile")
        {
            Directory.CreateDirectory(binlogs);
            loadResult = await LoadExperiment.RunAsync(options with
            {
                Mode = "baseline-binlog", OutputPath = loadPath, BinlogDirectory = binlogs,
            }, registration);
        }
        using var load = JsonDocument.Parse(File.ReadAllText(loadPath));
        var wall = load.RootElement.GetProperty("WallMs").GetDouble();
        var loadMs = load.RootElement.GetProperty("GroupMs")[0].GetDouble();
        var paths = Directory.GetFiles(binlogs, "*.binlog").Order(StringComparer.Ordinal).ToArray();
        CostProfile profile;
        try
        {
            if (loadResult != 0 || load.RootElement.GetProperty("Status").GetString() != "ok" || paths.Length == 0)
            {
                throw new InvalidOperationException("Baseline load failed or produced no binlogs; see the load report.");
            }

            var replay = new ProjectCostReplay(Path.GetDirectoryName(options.SolutionPath)!);
            foreach (var path in paths)
            {
                replay.Read(path);
            }

            var projects = replay.Capture(load.RootElement.GetProperty("Projects"));
            var complete = projects.All(project => project.WeightMs is not null && project.OwnBuildMs is not null);
            var intervalMs = replay.Sessions.Sum(session => session.FirstMs + session.WeightMs);
            var reconciliationError = Math.Abs(intervalMs - loadMs) / loadMs;
            if (reconciliationError > 0.1)
            {
                complete = false;
            }
            var requiredMarkup = projects.Where(project => project.Name is "FancyZonesEditor" or "ColorPickerUI");
            var isPowerToys = Path.GetFileName(options.SolutionPath).Equals("PowerToys.slnx", StringComparison.OrdinalIgnoreCase);
            if (requiredMarkup.Any(project => !project.Markup) || (isPowerToys && requiredMarkup.Count() != 2))
            {
                complete = false;
            }
            profile = new CostProfile(complete ? "ok" : "incomplete",
                complete ? null : "Missing build weights, incomplete required markup detection, or interval reconciliation exceeds 10%.",
                wall, ReaderVersion(), paths.Length, projects)
            {
                Sessions = replay.Sessions.ToArray(), LoadSolutionMs = loadMs, ReconciliationError = reconciliationError,
            };
        }
        catch (Exception ex)
        {
            // An unreadable binlog is an explicit experiment outcome, never a zero-cost successful profile.
            var error = ex.GetType().Name + ": " + ex.Message;
            error = error.Replace(Path.GetDirectoryName(options.SolutionPath)!, "<solution>", StringComparison.OrdinalIgnoreCase)
                .Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<user>", StringComparison.OrdinalIgnoreCase)
                .Replace(binlogs, "<binlogs>", StringComparison.OrdinalIgnoreCase);
            profile = new CostProfile("failed", error, wall, ReaderVersion(), paths.Length, []) { LoadSolutionMs = loadMs };
        }

        File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"profile status={profile.Status} projects={profile.Projects.Length} binlogs={paths.Length}");
        return profile.Status == "ok" ? 0 : 2;
    }

    private static string ReaderVersion() => typeof(BinaryLogReplayEventSource).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(BinaryLogReplayEventSource).Assembly.GetName().Version!.ToString();
}
