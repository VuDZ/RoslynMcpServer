using ParallelLoad.Models;

namespace ParallelLoad;

internal static class ProfileChecks
{
    public static int Run()
    {
        // A/B and B/C share different markup projects: the transitive bundle must keep all three roots together.
        var projects = new[]
        {
            Project("A", 9, false, "X", "Core"), Project("B", 7, false, "X", "Y", "Core"),
            Project("C", 5, false, "Y", "Core"), Project("D", 30, false, "Core"),
            Project("X", 10, true), Project("Y", 10, true), Project("Core", 2, false),
            Project("Cycle1", 1, false, "Cycle2"), Project("Cycle2", 1, false, "Cycle1"),
        };
        var profile = new CostProfile("ok", null, 100, "test", 1, projects);
        foreach (var workers in new[] { 2, 4, 8 })
        {
            var plan = ProfilePartition.Create(profile, workers);
            Require(plan.RepeatedMarkup.Length == 0, "Markup may appear in only one closure.");
            Require(plan.Closures.SelectMany(group => group).Distinct().Count() == projects.Length,
                "Coverage must include cycles without incoming roots.");
            Require(plan.RequestedProjects.Any(group => new[] { "A", "B", "C" }.All(group.Contains)),
                "Transitively overlapping markup roots must be bundled.");
            Require(plan.SharedCore.Contains("Core"), "A non-markup core may be repeated.");
            Require(plan.Status == "uncalibrated-model", "Planning must not claim that an absent model passed calibration.");
        }

        RequireRejected(profile with { Status = "failed" });
        RequireRejected(profile with { Projects = [Project("Missing", 1, false, "Absent")] });
        RequireRejected(profile with { Projects = [projects[0] with { WeightMs = null }] });
        RequireRejected(profile with { Projects = [projects[0] with { WeightMs = double.NaN }] });
        RequireRejected(profile with { Projects = [projects[0] with { WeightMs = -1 }] });
        ProfileReplayChecks.Run();
        Console.WriteLine("Profile checks passed: transitive markup ownership, shared core, cycles, coverage, invalid profiles.");
        return 0;
    }

    private static ProjectCost Project(string name, double cost, bool markup, params string[] references) =>
        new(name, name, name, references, null, cost, 1, markup, markup ? ["test"] : []) { WeightMs = cost };

    private static void RequireRejected(CostProfile profile)
    {
        try
        {
            ProfilePartition.Create(profile, 2);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException("An unusable profile must be rejected.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
