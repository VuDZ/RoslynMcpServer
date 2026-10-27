using Microsoft.CodeAnalysis;

namespace ParallelSearch.Models;

/// <summary>
/// Declarations in the order of <c>Solution.ProjectIds</c> and, within a project, in the order Roslyn returned
/// them. <see cref="ProjectIndexes"/> runs parallel to <see cref="Symbols"/>.
/// </summary>
internal sealed record SearchOutcome(
    IReadOnlyList<ISymbol> Symbols,
    IReadOnlyList<int> ProjectIndexes,
    IReadOnlyList<ProjectSearchRow> Rows);
