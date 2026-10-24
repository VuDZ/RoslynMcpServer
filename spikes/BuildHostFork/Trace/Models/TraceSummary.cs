namespace BuildHostFork.Trace;

/// <summary>
/// The publishable numbers of one traced load, written to report.json; names follow <see cref="PathNames"/>.
/// <see cref="UnreadableLines"/> and <see cref="SpansWithoutParent"/> show an incomplete host trace, for example of a
/// host killed before it wrote the line of an open span: its time would silently fall out of the breakdown.
/// </summary>
internal sealed record TraceSummary(
    double WallMs,
    int Projects,
    int Documents,
    int WorkspaceFailures,
    int Hosts,
    int UnreadableLines,
    int SpansWithoutParent,
    IReadOnlyDictionary<string, double> Breakdown,
    ClockCheckResult Clock,
    RepeatInventory Repeats,
    int GraphNodes,
    int GraphNodesBuilt,
    IReadOnlyDictionary<string, int> GraphEdges,
    int ReferencesWithoutOutputAssembly,
    int SolutionEntriesNotLoaded)
{
    /// <summary>True when every host line was read and every span's parent is in the trace.</summary>
    public bool Complete => UnreadableLines == 0 && SpansWithoutParent == 0;
}
