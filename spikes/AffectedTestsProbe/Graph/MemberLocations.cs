using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

/// <summary>
/// Every project key whose compilation contains the member's source file.
/// The first project that can resolve the documentation id is not enough: a multi-targeted
/// library exposes the same file as several keys, and tests may reference only one of them.
/// </summary>
internal static class MemberLocations
{
    public static async Task<IReadOnlyList<MemberLocation>> FindAsync(
        Solution solution,
        ProjectMap map,
        string documentationId,
        string? projectPath,
        CancellationToken cancellationToken)
    {
        var found = new List<MemberLocation>();
        var seen = new HashSet<MemberNode>();
        foreach (var project in solution.Projects)
        {
            if (projectPath is not null
                && !string.Equals(project.FilePath, Path.GetFullPath(projectPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            var symbol = compilation is null
                ? null
                : DocumentationCommentId.GetFirstSymbolForDeclarationId(documentationId, compilation);
            if (symbol is null || !DeclaredHere(project, symbol))
            {
                continue;
            }

            if (SymbolIds.TryNode(symbol, solution, map.Keys) is not { } node || !seen.Add(node))
            {
                continue;
            }

            found.Add(new MemberLocation(symbol, node));
        }

        return found;
    }

    public static string[] ForwardKeys(Reachability reach, DispatchKind kind, IReadOnlyList<MemberLocation> locations)
    {
        var units = new HashSet<TestUnit>();
        foreach (var location in locations)
        {
            foreach (var unit in reach.Units(kind, location.Node))
            {
                units.Add(unit);
            }
        }

        return units.Select(unit => unit.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray();
    }

    private static bool DeclaredHere(Project project, ISymbol symbol)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var document = project.Solution.GetDocument(reference.SyntaxTree);
            if (document?.Project.Id == project.Id)
            {
                return true;
            }
        }

        return false;
    }
}
