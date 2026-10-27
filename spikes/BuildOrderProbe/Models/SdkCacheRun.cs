namespace BuildOrderProbe.Models;

/// <summary>
/// One <see cref="SdkCacheProbe"/> run in a fresh process. <see cref="Phases"/> maps a kind of host step
/// (<see cref="SdkCacheProbe.RootEvaluation"/>, <see cref="SdkCacheProbe.TargetFrameworkEvaluation"/>,
/// <see cref="SdkCacheProbe.Globs"/>, <see cref="SdkCacheProbe.DesignTimeBuild"/>) to its steps. With
/// <see cref="Events"/>, <see cref="SdkResolutionMs"/> maps the same kinds to the time spent in SDK resolution inside
/// them, <see cref="ResolverMs"/> a resolver's name to the time spent in it, and the counts tell how many SDK
/// references were resolved and how many of them came from the resolver service's cache; without events they are
/// empty and zero. <see cref="Fingerprints"/> maps a step and project path (and target framework) to a hash of what
/// it produced. Paths are full paths, so the file stays on the machine.
/// </summary>
internal sealed record SdkCacheRun(
    string Mode,
    bool Events,
    string? MsBuildVersion,
    int LogicalProcessors,
    double WallMs,
    int Projects,
    int MultiTargetingProjects,
    IReadOnlyDictionary<string, PhaseTotal> Phases,
    IReadOnlyDictionary<string, double> SdkResolutionMs,
    IReadOnlyDictionary<string, double> ResolverMs,
    int SdkResolutions,
    int CachedSdkResolutions,
    double PeakWorkingSetMb,
    IReadOnlyDictionary<string, string> Fingerprints);
