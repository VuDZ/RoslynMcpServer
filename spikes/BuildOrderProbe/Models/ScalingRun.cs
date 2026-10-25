namespace BuildOrderProbe.Models;

/// <summary>
/// One run of <see cref="EvaluationScaling"/> in a fresh process: the root evaluations of a fixed list of projects on
/// <see cref="Workers"/> threads, in one project collection or one per thread, with no builds; the host's way of
/// evaluating, or <see cref="UsesLoadProject"/> for ProjectCollection.LoadProject. <see cref="Steps"/> hold full paths,
/// so the file stays on the machine.
/// </summary>
internal sealed record ScalingRun(
    int Workers,
    bool SeparateCollections,
    bool UsesLoadProject,
    int LogicalProcessors,
    double WallMs,
    int Evaluations,
    int ProjectsNotEvaluated,
    IReadOnlyList<TimedStep> Steps);
