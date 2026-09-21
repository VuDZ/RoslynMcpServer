using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcpServer.Services;

/// <summary>
/// Classifies virtual/override/abstract class-method reference locations as direct or virtual dispatch,
/// and optionally filters to direct-only for <c>find_symbol_references</c>.
/// </summary>
internal static class VirtualReferenceClassifier
{
    internal const string DirectOnlyRequiresFilePathError =
        "Error: `directOnly: true` requires `filePath`. Solution-wide name search has no single declaring type.";

    internal enum DispatchKind
    {
        Direct,
        Virtual,
        Unknown,
    }

    internal sealed record ApplyResult(
        IReadOnlyList<ReferenceLocation> Locations,
        string? Note,
        string? Error);

    public static string? ValidateDirectOnlyRequiresFilePath(bool directOnly, bool hasFilePath) =>
        directOnly && !hasFilePath ? DirectOnlyRequiresFilePathError : null;

    public static string FormatVirtualDispatchNote(int virtualCount, int totalCount) =>
        $"Note: {virtualCount} of {totalCount} reference location(s) are virtual dispatch; pass `directOnly: true` to keep only direct calls.";

    public static string FormatFilteredNote(int directCount, int removedCount) =>
        $"Note: Kept {directCount} direct reference location(s); removed {removedCount} virtual-dispatch location(s).";

    public static string FormatZeroRemainError(int removedCount) =>
        $"Error: No direct reference locations remain after `directOnly` filtering ({removedCount} virtual-dispatch location(s) removed). Retry with `directOnly: false`.";

    /// <summary>
    /// Class methods with <see cref="IMethodSymbol.IsVirtual"/>, <see cref="IMethodSymbol.IsOverride"/>,
    /// or <see cref="IMethodSymbol.IsAbstract"/>. Interface methods are never applicable.
    /// </summary>
    public static bool IsApplicableMethod(ISymbol symbol)
    {
        if (symbol is not IMethodSymbol method)
        {
            return false;
        }

        if (method.ContainingType?.TypeKind != TypeKind.Class)
        {
            return false;
        }

        return method.IsVirtual || method.IsOverride || method.IsAbstract;
    }

    public static async Task<ApplyResult> ApplyAsync(
        ISymbol requestedSymbol,
        IReadOnlyList<ReferenceLocation> locations,
        bool directOnly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedSymbol);
        ArgumentNullException.ThrowIfNull(locations);

        if (!IsApplicableMethod(requestedSymbol))
        {
            return new ApplyResult(locations, Note: null, Error: null);
        }

        var declaringType = ((IMethodSymbol)requestedSymbol).ContainingType
            ?? throw new InvalidOperationException("Applicable method has no containing type.");

        var kept = new List<ReferenceLocation>(locations.Count);
        var virtualCount = 0;

        foreach (var location in locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = await ClassifyAsync(location, declaringType, cancellationToken).ConfigureAwait(false);
            if (kind == DispatchKind.Virtual)
            {
                virtualCount++;
                if (!directOnly)
                {
                    kept.Add(location);
                }
            }
            else
            {
                kept.Add(location);
            }
        }

        if (!directOnly)
        {
            var note = virtualCount > 0
                ? FormatVirtualDispatchNote(virtualCount, locations.Count)
                : null;
            return new ApplyResult(locations, note, Error: null);
        }

        var removed = virtualCount;
        if (removed == 0)
        {
            return new ApplyResult(locations, Note: null, Error: null);
        }

        if (kept.Count == 0)
        {
            return new ApplyResult(
                Array.Empty<ReferenceLocation>(),
                Note: null,
                Error: FormatZeroRemainError(removed));
        }

        return new ApplyResult(
            kept,
            FormatFilteredNote(kept.Count, removed),
            Error: null);
    }

    public static async Task<DispatchKind> ClassifyAsync(
        ReferenceLocation location,
        INamedTypeSymbol declaringType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(declaringType);

        if (!location.Location.IsInSource)
        {
            return DispatchKind.Unknown;
        }

        var document = location.Document;
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null || root is null)
        {
            return DispatchKind.Unknown;
        }

        var token = root.FindToken(location.Location.SourceSpan.Start);
        var node = token.Parent;
        var receiver = GetCallSiteReceiverType(semanticModel, node, cancellationToken);
        if (receiver is null)
        {
            return DispatchKind.Unknown;
        }

        return IsSameOrDerivedFrom(receiver, declaringType)
            ? DispatchKind.Direct
            : DispatchKind.Virtual;
    }

    public static ITypeSymbol? GetCallSiteReceiverType(
        SemanticModel semanticModel,
        SyntaxNode? node,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);
        if (node is null)
        {
            return null;
        }

        if (node is IdentifierNameSyntax or GenericNameSyntax)
        {
            if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
            {
                return GetExpressionType(semanticModel, memberAccess.Expression, cancellationToken);
            }

            if (node.Parent is MemberBindingExpressionSyntax binding && binding.Name == node)
            {
                var conditional = node.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>();
                if (conditional is not null)
                {
                    return GetExpressionType(semanticModel, conditional.Expression, cancellationToken);
                }
            }

            if (node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node)
            {
                return GetEnclosingNamedType(semanticModel, node.SpanStart, cancellationToken);
            }
        }

        if (node.Parent is MemberAccessExpressionSyntax ma && ma.Name.Span.IntersectsWith(node.Span))
        {
            return GetExpressionType(semanticModel, ma.Expression, cancellationToken);
        }

        return null;
    }

    public static bool IsSameOrDerivedFrom(ITypeSymbol? receiver, INamedTypeSymbol declaringType)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        if (receiver is null)
        {
            return false;
        }

        for (ITypeSymbol? current = receiver; current is not null; current = current.BaseType)
        {
            if (TypesMatch(current, declaringType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Equality via <see cref="SymbolEqualityComparer"/>, then fully-qualified display name
    /// (source vs metadata across projects), then <see cref="INamedTypeSymbol.ConstructedFrom"/>
    /// so closed <c>T&lt;int&gt;</c> matches open <c>T&lt;&gt;</c>.
    /// </summary>
    public static bool TypesMatch(ITypeSymbol left, ITypeSymbol right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (SymbolEqualityComparer.Default.Equals(left, right))
        {
            return true;
        }

        if (left is INamedTypeSymbol leftNamed && right is INamedTypeSymbol rightNamed
            && ConstructedFormsMatch(leftNamed, rightNamed))
        {
            return true;
        }

        return EqualByComparerOrFullyQualifiedName(left, right);
    }

    /// <summary>
    /// Closed <c>T&lt;int&gt;</c> matches open <c>T&lt;&gt;</c> (and vice versa).
    /// Two different closed forms (<c>T&lt;int&gt;</c> vs <c>T&lt;string&gt;</c>) do not match here.
    /// </summary>
    private static bool ConstructedFormsMatch(INamedTypeSymbol left, INamedTypeSymbol right)
    {
        if (left.IsGenericType && !IsOpenOrUnbound(left) && IsOpenOrUnbound(right)
            && EqualByComparerOrFullyQualifiedName(left.ConstructedFrom, right))
        {
            return true;
        }

        if (right.IsGenericType && !IsOpenOrUnbound(right) && IsOpenOrUnbound(left)
            && EqualByComparerOrFullyQualifiedName(right.ConstructedFrom, left))
        {
            return true;
        }

        return false;
    }

    private static bool EqualByComparerOrFullyQualifiedName(ITypeSymbol left, ITypeSymbol right)
    {
        if (SymbolEqualityComparer.Default.Equals(left, right))
        {
            return true;
        }

        var leftName = left.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var rightName = right.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return string.Equals(leftName, rightName, StringComparison.Ordinal);
    }

    private static bool IsOpenOrUnbound(INamedTypeSymbol type) =>
        type.IsUnboundGenericType
        || (type.IsGenericType && SymbolEqualityComparer.Default.Equals(type, type.ConstructedFrom));
    private static ITypeSymbol? GetExpressionType(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        CancellationToken cancellationToken)
    {
        var info = semanticModel.GetTypeInfo(expression, cancellationToken);
        return info.Type ?? info.ConvertedType;
    }

    private static INamedTypeSymbol? GetEnclosingNamedType(
        SemanticModel semanticModel,
        int position,
        CancellationToken cancellationToken)
    {
        for (var symbol = semanticModel.GetEnclosingSymbol(position, cancellationToken);
             symbol is not null;
             symbol = symbol.ContainingSymbol)
        {
            if (symbol is INamedTypeSymbol named)
            {
                return named;
            }
        }

        return null;
    }
}
