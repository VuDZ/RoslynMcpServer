using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AffectedTestsProbe;

/// <summary>
/// Builds the queries of the <c>impact</c> command. Members are read from the workspace documents,
/// as the server would read them after an edit, so this needs a loaded solution and no build.
/// </summary>
internal static class ImpactTargets
{
    public static async Task<List<ImpactTarget>> FromMembersAsync(
        Solution solution,
        ProjectMap map,
        IReadOnlyList<string> documentationIds,
        CancellationToken cancellationToken)
    {
        var targets = new List<ImpactTarget>();
        foreach (var id in documentationIds)
        {
            var locations = await MemberLocations.FindAsync(solution, map, id, projectPath: null, cancellationToken).ConfigureAwait(false);
            var kinds = locations.Count == 0 ? [] : new[] { MemberKinds.Of(locations[0].Symbol) };
            targets.Add(new ImpactTarget("member", id, [id], kinds, locations));
        }

        return targets;
    }

    /// <summary>Every member with a body in each file, in every project that compiles the file.</summary>
    public static async Task<List<ImpactTarget>> FromFilesAsync(
        Solution solution,
        ProjectMap map,
        IReadOnlyList<string> paths,
        string baseDirectory,
        CancellationToken cancellationToken)
    {
        var targets = new List<ImpactTarget>();
        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(path, baseDirectory);
            var locations = new List<MemberLocation>();
            var seen = new HashSet<MemberNode>();
            foreach (var documentId in solution.GetDocumentIdsWithFilePath(fullPath))
            {
                var document = solution.GetDocument(documentId);
                if (document is null)
                {
                    continue;
                }

                foreach (var (symbol, _) in await DeclaredMembersAsync(document, within: null, cancellationToken).ConfigureAwait(false))
                {
                    if (SymbolIds.TryNode(symbol, solution, map.Keys) is { } node && seen.Add(node))
                    {
                        locations.Add(new MemberLocation(symbol, node));
                    }
                }
            }

            targets.Add(Group("file", fullPath, locations));
        }

        return targets;
    }

    /// <summary>
    /// Random members with a body from production projects that some selected test project depends on.
    /// A project whose name contains "Test" counts as test code even without test methods.
    /// </summary>
    public static async Task<List<ImpactTarget>> SampleAsync(
        Solution solution,
        ProjectMap map,
        RootIndex roots,
        int count,
        int seed,
        CancellationToken cancellationToken)
    {
        var candidates = await CandidatesAsync(solution, map, roots, cancellationToken).ConfigureAwait(false);
        Shuffle(candidates, seed);
        var targets = new List<ImpactTarget>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (document, span) in candidates)
        {
            if (targets.Count >= count)
            {
                break;
            }

            var locations = await LocationsAtAsync(solution, map, document, span, cancellationToken).ConfigureAwait(false);
            if (locations.Count == 0 || !taken.Add(locations[0].Node.DocumentationId))
            {
                continue;
            }

            targets.Add(Group("sample", locations[0].Node.DocumentationId, locations));
        }

        return targets;
    }

    private static ImpactTarget Group(string source, string label, List<MemberLocation> locations)
    {
        var members = locations
            .GroupBy(location => location.Node.DocumentationId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        return new ImpactTarget(
            source,
            label,
            members.Select(group => group.Key).ToArray(),
            members.Select(group => MemberKinds.Of(group.First().Symbol)).ToArray(),
            locations);
    }

    private static async Task<List<(Document Document, TextSpan Span)>> CandidatesAsync(
        Solution solution,
        ProjectMap map,
        RootIndex roots,
        CancellationToken cancellationToken)
    {
        var candidates = new List<(Document Document, TextSpan Span)>();
        foreach (var project in ProductionProjects(solution, map, roots))
        {
            foreach (var document in project.Documents.OrderBy(document => document.FilePath, StringComparer.Ordinal))
            {
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (root is null)
                {
                    continue;
                }

                foreach (var node in root.DescendantNodes().Where(HasBody))
                {
                    candidates.Add((document, node.Span));
                }
            }
        }

        return candidates;
    }

    private static IEnumerable<Project> ProductionProjects(Solution solution, ProjectMap map, RootIndex roots)
    {
        var testIds = roots.Classes
            .Where(testClass => testClass.Units.Count > 0 && map.Projects.ContainsKey(testClass.Project))
            .Select(testClass => map.Projects[testClass.Project].Id)
            .ToHashSet();
        var graph = solution.GetProjectDependencyGraph();
        return solution.Projects
            .Where(project => project.Language == LanguageNames.CSharp
                && !testIds.Contains(project.Id)
                && !project.Name.Contains("Test", StringComparison.OrdinalIgnoreCase)
                && graph.GetProjectsThatTransitivelyDependOnThisProject(project.Id).Any(testIds.Contains))
            .OrderBy(project => project.FilePath, StringComparer.Ordinal)
            .ThenBy(project => project.Name, StringComparer.Ordinal);
    }

    /// <summary>The same declaration in every project that compiles the file, for example each target framework.</summary>
    private static async Task<List<MemberLocation>> LocationsAtAsync(
        Solution solution,
        ProjectMap map,
        Document document,
        TextSpan span,
        CancellationToken cancellationToken)
    {
        var locations = new List<MemberLocation>();
        var seen = new HashSet<MemberNode>();
        var documentIds = document.FilePath is null ? [document.Id] : solution.GetDocumentIdsWithFilePath(document.FilePath);
        foreach (var documentId in documentIds)
        {
            var sibling = solution.GetDocument(documentId);
            if (sibling is null)
            {
                continue;
            }

            foreach (var (symbol, declared) in await DeclaredMembersAsync(sibling, span, cancellationToken).ConfigureAwait(false))
            {
                if (declared == span && SymbolIds.TryNode(symbol, solution, map.Keys) is { } node && seen.Add(node))
                {
                    locations.Add(new MemberLocation(symbol, node));
                }
            }
        }

        return locations;
    }

    private static async Task<List<(ISymbol Symbol, TextSpan Span)>> DeclaredMembersAsync(
        Document document,
        TextSpan? within,
        CancellationToken cancellationToken)
    {
        var members = new List<(ISymbol Symbol, TextSpan Span)>();
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return members;
        }

        var nodes = within is { } span ? root.DescendantNodes(span) : root.DescendantNodes();
        foreach (var node in nodes.Where(HasBody))
        {
            if (Declared(model, node, cancellationToken) is { } symbol)
            {
                members.Add((symbol, node.Span));
            }
        }

        return members;
    }

    // An expression-bodied property or indexer has no accessor syntax; its body is the getter.
    private static ISymbol? Declared(SemanticModel model, SyntaxNode node, CancellationToken cancellationToken)
    {
        var symbol = model.GetDeclaredSymbol(node, cancellationToken);
        return symbol is IPropertySymbol property ? property.GetMethod : symbol;
    }

    private static bool HasBody(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax method => method.Body is not null || method.ExpressionBody is not null,
        AccessorDeclarationSyntax accessor => accessor.Body is not null || accessor.ExpressionBody is not null,
        PropertyDeclarationSyntax property => property.ExpressionBody is not null,
        IndexerDeclarationSyntax indexer => indexer.ExpressionBody is not null,
        _ => false,
    };

    private static void Shuffle<T>(List<T> items, int seed)
    {
        var random = new Random(seed);
        for (var index = items.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (items[index], items[swap]) = (items[swap], items[index]);
        }
    }
}
