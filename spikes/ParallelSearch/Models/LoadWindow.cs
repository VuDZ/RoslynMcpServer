namespace ParallelSearch.Models;

/// <summary>
/// Stopwatch timestamps around <c>OpenSolutionAsync</c> alone, without creating the MEF host and the workspace, which
/// the server does once per process.
/// </summary>
internal sealed class LoadWindow
{
    public long OpenStart { get; set; }

    public long OpenEnd { get; set; }
}
