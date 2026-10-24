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
    /// The order of <see cref="ReferencesFirst"/>, and every finished evaluation queues the evaluations of the projects
    /// it references, so the whole reference closure is evaluated ahead by worker threads, deepest first. When the
    /// builds reach a project whose evaluation has not started, the build thread evaluates it itself.
    /// </summary>
    EvaluateClosure,
}
