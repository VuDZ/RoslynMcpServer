namespace BuildOrderProbe.Models;

/// <summary>
/// The order in which a run evaluates and builds the projects of a solution.
/// </summary>
internal enum LoadOrder
{
    /// <summary>
    /// The 5.9.0 loader's order: a project is evaluated and built, then the projects it references are loaded, depth
    /// first. A design-time build that reaches a referenced project not built yet makes MSBuild evaluate it again.
    /// </summary>
    Client,

    /// <summary>
    /// The referenced projects are evaluated and built before the project, so its design-time build finds their build
    /// configurations in the build manager (BuildHost/task-02, part A).
    /// </summary>
    ReferencesFirst,

    /// <summary>
    /// The order of <see cref="ReferencesFirst"/>, and as soon as a project is evaluated, the root evaluations of the
    /// projects it references start on worker threads while builds go on one at a time (BuildHost/task-02, part B).
    /// </summary>
    EvaluateAhead,

    /// <summary>
    /// <see cref="EvaluateAhead"/>, except that no worker starts an evaluation while a multi-targeting project builds;
    /// evaluations already running finish. On Roslyn.slnx builds of multi-targeting projects, which reevaluate the
    /// project for each target framework, stood still while root evaluations ran beside them; this order checks
    /// whether keeping evaluations away from just those builds gives the gain of part B back (BuildHost/task-02).
    /// </summary>
    EvaluateAheadPaused,

    /// <summary>
    /// The order of <see cref="ReferencesFirst"/>, and every finished evaluation queues the evaluations of the projects
    /// it references, so the whole reference closure is evaluated ahead by worker threads, deepest first. When the
    /// builds reach a project whose evaluation has not started, the build thread evaluates it itself.
    /// </summary>
    EvaluateClosure,

    /// <summary>
    /// <see cref="EvaluateClosure"/>, except that a worker takes the queued project the build thread will ask for
    /// first: the first one not started in the depth-first pre-order of the traversal over the references known so
    /// far. With the most recent first, the build thread found most projects on its own path not started and
    /// evaluated them itself (about 200 of 318 on Roslyn.slnx).
    /// </summary>
    EvaluateClosurePreorder,
}
