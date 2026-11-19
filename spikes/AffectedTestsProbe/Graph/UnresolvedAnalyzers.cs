using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AffectedTestsProbe;

/// <summary>
/// Drops <see cref="UnresolvedAnalyzerReference"/> stubs from a solution snapshot.
/// </summary>
/// <remarks>
/// MSBuildWorkspace inserts a stub when an analyzer DLL is missing. Roslyn 5.9 cannot checksum a project that
/// still has one: <c>SymbolFinder</c> then throws
/// <c>Unexpected value 'UnresolvedAnalyzerReference'</c> for a solution-wide search, whichever project is queried.
/// A stub loaded no assembly, so dropping it changes no binding. The result is a new snapshot and must not be
/// written back with <see cref="Workspace.TryApplyChanges"/>.
/// </remarks>
internal static class UnresolvedAnalyzers
{
    public static Solution Drop(Solution solution)
    {
        var updated = solution;
        var removed = 0;
        foreach (var project in solution.Projects)
        {
            var stubs = project.AnalyzerReferences.OfType<UnresolvedAnalyzerReference>().ToList();
            if (stubs.Count == 0)
            {
                continue;
            }

            var kept = project.AnalyzerReferences.Where(reference => reference is not UnresolvedAnalyzerReference).ToList();
            updated = updated.WithProjectAnalyzerReferences(project.Id, kept);
            removed += stubs.Count;
        }

        if (removed > 0)
        {
            Console.Error.WriteLine(
                $"Dropped {removed} unresolved analyzer references. The analyzer assemblies were not on disk, so they were not loaded.");
        }

        return updated;
    }
}
