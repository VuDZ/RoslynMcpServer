using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynMcpServer.Services;

/// <summary>
/// Removes <see cref="UnresolvedAnalyzerReference"/> stubs from the projects of a <see cref="Solution"/>.
/// </summary>
/// <remarks>
/// MSBuildWorkspace adds such a stub for an analyzer it could not resolve (the analyzer DLL is missing on disk).
/// Roslyn 5.9.0 cannot compute a project checksum that contains the stub: <c>SerializerService.CreateChecksum</c>
/// throws (<c>Unexpected value 'UnresolvedAnalyzerReference'</c>).
/// The checksum is computed by <c>DependentTypeFinder</c> for every solution-wide <c>SymbolFinder</c> search that
/// builds the dependent-type index, so a single stub breaks <c>find_usages</c>, <c>find_symbol_references</c>,
/// <c>get_call_graph</c>, <c>find_implementations</c> and <c>rename_symbol</c> for the whole solution,
/// regardless of which project the searched symbol belongs to.
/// <para>
/// A stub provides no analyzer (its assembly never loaded), so removing it changes no diagnostics;
/// the project checksum becomes computable again. The sanitizer returns a new snapshot only — it must not be
/// written back with <c>TryApplyChanges</c> or published over the raw workspace solution.
/// </para>
/// </remarks>
public static class WorkspaceAnalyzerSanitizer
{
    /// <summary>
    /// Removes every <see cref="UnresolvedAnalyzerReference"/> from every project of <paramref name="solution"/>.
    /// Returns the input instance unchanged (same reference, no new allocated <see cref="Solution"/>)
    /// when nothing was removed; otherwise a new solution with the cleaned projects and the total removed count.
    /// </summary>
    public static (Solution Solution, int RemovedCount) RemoveUnresolvedAnalyzers(Solution solution)
    {
        ArgumentNullException.ThrowIfNull(solution);

        var updated = solution;
        var removed = 0;
        foreach (var project in solution.Projects)
        {
            var bad = project.AnalyzerReferences
                .Where(static r => r is UnresolvedAnalyzerReference)
                .ToList();
            if (bad.Count == 0)
            {
                continue;
            }

            var good = project.AnalyzerReferences
                .Where(static r => r is not UnresolvedAnalyzerReference)
                .ToList();
            updated = updated.WithProjectAnalyzerReferences(project.Id, good);
            removed += bad.Count;
        }

        return (updated, removed);
    }

    /// <summary>
    /// True when <paramref name="ex"/> is the Roslyn 5.9.0 project-checksum failure on an
    /// <see cref="UnresolvedAnalyzerReference"/> (the message carries the type name of the unexpected value).
    /// </summary>
    public static bool IsUnresolvedAnalyzerError(Exception ex) =>
        ex is InvalidOperationException
        && ex.Message.Contains("UnresolvedAnalyzerReference", StringComparison.Ordinal);

    /// <summary>
    /// Runs <paramref name="search"/> on <paramref name="solution"/>. When the Roslyn checksum crash on an
    /// unresolved analyzer reference occurs (a stub appeared after the last sanitization), re-fetches a sanitized
    /// solution from <paramref name="resanitize"/> and retries the search once on it.
    /// Re-throws the original exception when <paramref name="resanitize"/> returns the same solution
    /// (nothing new can be sanitized) or <see langword="null"/>.
    /// </summary>
    public static async Task<(T Value, bool Retried)> WithSanitizedRetryAsync<T>(
        Func<Solution, Task<T>> search,
        Func<Solution?> resanitize,
        Solution solution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(resanitize);
        ArgumentNullException.ThrowIfNull(solution);

        try
        {
            return (await search(solution).ConfigureAwait(false), false);
        }
        catch (InvalidOperationException ex) when (IsUnresolvedAnalyzerError(ex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retrySolution = resanitize();
            if (retrySolution is null || ReferenceEquals(retrySolution, solution))
            {
                throw;
            }

            return (await search(retrySolution).ConfigureAwait(false), true);
        }
    }
}
