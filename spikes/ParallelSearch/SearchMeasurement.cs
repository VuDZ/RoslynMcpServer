using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using ParallelSearch.Models;

namespace ParallelSearch;

/// <summary>Times one search and describes its result in a form that can be compared across processes.</summary>
internal static class SearchMeasurement
{
    public static async Task<object> MeasureAsync(
        Solution solution,
        string solutionDirectory,
        string name,
        int degreeOfParallelism)
    {
        var gcBefore = Enumerable.Range(0, GC.MaxGeneration + 1).Select(GC.CollectionCount).ToArray();
        var pauseBefore = GC.GetTotalPauseDuration();
        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var workingSetBefore = Environment.WorkingSet;
        using var sampler = new WorkingSetSampler();
        var watch = Stopwatch.StartNew();
        var outcome = await DeclarationSearch.SearchAsync(
                solution, name, SymbolFilter.Type | SymbolFilter.Member, degreeOfParallelism, CancellationToken.None)
            .ConfigureAwait(false);
        watch.Stop();
        var peakWorkingSet = sampler.Stop();
        var cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalMilliseconds;
        var lines = DescribeResult(outcome, solutionDirectory);
        return new
        {
            wallMs = watch.Elapsed.TotalMilliseconds,
            cpuMs,
            busyCores = cpuMs / watch.Elapsed.TotalMilliseconds,
            workingSetBeforeMb = workingSetBefore / 1048576.0,
            peakWorkingSetMb = peakWorkingSet / 1048576.0,
            gcCollections = Enumerable.Range(0, GC.MaxGeneration + 1).Select(generation => GC.CollectionCount(generation) - gcBefore[generation]).ToArray(),
            gcPauseMs = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds,
            projectSumMs = outcome.Rows.Sum(row => row.ElapsedMs),
            declarations = lines.Count,
            resultSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)))),
            result = lines,
            // Relative paths: result folders are committed to Git, so machine directories stay out of them.
            projects = outcome.Rows.Select(row => row with
            {
                Project = row.Project is null ? null : Path.GetRelativePath(solutionDirectory, row.Project),
            }),
        };
    }

    /// <summary>
    /// One line per declaration in result order: project index, kind, display string and source locations
    /// relative to the solution directory. Equal lines mean an equal answer for the server's formatter.
    /// </summary>
    public static IReadOnlyList<string> DescribeResult(SearchOutcome outcome, string solutionDirectory)
    {
        var lines = new List<string>(outcome.Symbols.Count);
        for (var index = 0; index < outcome.Symbols.Count; index++)
        {
            var symbol = outcome.Symbols[index];
            var locations = symbol.Locations
                .Where(location => location.IsInSource)
                .Select(location => Path.GetRelativePath(solutionDirectory, location.SourceTree!.FilePath)
                    + ":" + location.SourceSpan.Start + "-" + location.SourceSpan.End);
            lines.Add(string.Join(
                '\t',
                outcome.ProjectIndexes[index],
                symbol.Kind,
                symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                string.Join(';', locations)));
        }

        return lines;
    }
}
