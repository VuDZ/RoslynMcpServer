using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcpServer.Services;

/// <summary>
/// Shared name/FQN declaration resolver for navigation tools (S2).
/// One resolver for name-based search — do not invent a second FQN path when <c>symbolId</c> arrives.
/// </summary>
internal static class SymbolDeclarationResolver
{
    private const int MaxCandidatesListed = 10;

    /// <summary>
    /// Resolves declarations on the given solution snapshot.
    /// Name without <c>.</c>: case-insensitive simple name (all matches).
    /// Name with <c>.</c>: exact FQN (<c>Namespace.Type</c> or <c>Namespace.Type.Member</c>);
    /// no <c>global::</c>, no <c>()</c>. FQN miss returns candidates and does not fall back to simple name.
    /// </summary>
    public static async Task<(IReadOnlyList<ISymbol> Symbols, string? Error)> ResolveDeclarationsAsync(
        Solution solution,
        string symbolName,
        SymbolFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);

        if (string.IsNullOrWhiteSpace(symbolName))
        {
            return ([], "Error: `symbolName` is empty.");
        }

        var normalized = NormalizeFqn(symbolName);
        if (normalized.Length == 0)
        {
            return ([], "Error: `symbolName` is empty.");
        }

        var isFqn = normalized.Contains('.', StringComparison.Ordinal);
        var simpleName = isFqn ? GetSimpleName(normalized) : normalized;
        if (simpleName.Length == 0)
        {
            return ([], $"Error: invalid symbol name `{symbolName.Trim()}`.");
        }

        var found = await FindDeclarationsBySimpleNameAsync(
                solution,
                simpleName,
                ignoreCase: true,
                filter,
                cancellationToken)
            .ConfigureAwait(false);

        // Name-based navigation is solution source only — metadata hits (e.g. BCL) must not become groups.
        var distinct = found
            .Where(s => s.Locations.Any(static l => l.IsInSource))
            .Distinct(SymbolEqualityComparer.Default)
            .ToList();

        if (!isFqn)
        {
            return (distinct, null);
        }

        var matches = distinct
            .Where(s => string.Equals(GetSymbolFqn(s), normalized, StringComparison.Ordinal))
            .ToList();

        if (matches.Count == 0)
        {
            return ([], FormatFqnMissError(normalized, distinct));
        }

        return (matches, null);
    }

    /// <summary>
    /// Groups declarations by FQN. Overloads of one method share a FQN and stay one group.
    /// Distinct types/members with the same simple name become separate groups.
    /// </summary>
    public static IReadOnlyList<IGrouping<string, ISymbol>> GroupByFqn(IEnumerable<ISymbol> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        return symbols
            .GroupBy(GetSymbolFqn, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// FQN without <c>global::</c> or parameter lists: <c>Namespace.Type</c> or <c>Namespace.Type.Member</c>.
    /// </summary>
    public static string GetSymbolFqn(ISymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        symbol = symbol.OriginalDefinition;

        if (symbol is INamespaceSymbol ns)
        {
            return NormalizeFqn(ns.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        if (symbol is INamedTypeSymbol type)
        {
            return FormatNamedTypeFqn(type);
        }

        if (symbol.ContainingType is { } containingType)
        {
            return FormatNamedTypeFqn(containingType) + "." + symbol.Name;
        }

        return NormalizeFqn(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    public static string NormalizeFqn(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var n = name.Trim();
        const string globalPrefix = "global::";
        if (n.StartsWith(globalPrefix, StringComparison.Ordinal))
        {
            n = n[globalPrefix.Length..];
        }

        if (n.EndsWith("()", StringComparison.Ordinal))
        {
            n = n[..^2].TrimEnd();
        }

        return n;
    }

    private static string GetSimpleName(string normalizedFqn)
    {
        var lastDot = normalizedFqn.LastIndexOf('.');
        return lastDot < 0 ? normalizedFqn : normalizedFqn[(lastDot + 1)..];
    }

    private static string FormatNamedTypeFqn(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        var parts = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            parts.Push(current.Name);
        }

        var typePath = string.Join(".", parts);
        var ns = type.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace)
        {
            return typePath;
        }

        return NormalizeFqn(ns.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) + "." + typePath;
    }

    private static string FormatFqnMissError(string normalizedFqn, IReadOnlyList<ISymbol> simpleNameCandidates)
    {
        if (simpleNameCandidates.Count == 0)
        {
            return $"No declaration matches FQN `{normalizedFqn}` (and no symbols share the simple name `{GetSimpleName(normalizedFqn)}`).";
        }

        var candidateFqns = simpleNameCandidates
            .Select(GetSymbolFqn)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"No declaration matches FQN `{normalizedFqn}`.");
        sb.AppendLine("Did not fall back to simple-name search. Candidates with the same simple name:");
        foreach (var fqn in candidateFqns.Take(MaxCandidatesListed))
        {
            sb.AppendLine($"  - `{fqn}`");
        }

        if (candidateFqns.Count > MaxCandidatesListed)
        {
            sb.AppendLine($"  … and {candidateFqns.Count - MaxCandidatesListed} more.");
        }

        return sb.ToString().TrimEnd();
    }

    private static async Task<List<ISymbol>> FindDeclarationsBySimpleNameAsync(
        Solution solution,
        string simpleName,
        bool ignoreCase,
        SymbolFilter filter,
        CancellationToken cancellationToken)
    {
        var declarations = new List<ISymbol>();
        foreach (var projectId in solution.ProjectIds)
        {
            var project = solution.GetProject(projectId);
            if (project is null)
            {
                continue;
            }

            var found = await SymbolFinder.FindDeclarationsAsync(
                project,
                simpleName,
                ignoreCase,
                filter,
                cancellationToken).ConfigureAwait(false);
            declarations.AddRange(found);
        }

        return declarations;
    }
}
