using BuildOrderProbe.Models;

namespace BuildOrderProbe;

/// <summary>
/// How a run's root evaluations lay on the clock: how much of their time overlapped design-time builds, how many ran
/// at once, and how much longer each took than the same project's evaluation in a reference run. Together they tell
/// evaluations that waited on each other (high concurrency, high slowdown, little gain) from evaluations that ran
/// alongside the builds.
/// </summary>
internal static class StepMetrics
{
    /// <summary>Share of the evaluations' summed time that overlapped some design-time build.</summary>
    public static double EvaluationOverlapWithBuilds(ProbeRun run)
    {
        var builds = Union(run.BuildSteps);
        var total = run.EvaluationSteps.Sum(step => step.DurationMs);
        return total == 0 ? 0 : run.EvaluationSteps.Sum(step => Overlap(step, builds)) / total;
    }

    /// <summary>
    /// Wall time in which the build thread was not building: waiting for evaluations or running them itself. It is
    /// the part of the critical path that evaluating ahead can remove.
    /// </summary>
    public static double NotBuildingMs(ProbeRun run)
        => run.WallMs - run.BuildSteps.Sum(step => step.DurationMs);

    /// <summary>Root evaluations the build thread ran itself, because no worker had started them.</summary>
    public static int EvaluationsOnBuildThread(ProbeRun run)
        => run.BuildSteps.Count == 0 ? 0 : run.EvaluationSteps.Count(step => step.Thread == run.BuildSteps[0].Thread);

    /// <summary>The evaluations' summed time over the time at least one of them ran: the mean number at once.</summary>
    public static double MeanConcurrentEvaluations(ProbeRun run)
    {
        var covered = Union(run.EvaluationSteps).Sum(interval => interval.End - interval.Start);
        return covered == 0 ? 0 : run.EvaluationSteps.Sum(step => step.DurationMs) / covered;
    }

    /// <summary>
    /// Median, over the run's projects of one kind, of each root evaluation's duration against the same project's in
    /// the reference durations; NaN when the run has no such project.
    /// </summary>
    public static double EvaluationSlowdown(ProbeRun run, IReadOnlyDictionary<string, double> reference, bool multiTargeting)
    {
        var ratios = run.EvaluationSteps
            .Where(step => step.MultiTargeting == multiTargeting && step.Thread >= 0 && reference.TryGetValue(step.Path, out var baseline) && baseline > 0)
            .Select(step => step.DurationMs / reference[step.Path])
            .Order()
            .ToArray();
        return ratios.Length == 0 ? double.NaN : ratios[ratios.Length / 2];
    }

    /// <summary>Per project, the median root evaluation duration over the given runs.</summary>
    public static IReadOnlyDictionary<string, double> EvaluationDurations(IEnumerable<ProbeRun> runs)
        => runs.SelectMany(run => run.EvaluationSteps)
            .Where(step => step.Thread >= 0)
            .GroupBy(step => step.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var sorted = group.Select(step => step.DurationMs).Order().ToArray();
                    return sorted[sorted.Length / 2];
                },
                StringComparer.OrdinalIgnoreCase);

    private static List<(double Start, double End)> Union(IEnumerable<TimedStep> steps)
    {
        var merged = new List<(double Start, double End)>();
        foreach (var step in steps.OrderBy(step => step.StartMs))
        {
            if (merged.Count > 0 && step.StartMs <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, step.EndMs));
            }
            else
            {
                merged.Add((step.StartMs, step.EndMs));
            }
        }

        return merged;
    }

    private static double Overlap(TimedStep step, List<(double Start, double End)> intervals)
        => intervals.Sum(interval => Math.Max(0, Math.Min(step.EndMs, interval.End) - Math.Max(step.StartMs, interval.Start)));
}
