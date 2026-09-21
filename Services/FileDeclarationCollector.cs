using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Services;

/// <summary>
/// Collects declarations in one file whose identifier equals <c>symbolName</c> (ordinal, case-sensitive).
/// Used by <c>find_symbol_references</c> / <c>find_symbol_definition</c> when <c>filePath</c> is set and <c>line</c> is omitted.
/// </summary>
internal static class FileDeclarationCollector
{
    internal sealed record Match(ISymbol Symbol, int Line, int Column);

    /// <summary>
    /// Kinds: class, struct, record, interface, enum, method, constructor, destructor,
    /// property, event, field, event field. Not: operator, indexer, local function.
    /// Coordinate is the identifier token (1-based), not the start of the declaration node.
    /// </summary>
    public static IReadOnlyList<Match> Collect(
        SyntaxNode root,
        SemanticModel semanticModel,
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(semanticModel);

        if (string.IsNullOrEmpty(symbolName))
        {
            return [];
        }

        var matches = new List<Match>();
        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (node)
            {
                case ClassDeclarationSyntax c when IdentifierEquals(c.Identifier, symbolName):
                    TryAdd(matches, semanticModel, c, c.Identifier, cancellationToken);
                    break;
                case StructDeclarationSyntax s when IdentifierEquals(s.Identifier, symbolName):
                    TryAdd(matches, semanticModel, s, s.Identifier, cancellationToken);
                    break;
                case RecordDeclarationSyntax r when IdentifierEquals(r.Identifier, symbolName):
                    TryAdd(matches, semanticModel, r, r.Identifier, cancellationToken);
                    break;
                case InterfaceDeclarationSyntax i when IdentifierEquals(i.Identifier, symbolName):
                    TryAdd(matches, semanticModel, i, i.Identifier, cancellationToken);
                    break;
                case EnumDeclarationSyntax e when IdentifierEquals(e.Identifier, symbolName):
                    TryAdd(matches, semanticModel, e, e.Identifier, cancellationToken);
                    break;
                case MethodDeclarationSyntax m when IdentifierEquals(m.Identifier, symbolName):
                    TryAdd(matches, semanticModel, m, m.Identifier, cancellationToken);
                    break;
                case ConstructorDeclarationSyntax ctor when IdentifierEquals(ctor.Identifier, symbolName):
                    TryAdd(matches, semanticModel, ctor, ctor.Identifier, cancellationToken);
                    break;
                case DestructorDeclarationSyntax d when IdentifierEquals(d.Identifier, symbolName):
                    TryAdd(matches, semanticModel, d, d.Identifier, cancellationToken);
                    break;
                case PropertyDeclarationSyntax p when IdentifierEquals(p.Identifier, symbolName):
                    TryAdd(matches, semanticModel, p, p.Identifier, cancellationToken);
                    break;
                case EventDeclarationSyntax ev when IdentifierEquals(ev.Identifier, symbolName):
                    TryAdd(matches, semanticModel, ev, ev.Identifier, cancellationToken);
                    break;
                case VariableDeclaratorSyntax v
                    when IdentifierEquals(v.Identifier, symbolName)
                         && IsFieldOrEventFieldDeclarator(v):
                    TryAdd(matches, semanticModel, v, v.Identifier, cancellationToken);
                    break;
            }
        }

        return matches;
    }

    public static string FormatAmbiguity(string symbolName, string filePath, IReadOnlyList<Match> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            $"Error: Ambiguous: identifier `{symbolName}` matches {matches.Count} declarations in `{filePath}`. "
            + "Pass `line` (and `column` if needed) to disambiguate:");
        foreach (var match in matches)
        {
            sb.AppendLine(
                $"  - `{SymbolDeclarationResolver.GetSymbolFqn(match.Symbol)}` at {match.Line}:{match.Column}");
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatNotFound(string symbolName, string filePath) =>
        $"Symbol `{symbolName}` was not found as a type/member declaration in `{filePath}`.";

    private static bool IdentifierEquals(SyntaxToken identifier, string symbolName) =>
        string.Equals(identifier.ValueText, symbolName, StringComparison.Ordinal);

    private static bool IsFieldOrEventFieldDeclarator(VariableDeclaratorSyntax declarator) =>
        declarator.Parent is VariableDeclarationSyntax
        {
            Parent: FieldDeclarationSyntax or EventFieldDeclarationSyntax
        };

    private static void TryAdd(
        List<Match> matches,
        SemanticModel semanticModel,
        SyntaxNode declarationNode,
        SyntaxToken identifier,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetDeclaredSymbol(declarationNode, cancellationToken);
        if (symbol is null)
        {
            return;
        }

        var linePos = identifier.GetLocation().GetLineSpan().StartLinePosition;
        matches.Add(new Match(symbol, linePos.Line + 1, linePos.Character + 1));
    }
}
