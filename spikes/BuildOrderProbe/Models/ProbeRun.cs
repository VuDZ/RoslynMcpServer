namespace BuildOrderProbe.Models;

/// <summary>
/// One run of one order in a fresh process. <see cref="RootEvaluationMs"/> and <see cref="BuildMs"/> add up the
/// durations of each evaluation and build, which overlap in <see cref="LoadOrder.EvaluateAhead"/>; only
/// <see cref="WallMs"/> is elapsed time. <see cref="Fingerprints"/> maps a project path and target framework to a hash
/// of what its design-time build produced; the keys are full paths, so the file stays on the machine.
/// </summary>
internal sealed record ProbeRun(
    string Order,
    int Workers,
    int LogicalProcessors,
    double WallMs,
    int RootEvaluations,
    double RootEvaluationMs,
    int Builds,
    double BuildMs,
    int EvaluationsDuringBuilds,
    int ProjectsNotEvaluated,
    IReadOnlyDictionary<string, string> Fingerprints);
