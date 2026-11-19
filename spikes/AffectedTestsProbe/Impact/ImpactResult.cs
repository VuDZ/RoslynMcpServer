namespace AffectedTestsProbe;

/// <summary>
/// The answer to one query. <see cref="Answer"/> is <c>reverse</c> when the walk finished,
/// <c>level0</c> when a limit fell back to level 0, and <c>missing</c> when no member was located.
/// <see cref="LocateMs"/> is the time to find the members, <see cref="SearchMs"/> the walk itself.
/// </summary>
internal sealed record ImpactResult(
    ImpactTarget Target,
    string Answer,
    string? Limit,
    long LocateMs,
    long SearchMs,
    int Visited,
    int Depth,
    IReadOnlyList<TestUnit> Predicted,
    IReadOnlyList<TestUnit> LevelZero,
    MemorySnapshot After);
