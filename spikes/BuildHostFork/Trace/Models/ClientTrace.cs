namespace BuildHostFork.Trace;

/// <summary>
/// The client side of a traced load: the load window and the loader's operations on the Stopwatch clock, the solution
/// entries, and what the workspace holds afterwards.
/// </summary>
internal sealed record ClientTrace(
    string Solution,
    long Frequency,
    long LoadStart,
    long LoadEnd,
    IReadOnlyList<ClientOperation> Operations,
    IReadOnlyList<SolutionEntry> SolutionEntries,
    int Projects,
    int Documents,
    int WorkspaceFailures);
