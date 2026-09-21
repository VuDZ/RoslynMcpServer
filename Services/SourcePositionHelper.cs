using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcpServer.Services;

/// <summary>
/// LSP-style 1-based line/column helpers: offset conversion, identifier auto-column,
/// and symbol resolution at a source position (declaration or usage).
/// </summary>
internal static class SourcePositionHelper
{
    internal sealed record PositionResolution(int Line, int Column, int AbsolutePosition, SyntaxToken IdentifierToken);

    /// <summary>
    /// Resolves an optional 1-based column on a 1-based line. When <paramref name="column1Based"/> is null,
    /// picks the unique identifier token on that line whose text equals <paramref name="symbolName"/> (ordinal).
    /// Never uses <see cref="string.IndexOf(string)"/>.
    /// </summary>
    public static bool TryResolvePosition(
        SyntaxNode root,
        SourceText text,
        int line1Based,
        string symbolName,
        int? column1Based,
        out PositionResolution resolution,
        out string? error)
    {
        resolution = null!;
        error = null;

        if (string.IsNullOrWhiteSpace(symbolName))
        {
            error = "Symbol name is empty.";
            return false;
        }

        if (column1Based is int column)
        {
            if (!TryGetAbsolutePosition(text, line1Based, column, out var absolutePosition, out error))
            {
                return false;
            }

            var token = FindIdentifierTokenAt(root, text, absolutePosition);
            resolution = new PositionResolution(line1Based, column, absolutePosition, token);
            return true;
        }

        if (!TryGetLine(text, line1Based, out var line, out error))
        {
            return false;
        }

        var lineIndex = line1Based - 1;
        var matches = new List<SyntaxToken>();
        foreach (var token in root.DescendantTokens(line.Span))
        {
            if (!token.IsKind(SyntaxKind.IdentifierToken))
            {
                continue;
            }

            if (!string.Equals(token.ValueText, symbolName, StringComparison.Ordinal))
            {
                continue;
            }

            var tokenLine = text.Lines.GetLinePosition(token.SpanStart).Line;
            if (tokenLine != lineIndex)
            {
                continue;
            }

            matches.Add(token);
        }

        if (matches.Count == 0)
        {
            error =
                $"Symbol `{symbolName}` was not found as an identifier on line {line1Based}. "
                + "Pass `column` (1-based) to target a specific position.";
            return false;
        }

        if (matches.Count > 1)
        {
            var columns = string.Join(
                ", ",
                matches.Select(t => (text.Lines.GetLinePosition(t.SpanStart).Character + 1).ToString()));
            error =
                $"Ambiguous: identifier `{symbolName}` appears {matches.Count} times on line {line1Based} "
                + $"at columns {columns}. Pass `column` to disambiguate.";
            return false;
        }

        var match = matches[0];
        var matchColumn = text.Lines.GetLinePosition(match.SpanStart).Character + 1;
        resolution = new PositionResolution(line1Based, matchColumn, match.SpanStart, match);
        return true;
    }

    /// <summary>
    /// Converts a 1-based line/column to an absolute UTF-16 offset. Returns a human-readable error
    /// when the line or column is out of range (does not throw).
    /// </summary>
    public static bool TryGetAbsolutePosition(
        SourceText text,
        int line1Based,
        int column1Based,
        out int absolutePosition,
        out string? error)
    {
        absolutePosition = 0;
        if (!TryGetLine(text, line1Based, out var line, out error))
        {
            return false;
        }

        if (column1Based < 1)
        {
            error = $"Column must be >= 1 (got {column1Based}).";
            return false;
        }

        var lineLength = line.Span.Length;
        if (lineLength == 0)
        {
            if (column1Based != 1)
            {
                error =
                    $"Column {column1Based} is past the end of line {line1Based} "
                    + "(empty line; use column 1).";
                return false;
            }

            absolutePosition = line.Start;
            return true;
        }

        if (column1Based > lineLength)
        {
            error =
                $"Column {column1Based} is past the end of line {line1Based} "
                + $"(line length is {lineLength}).";
            return false;
        }

        absolutePosition = line.Start + (column1Based - 1);
        return true;
    }

    public static bool TryGetLine(
        SourceText text,
        int line1Based,
        out TextLine line,
        out string? error)
    {
        line = default;
        if (line1Based < 1)
        {
            error = $"Line must be >= 1 (got {line1Based}).";
            return false;
        }

        if (text.Lines.Count == 0)
        {
            error = "The file is empty.";
            return false;
        }

        if (line1Based > text.Lines.Count)
        {
            error =
                $"Line {line1Based} is past the end of the file "
                + $"({text.Lines.Count} line(s)).";
            return false;
        }

        line = text.Lines[line1Based - 1];
        error = null;
        return true;
    }

    /// <summary>
    /// Resolves the symbol at an absolute position: declaration name or usage.
    /// </summary>
    public static ISymbol? GetSymbolAtPosition(
        SemanticModel semanticModel,
        SyntaxNode root,
        int absolutePosition,
        CancellationToken cancellationToken = default)
    {
        if (absolutePosition < 0 || absolutePosition > root.FullSpan.End)
        {
            return null;
        }

        var token = root.FindToken(absolutePosition);
        if (token.RawKind == 0)
        {
            return null;
        }

        if (token.IsKind(SyntaxKind.IdentifierToken)
            && TryGetDeclaredSymbolForNameToken(semanticModel, token, cancellationToken, out var declared)
            && declared is not null)
        {
            return declared;
        }

        var node = root.FindNode(
            TextSpan.FromBounds(absolutePosition, absolutePosition),
            findInsideTrivia: false,
            getInnermostNodeForTie: true);

        for (var current = node; current is not null; current = current.Parent)
        {
            var info = semanticModel.GetSymbolInfo(current, cancellationToken);
            if (info.Symbol is not null)
            {
                return info.Symbol;
            }

            if (info.CandidateSymbols.Length == 1)
            {
                return info.CandidateSymbols[0];
            }

            var memberGroup = semanticModel.GetMemberGroup(current, cancellationToken);
            if (memberGroup.Length == 1)
            {
                return memberGroup[0];
            }

            var typeInfo = semanticModel.GetTypeInfo(current, cancellationToken);
            if (typeInfo.Type is INamedTypeSymbol { TypeKind: not TypeKind.Error } named)
            {
                return named;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a symbol in a document at <paramref name="line1Based"/> / optional column using
    /// identifier-token auto-column rules. Returns a human-readable error on failure.
    /// </summary>
    public static async Task<(ISymbol? Symbol, PositionResolution? Position, string? Error)> ResolveSymbolInDocumentAsync(
        Document document,
        string symbolName,
        int line1Based,
        int? column1Based,
        CancellationToken cancellationToken = default)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return (null, null, $"Could not build semantic model for `{document.FilePath}`.");
        }

        if (!TryResolvePosition(root, text, line1Based, symbolName, column1Based, out var position, out var error))
        {
            return (null, null, error);
        }

        var symbol = GetSymbolAtPosition(model, root, position.AbsolutePosition, cancellationToken);
        if (symbol is null)
        {
            return (
                null,
                position,
                $"No symbol found at line {position.Line}, column {position.Column} in `{document.FilePath}`.");
        }

        return (symbol, position, null);
    }

    private static SyntaxToken FindIdentifierTokenAt(SyntaxNode root, SourceText text, int absolutePosition)
    {
        var token = root.FindToken(absolutePosition);
        if (token.IsKind(SyntaxKind.IdentifierToken)
            && (token.Span.Contains(absolutePosition) || absolutePosition == token.Span.Start))
        {
            return token;
        }

        // Caret on the first character is Span.Start; Contains excludes End.
        if (token.IsKind(SyntaxKind.IdentifierToken) && absolutePosition == token.Span.End)
        {
            return token;
        }

        _ = text;
        return default;
    }

    private static bool TryGetDeclaredSymbolForNameToken(
        SemanticModel semanticModel,
        SyntaxToken token,
        CancellationToken cancellationToken,
        out ISymbol? symbol)
    {
        symbol = null;
        var parent = token.Parent;
        if (parent is null)
        {
            return false;
        }

        if (!IsDeclarationNameToken(parent, token))
        {
            return false;
        }

        // VariableDeclarator / Parameter / TypeParameter hold the declared symbol on themselves.
        symbol = semanticModel.GetDeclaredSymbol(parent, cancellationToken);
        if (symbol is not null)
        {
            return true;
        }

        // Class/method/property: declared symbol is on the declaration node (often the parent itself).
        for (var current = parent; current is not null; current = current.Parent)
        {
            symbol = semanticModel.GetDeclaredSymbol(current, cancellationToken);
            if (symbol is not null)
            {
                return true;
            }

            // Stop before walking into unrelated outer members once we left the immediate declaration.
            if (current is MemberDeclarationSyntax or BaseTypeDeclarationSyntax or DelegateDeclarationSyntax
                or LocalFunctionStatementSyntax or VariableDeclaratorSyntax or ParameterSyntax)
            {
                break;
            }
        }

        return false;
    }

    private static bool IsDeclarationNameToken(SyntaxNode parent, SyntaxToken token) =>
        parent switch
        {
            MethodDeclarationSyntax m => m.Identifier == token,
            ConstructorDeclarationSyntax c => c.Identifier == token,
            DestructorDeclarationSyntax d => d.Identifier == token,
            PropertyDeclarationSyntax p => p.Identifier == token,
            EventDeclarationSyntax e => e.Identifier == token,
            IndexerDeclarationSyntax => false,
            OperatorDeclarationSyntax => false,
            ConversionOperatorDeclarationSyntax => false,
            FieldDeclarationSyntax => false,
            EventFieldDeclarationSyntax => false,
            VariableDeclaratorSyntax v => v.Identifier == token,
            ParameterSyntax p => p.Identifier == token,
            TypeParameterSyntax t => t.Identifier == token,
            ClassDeclarationSyntax c => c.Identifier == token,
            StructDeclarationSyntax s => s.Identifier == token,
            InterfaceDeclarationSyntax i => i.Identifier == token,
            RecordDeclarationSyntax r => r.Identifier == token,
            EnumDeclarationSyntax e => e.Identifier == token,
            EnumMemberDeclarationSyntax e => e.Identifier == token,
            DelegateDeclarationSyntax d => d.Identifier == token,
            LocalFunctionStatementSyntax l => l.Identifier == token,
            NamespaceDeclarationSyntax n => false,
            FileScopedNamespaceDeclarationSyntax => false,
            IdentifierNameSyntax => false,
            _ => false,
        };
}
