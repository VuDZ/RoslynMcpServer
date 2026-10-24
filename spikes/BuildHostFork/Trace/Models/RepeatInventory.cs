namespace BuildHostFork.Trace;

/// <summary>
/// Work that happened more than once for the same input, and reuse that was observed. A repeated root evaluation of a
/// path in another host is listed too: its effective context can differ, so it is a candidate, not proven waste.
/// </summary>
internal sealed record RepeatInventory(
    int PathsEvaluatedMoreThanOnce,
    double RepeatedEvaluationMs,
    int CachedProjectLoads,
    int RepeatedBuildsOfSamePathAndTfm,
    double RepeatedBuildMs,
    int OutputPathQueries,
    int OutputPathQueriesWithCachedProject);
