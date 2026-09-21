using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Services;

/// <summary>
/// Shared name/position selection for <c>rename_symbol</c> and <c>get_call_graph</c>.
/// Declaration collection reuses <see cref="FileDeclarationCollector"/> (stage-1 kinds); no second kind list.
/// </summary>
internal static class SymbolSelectionHelper
{
    internal sealed record RenameSelection(ISymbol Symbol, int? AbsolutePosition);

    /// <summary>
    /// Ambiguity advice for rename / call graph: both coordinates are required (unlike stage-1 find tools).
    /// </summary>
    internal const string LineAndColumnTogetherAdvice =
        "Pass `line` and `column` together to disambiguate:";

    /// <summary>
    /// <c>line</c> and <c>column</c> must both be omitted or both provided.
    /// </summary>
    public static string? ValidateLineColumnPair(int? line, int? column)
    {
        if (line is null && column is null)
        {
            return null;
        }

        if (line is not null && column is not null)
        {
            return null;
        }

        return "Error: `line` and `column` must be passed together (omit both for name-only selection).";
    }

    /// <summary>
    /// Selects a rename target in one file. No position: unique stage-1 declaration.
    /// With line+column: <see cref="SourcePositionHelper"/> (declaration or usage in this file).
    /// </summary>
    public static async Task<(RenameSelection? Selection, string? Error)> TrySelectForRenameAsync(
        Document document,
        string symbolName,
        int? line,
        int? column,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var pairError = ValidateLineColumnPair(line, column);
        if (pairError is not null)
        {
            return (null, pairError);
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return (null, "Error: Failed to obtain syntax root or semantic model.");
        }

        var filePath = document.FilePath ?? "(unknown)";

        if (line is int lineNumber && column is int columnNumber)
        {
            var (symbol, position, positionError) = await SourcePositionHelper
                .ResolveSymbolInDocumentAsync(document, symbolName, lineNumber, columnNumber, cancellationToken)
                .ConfigureAwait(false);
            if (positionError is not null)
            {
                return (null, positionError);
            }

            if (symbol is null || position is null)
            {
                return (
                    null,
                    $"Error: Unable to resolve symbol `{symbolName}` at line {lineNumber}, column {columnNumber} in `{filePath}`.");
            }

            if (!string.Equals(symbol.Name, symbolName, StringComparison.Ordinal))
            {
                return (
                    null,
                    $"Error: Symbol at line {lineNumber}, column {columnNumber} is `{SymbolDeclarationResolver.GetSymbolFqn(symbol)}`, "
                    + $"not `{symbolName}`.");
            }

            return (new RenameSelection(symbol, position.AbsolutePosition), null);
        }

        var matches = FileDeclarationCollector.Collect(root, model, symbolName, cancellationToken);
        if (matches.Count == 0)
        {
            return (null, $"Error: Symbol `{symbolName}` not found.");
        }

        if (matches.Count > 1)
        {
            return (
                null,
                FileDeclarationCollector.FormatAmbiguity(
                    symbolName,
                    filePath,
                    matches,
                    LineAndColumnTogetherAdvice));
        }

        return (new RenameSelection(matches[0].Symbol, AbsolutePosition: null), null);
    }

    /// <summary>
    /// Selects a method for the call graph. No position: stage-1 collector filtered to ordinary methods
    /// of <paramref name="className"/>. With line+column: declaration or call site; non-method is an error.
    /// </summary>
    public static async Task<(IMethodSymbol? Method, string? Error)> TrySelectMethodForCallGraphAsync(
        Document document,
        string className,
        string methodName,
        int? line,
        int? column,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var pairError = ValidateLineColumnPair(line, column);
        if (pairError is not null)
        {
            return (null, pairError);
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return (null, "Error: Could not obtain syntax tree or semantic model.");
        }

        var filePath = document.FilePath ?? "(unknown)";
        var trimmedClass = className.Trim();
        var trimmedMethod = methodName.Trim();

        if (line is int lineNumber && column is int columnNumber)
        {
            var (symbol, position, positionError) = await SourcePositionHelper
                .ResolveSymbolInDocumentAsync(document, trimmedMethod, lineNumber, columnNumber, cancellationToken)
                .ConfigureAwait(false);
            if (positionError is not null)
            {
                return (null, positionError);
            }

            if (symbol is null || position is null)
            {
                return (
                    null,
                    $"Error: Unable to resolve `{trimmedMethod}` at line {lineNumber}, column {columnNumber} in `{filePath}`.");
            }

            if (symbol is not IMethodSymbol methodSymbol)
            {
                return (
                    null,
                    $"Error: Symbol at line {lineNumber}, column {columnNumber} is not a method (`{symbol.Kind}`). "
                    + "`get_call_graph` requires a method declaration or call site.");
            }

            if (!string.Equals(methodSymbol.Name, trimmedMethod, StringComparison.Ordinal))
            {
                return (
                    null,
                    $"Error: Symbol at line {lineNumber}, column {columnNumber} is `{SymbolDeclarationResolver.GetSymbolFqn(methodSymbol)}`, "
                    + $"not `{trimmedMethod}`.");
            }

            return (methodSymbol, null);
        }

        if (TypeSyntaxHelper.FindClassDeclaration(root, trimmedClass) is null)
        {
            return (null, $"Error: Class `{trimmedClass}` not found in `{filePath}`.");
        }

        var matches = FileDeclarationCollector.Collect(root, model, trimmedMethod, cancellationToken)
            .Where(m => IsOrdinaryMethodOfClass(m.Symbol, trimmedClass))
            .ToList();

        if (matches.Count == 0)
        {
            return (null, $"Error: Method `{trimmedMethod}` not found in class `{trimmedClass}`.");
        }

        if (matches.Count > 1)
        {
            return (
                null,
                FileDeclarationCollector.FormatAmbiguity(
                    trimmedMethod,
                    filePath,
                    matches,
                    LineAndColumnTogetherAdvice));
        }

        return ((IMethodSymbol)matches[0].Symbol, null);
    }

    private static bool IsOrdinaryMethodOfClass(ISymbol symbol, string className) =>
        symbol is IMethodSymbol { MethodKind: MethodKind.Ordinary } method
        && method.ContainingType is { } type
        && string.Equals(type.Name, className, StringComparison.Ordinal);
}
