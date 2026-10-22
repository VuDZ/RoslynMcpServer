using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ParallelSearch.Models;

namespace ParallelSearch;

/// <summary>
/// Name-based source declaration search over every project of a solution, as the server's
/// <c>SymbolDeclarationResolver</c> runs it: <see cref="SymbolFinder.FindSourceDeclarationsAsync(Project, string, bool, SymbolFilter, CancellationToken)"/>
/// per project with <c>ignoreCase: true</c>.
/// </summary>
internal static class DeclarationSearch
{
    /// <summary>
    /// <paramref name="degreeOfParallelism"/> 1 is the server's sequential loop; larger values keep up to that many
    /// projects in flight. The result order is the same for every degree: projects are merged by their index.
    /// </summary>
    public static Task<SearchOutcome> SearchAsync(
        Solution solution,
        string name,
        SymbolFilter filter,
        int degreeOfParallelism,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(degreeOfParallelism, 1);
        return degreeOfParallelism == 1
            ? SearchSequentiallyAsync(solution, name, filter, cancellationToken)
            : SearchInParallelAsync(solution, name, filter, degreeOfParallelism, cancellationToken);
    }

    private static async Task<SearchOutcome> SearchSequentiallyAsync(
        Solution solution,
        string name,
        SymbolFilter filter,
        CancellationToken cancellationToken)
    {
        var projects = solution.ProjectIds.Select(solution.GetProject).ToList();
        var found = new IReadOnlyList<ISymbol>?[projects.Count];
        var rows = new ProjectSearchRow?[projects.Count];
        var started = Stopwatch.GetTimestamp();
        for (var index = 0; index < projects.Count; index++)
        {
            found[index] = await SearchProjectAsync(
                    projects[index], index, name, filter, started, rows, cancellationToken)
                .ConfigureAwait(false);
        }

        return Merge(found, rows);
    }

    private static async Task<SearchOutcome> SearchInParallelAsync(
        Solution solution,
        string name,
        SymbolFilter filter,
        int degreeOfParallelism,
        CancellationToken cancellationToken)
    {
        var projects = solution.ProjectIds.Select(solution.GetProject).ToList();
        var found = new IReadOnlyList<ISymbol>?[projects.Count];
        var rows = new ProjectSearchRow?[projects.Count];
        var started = Stopwatch.GetTimestamp();
        var next = -1;
        ExceptionDispatchInfo? firstFailure = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async Task RunWorkerAsync()
        {
            while (true)
            {
                // Projects start in solution order, as in the sequential loop; only completion overlaps.
                var index = Interlocked.Increment(ref next);
                if (index >= projects.Count || stop.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    found[index] = await SearchProjectAsync(
                            projects[index], index, name, filter, started, rows, stop.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    // The first failure stops the other projects; it is rethrown as itself, not wrapped.
                    Interlocked.CompareExchange(ref firstFailure, ExceptionDispatchInfo.Capture(exception), null);
                    stop.Cancel();
                    return;
                }
            }
        }

        var workers = Enumerable.Range(0, Math.Min(degreeOfParallelism, projects.Count))
            .Select(_ => Task.Run(RunWorkerAsync, CancellationToken.None))
            .ToList();
        await Task.WhenAll(workers).ConfigureAwait(false);

        firstFailure?.Throw();
        cancellationToken.ThrowIfCancellationRequested();
        return Merge(found, rows);
    }

    private static async Task<IReadOnlyList<ISymbol>> SearchProjectAsync(
        Project? project,
        int index,
        string name,
        SymbolFilter filter,
        long searchStarted,
        ProjectSearchRow?[] rows,
        CancellationToken cancellationToken)
    {
        if (project is null)
        {
            return [];
        }

        var waitMs = Stopwatch.GetElapsedTime(searchStarted).TotalMilliseconds;
        var projectStarted = Stopwatch.GetTimestamp();
        var outcome = "failure";
        int? matches = null;
        try
        {
            var symbols = (await SymbolFinder.FindSourceDeclarationsAsync(
                    project, name, ignoreCase: true, filter, cancellationToken)
                .ConfigureAwait(false)).ToList();
            matches = symbols.Count;
            outcome = "success";
            return symbols;
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            throw;
        }
        finally
        {
            rows[index] = new ProjectSearchRow(
                index,
                project.FilePath,
                project.Name,
                project.DocumentIds.Count,
                waitMs,
                Stopwatch.GetElapsedTime(projectStarted).TotalMilliseconds,
                matches,
                outcome);
        }
    }

    private static SearchOutcome Merge(IReadOnlyList<IReadOnlyList<ISymbol>?> found, IReadOnlyList<ProjectSearchRow?> rows)
    {
        var symbols = new List<ISymbol>();
        var projectIndexes = new List<int>();
        for (var index = 0; index < found.Count; index++)
        {
            foreach (var symbol in found[index] ?? [])
            {
                symbols.Add(symbol);
                projectIndexes.Add(index);
            }
        }

        return new SearchOutcome(symbols, projectIndexes, rows.OfType<ProjectSearchRow>().ToList());
    }
}
