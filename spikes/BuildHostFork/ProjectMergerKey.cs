namespace ParallelLoad;

/// <summary>
/// The project key of spikes/ParallelLoad, so its SolutionObservation compiles here unchanged; the rest of
/// ProjectMerger (partitioned loads) is not part of this spike.
/// </summary>
internal static class ProjectMerger
{
    public static string Key(string? path, string name) => (path ?? "").ToUpperInvariant() + "|" + name;
}
