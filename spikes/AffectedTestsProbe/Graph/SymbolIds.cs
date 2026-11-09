using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

internal static class SymbolIds
{
    public static string? DocumentationId(ISymbol symbol) =>
        symbol.OriginalDefinition.GetDocumentationCommentId();

    public static string TypeKey(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return "";
        }

        var id = named.OriginalDefinition.GetDocumentationCommentId();
        if (string.IsNullOrEmpty(id))
        {
            return "";
        }

        var assembly = named.ContainingAssembly?.Identity.Name ?? "";
        return assembly + ":" + id;
    }

    /// <summary>
    /// An implicit constructor has no syntax of its own. Its node uses the containing type's file
    /// and the constructor's documentation id, so <c>new T()</c> still has a source target.
    /// </summary>
    public static MemberNode? TryNode(ISymbol symbol, Solution solution, IReadOnlyDictionary<ProjectId, ProjectKey> keys)
    {
        var original = symbol.OriginalDefinition;
        var id = original.GetDocumentationCommentId();
        if (id is null)
        {
            return null;
        }

        var tree = original.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree
            ?? original.Locations.FirstOrDefault(location => location.SourceTree is not null)?.SourceTree;
        if (tree is null && original.IsImplicitlyDeclared)
        {
            tree = original.ContainingType?.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree;
        }

        if (tree is null)
        {
            return null;
        }

        var document = solution.GetDocument(tree);
        if (document is null || !keys.TryGetValue(document.Project.Id, out var key))
        {
            return null;
        }

        return new MemberNode(key, id);
    }
}
