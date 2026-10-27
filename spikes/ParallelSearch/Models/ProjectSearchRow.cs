namespace ParallelSearch.Models;

/// <summary>
/// One project of a search, like the server's <c>workspace_search_project</c> line, plus the time the
/// project waited for a free slot after the search started.
/// </summary>
internal sealed record ProjectSearchRow(
    int Index,
    string? Project,
    string Name,
    int Documents,
    double WaitMs,
    double ElapsedMs,
    int? Matches,
    string Outcome);
