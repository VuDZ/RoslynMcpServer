namespace BuildHostFork.Trace;

/// <summary>The publishable numbers of one traced load, written to report.json; names follow <see cref="PathNames"/>.</summary>
internal sealed record TraceSummary(
    double WallMs,
    int Projects,
    int Documents,
    int WorkspaceFailures,
    int Hosts,
    IReadOnlyDictionary<string, double> Breakdown,
    ClockCheckResult Clock,
    RepeatInventory Repeats,
    int GraphNodes,
    int GraphNodesBuilt,
    IReadOnlyDictionary<string, int> GraphEdges,
    int ReferencesWithoutOutputAssembly,
    int SolutionEntriesNotLoaded);
