using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime;
using System.Text.Json;

namespace RoslynMcpServer.Services.Navigation;

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
    public static Task<(IReadOnlyList<ISymbol> Symbols, string? Error)> ResolveDeclarationsAsync(
        Solution solution,
        string symbolName,
        SymbolFilter filter,
        CancellationToken cancellationToken = default) =>
        ResolveDeclarationsAsync(solution, symbolName, filter, NullLogger.Instance, TimeProvider.System, cancellationToken);

    public static async Task<(IReadOnlyList<ISymbol> Symbols, string? Error)> ResolveDeclarationsAsync(
        Solution solution,
        string symbolName,
        SymbolFilter filter,
        ILogger logger,
        TimeProvider timeProvider,
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
                logger,
                timeProvider,
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
            return ([], FormatFqnMissError(normalized, simpleName, distinct.Select(GetSymbolFqn)));
        }

        return (matches, null);
    }

    /// <summary>Resolves the qualified declaration names shown by <c>find_implementations</c>.</summary>
    public static async Task<(IReadOnlyList<ISymbol> Symbols, string? Error)> ResolveImplementationBasesAsync(
        Solution solution,
        string symbolName,
        ILogger logger,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeFqn(symbolName);
        var parsed = normalized.Contains('<') ? SyntaxFactory.ParseName(normalized) : null;
        var simpleName = parsed switch
        {
            null => GetSimpleName(normalized),
            QualifiedNameSyntax { ContainsDiagnostics: false } qualified => qualified.Right.Identifier.ValueText,
            SimpleNameSyntax { ContainsDiagnostics: false } simple => simple.Identifier.ValueText,
            _ => string.Empty,
        };
        if (simpleName.Length == 0)
        {
            return ([], $"Error: invalid symbol name `{symbolName.Trim()}`.");
        }

        if (simpleName.StartsWith('@'))
        {
            simpleName = simpleName[1..];
        }

        var (symbols, error) = await ResolveDeclarationsAsync(
            solution, simpleName, SymbolFilter.Type, logger, timeProvider, cancellationToken)
            .ConfigureAwait(false);
        if (error is not null || (parsed is null && !normalized.Contains('.')
            && !symbolName.TrimStart().StartsWith("global::", StringComparison.Ordinal)))
        {
            return (symbols, error);
        }

        return MatchImplementationBaseFqn(parsed?.NormalizeWhitespace().ToFullString() ?? normalized, simpleName, symbols);
    }

    public static string GetImplementationBaseFqn(INamedTypeSymbol type)
    {
        var display = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        // A global-namespace declaration needs a qualifier to distinguish it from a short-name search.
        return type.ContainingNamespace is { IsGlobalNamespace: true } ? display : NormalizeFqn(display);
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

    private static (IReadOnlyList<ISymbol> Symbols, string? Error) MatchImplementationBaseFqn(
        string qualifiedName,
        string simpleName,
        IReadOnlyList<ISymbol> symbols)
    {
        var candidates = symbols.OfType<INamedTypeSymbol>().ToArray();
        var matches = candidates
            .Where(type => string.Equals(NormalizeFqn(GetImplementationBaseFqn(type)), qualifiedName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length > 0)
        {
            return (matches, null);
        }

        // Old FQNs omitted type parameters and used raw type names, including escaped keywords.
        if (!qualifiedName.Contains('<'))
        {
            var legacyMatches = candidates
                .Where(type => string.Equals(GetSymbolFqn(type), qualifiedName, StringComparison.Ordinal))
                .ToArray();
            if (legacyMatches.Length == 1)
            {
                return (legacyMatches, null);
            }

            // A nested declaration can have its own arity of zero in a generic container.
            // Apply that preference only when the containing type is unambiguous.
            if (legacyMatches.Length > 1 && legacyMatches.All(type =>
                    SymbolEqualityComparer.Default.Equals(type.ContainingType, legacyMatches[0].ContainingType)))
            {
                var nonGeneric = legacyMatches.Where(type => type.Arity == 0).ToArray();
                if (nonGeneric.Length > 0)
                {
                    return (nonGeneric, null);
                }
            }
        }

        return ([], FormatFqnMissError(qualifiedName, simpleName, candidates.Select(GetImplementationBaseFqn)));
    }

    private static string FormatFqnMissError(
        string normalizedFqn,
        string simpleName,
        IEnumerable<string> candidates)
    {
        var candidateFqns = candidates
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        if (candidateFqns.Count == 0)
        {
            return $"No declaration matches FQN `{normalizedFqn}` (and no symbols share the simple name `{simpleName}`).";
        }

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
        ILogger logger,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var declarations = new List<ISymbol>();
        var searchId = Guid.NewGuid();
        var started = timeProvider.GetTimestamp();
        var completedProjects = 0;
        var outcome = "failure";
        // Process-wide counters: concurrent work in the server adds to them. Under workstation GC the
        // pauses took 40-46% of the first search on Roslyn.slnx, so the summary records them per search.
        var gcCollectionsBefore = CountGcCollections();
        var gcPauseBefore = GC.GetTotalPauseDuration();
        using var exceptions = new SearchExceptionRecorder();
        try
        {
            foreach (var projectId in solution.ProjectIds)
            {
                var project = solution.GetProject(projectId);
                if (project is null)
                {
                    continue;
                }

                var projectStarted = timeProvider.GetTimestamp();
                var projectOutcome = "failure";
                int? matches = null;
                exceptions.CurrentProject = project.FilePath ?? project.Name;
                try
                {
                    // The public contract is solution source only; scanning reference assemblies is discarded below.
                    var found = await SymbolFinder.FindSourceDeclarationsAsync(
                        project, simpleName, ignoreCase, filter, cancellationToken).ConfigureAwait(false);
                    var previousCount = declarations.Count;
                    declarations.AddRange(found);
                    matches = declarations.Count - previousCount;
                    completedProjects++;
                    projectOutcome = "success";
                }
                catch (OperationCanceledException)
                {
                    projectOutcome = "cancelled";
                    throw;
                }
                finally
                {
                    exceptions.CurrentProject = null;
                    logger.LogInformation("workspace_search_project {Json:l}", JsonSerializer.Serialize(new
                    {
                        searchId,
                        projectId = project.Id.Id,
                        project = project.FilePath,
                        name = project.Name,
                        documents = project.DocumentIds.Count,
                        elapsedMs = timeProvider.GetElapsedTime(projectStarted).TotalMilliseconds,
                        outcome = projectOutcome,
                        matches,
                    }));
                }
            }

            outcome = "success";
            return declarations;
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            throw;
        }
        finally
        {
            logger.LogInformation("workspace_search_exceptions {Json:l}", exceptions.ToJson(searchId));
            logger.LogInformation("workspace_search_summary {Json:l}", JsonSerializer.Serialize(new
            {
                searchId,
                outcome,
                elapsedMs = timeProvider.GetElapsedTime(started).TotalMilliseconds,
                projects = solution.ProjectIds.Count,
                completedProjects,
                serverGc = GCSettings.IsServerGC,
                gcPauseMs = (GC.GetTotalPauseDuration() - gcPauseBefore).TotalMilliseconds,
                gcCollections = CountGcCollections().Zip(gcCollectionsBefore, (after, before) => after - before).ToArray(),
            }));
        }
    }

    private static int[] CountGcCollections() =>
        Enumerable.Range(0, GC.MaxGeneration + 1).Select(GC.CollectionCount).ToArray();
}
