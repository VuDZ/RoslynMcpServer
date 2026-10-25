namespace BuildOrderProbe.Models;

/// <summary>
/// One run of one order in a fresh process. <see cref="RootEvaluationMs"/> and <see cref="BuildMs"/> add up the
/// durations of each evaluation and build, which overlap in <see cref="LoadOrder.EvaluateAhead"/>; only
/// <see cref="WallMs"/> is elapsed time, and <see cref="EvaluationSteps"/> and <see cref="BuildSteps"/> show how the
/// work lay on the clock. <see cref="Fingerprints"/> maps a project path and target framework to a hash of what its
/// design-time build produced. <see cref="Window"/> is how many solution entries ahead of the build thread
/// <see cref="LoadOrder.EvaluateSolutionPreorder"/> queues, 0 for all of them at the start. <see cref="GcPauseMs"/>,
/// <see cref="GcCollections"/> (generations 0, 1 and 2) and <see cref="PeakWorkingSetMb"/> cover the run's wall time,
/// the peak the whole process. Paths are full paths, so the file stays on the machine.
/// </summary>
internal sealed record ProbeRun(
    string Order,
    int Workers,
    int Window,
    bool ServerGc,
    int LogicalProcessors,
    double WallMs,
    int RootEvaluations,
    double RootEvaluationMs,
    int Builds,
    double BuildMs,
    int EvaluationsDuringBuilds,
    int ProjectsNotEvaluated,
    double GcPauseMs,
    IReadOnlyList<int> GcCollections,
    double PeakWorkingSetMb,
    IReadOnlyDictionary<string, string> Fingerprints,
    IReadOnlyList<TimedStep> EvaluationSteps,
    IReadOnlyList<TimedStep> BuildSteps);
