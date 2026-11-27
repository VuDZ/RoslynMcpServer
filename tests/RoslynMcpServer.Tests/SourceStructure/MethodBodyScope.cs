using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// The executed body of one method: its own statements without the contents of local functions and
/// lambdas, because a call inside a function that is never invoked is not an executed call. Queries
/// answer with bound symbols, so a required call on a receiver is a call on that symbol, not a name.
/// </summary>
internal sealed class MethodBodyScope
{
    public SourceSetAnalysis Analysis { get; }

    public IMethodSymbol Method { get; }

    /// <summary>Block body or the expression of an expression-bodied method.</summary>
    public SyntaxNode Body { get; }

    public SemanticModel Model { get; }

    /// <summary>Body nodes outside nested function scopes, in source order.</summary>
    public IReadOnlyList<SyntaxNode> Nodes { get; }

    public IReadOnlyList<InvocationExpressionSyntax> Invocations { get; }

    /// <summary>Target as <c>Type.Method</c>, as it appears in diagnostics.</summary>
    public string Display => $"{Method.ContainingType.Name}.{Method.Name}";

    private MethodBodyScope(SourceSetAnalysis analysis, IMethodSymbol method, SyntaxNode body)
    {
        Analysis = analysis;
        Method = method;
        Body = body;
        Nodes = DirectNodes(body).ToList();
        Invocations = Nodes.OfType<InvocationExpressionSyntax>().ToList();
        Model = analysis.ModelFor(body.SyntaxTree);
    }

    /// <summary>
    /// Locates the body of <paramref name="method"/> in the analyzed sources. A method without a body
    /// (abstract, extern, interface member) and a method declared outside the scope are diagnostics.
    /// </summary>
    public static (MethodBodyScope? Scope, string? Error) Create(SourceSetAnalysis analysis, IMethodSymbol method)
    {
        var display = $"{method.ContainingType.Name}.{method.Name}";
        var declarations = method.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<MethodDeclarationSyntax>()
            .ToList();
        if (declarations.Count == 0)
        {
            return (null, $"{display}: declaration is not part of the analyzed scope");
        }

        if (declarations.Count > 1)
        {
            return (null, $"{display}: {declarations.Count} declarations found; this check requires a single declaration");
        }

        var declarationSyntax = declarations[0];
        var body = (SyntaxNode?)declarationSyntax.Body ?? declarationSyntax.ExpressionBody?.Expression;
        if (body is null)
        {
            return (null, $"{display}: method has no body to check");
        }

        return (new MethodBodyScope(analysis, method, body), null);
    }

    /// <summary>
    /// Body nodes without the contents of local functions and lambdas. The root itself is included: an
    /// expression body that is a single call (<c>=&gt; Helper()</c>) is the invocation, and
    /// <see cref="SyntaxNode.DescendantNodes()"/> would skip it.
    /// </summary>
    public static IEnumerable<SyntaxNode> DirectNodes(SyntaxNode root)
    {
        var nodes = root.DescendantNodes(descendIntoChildren: node => !IsDeferredScope(node));
        return IsDeferredScope(root) ? nodes : nodes.Prepend(root);
    }

    /// <summary>
    /// Statements of a deferred scope, or a regular scope unchanged. A lambda must be entered through its
    /// body: the traversal stops descending at a deferred node, so the lambda node itself has no
    /// descendants to visit.
    /// </summary>
    public static SyntaxNode ScopeBody(SyntaxNode scope) => scope switch
    {
        LambdaExpressionSyntax lambda => lambda.Body,
        AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
        LocalFunctionStatementSyntax localFunction => (SyntaxNode?)localFunction.Body
            ?? localFunction.ExpressionBody?.Expression
            ?? (SyntaxNode)localFunction,
        _ => scope,
    };

    /// <summary>Local function or lambda: its body runs where it is invoked, not where it is written.</summary>
    public static bool IsDeferredScope(SyntaxNode node) =>
        node is LocalFunctionStatementSyntax or LambdaExpressionSyntax or AnonymousMethodExpressionSyntax;

    /// <summary>
    /// Call target in its declared form: a reduced extension method is replaced by the static method it
    /// reduces from, so its declaration can be found and its body traversed. The mapping keeps symbol
    /// identity of the declaration, which is what every comparison uses.
    /// </summary>
    public static IMethodSymbol DeclaredForm(IMethodSymbol method) =>
        method.ReducedFrom is { } reduced ? DeclaredForm(reduced) : method;

    /// <summary>Name references in the immediate body with the given identifier text.</summary>
    public IReadOnlyList<SourceReference> ReferencesNamed(string name) =>
        Nodes
            .OfType<SimpleNameSyntax>()
            .Where(node => string.Equals(node.Identifier.ValueText, name, StringComparison.Ordinal))
            .Select(node => SourceReference.From(Model, node))
            .ToList();

    /// <summary>
    /// Name references in the immediate body that bind to <paramref name="symbol"/>. A candidate symbol
    /// counts as well: <c>nameof(Entry)</c> reports the member group as a candidate instead of binding
    /// it, and losing that mention would weaken the standing prohibition.
    /// </summary>
    public IReadOnlyList<SourceReference> ReferencesTo(ISymbol symbol) =>
        Nodes
            .OfType<SimpleNameSyntax>()
            .Select(node => SourceReference.From(Model, node))
            .Where(reference => RefersTo(reference, symbol))
            .ToList();

    private static bool RefersTo(SourceReference reference, ISymbol symbol) =>
        (reference.Symbol is not null && IsSameMember(reference.Symbol, symbol))
        || reference.Candidates.Any(candidate => IsSameMember(candidate, symbol));

    /// <summary>Symbol of a name or member-access expression, or null when it does not bind.</summary>
    public ISymbol? SymbolOf(SyntaxNode node) => node is ExpressionSyntax expression ? Model.GetSymbolInfo(expression).Symbol : null;

    public IMethodSymbol? CalledMethod(InvocationExpressionSyntax invocation) =>
        SymbolOf(invocation.Expression) as IMethodSymbol;

    /// <summary>
    /// Invocations of the immediate body whose target symbol did not bind. Such a call is a blind spot:
    /// it could be the forbidden call the check is looking for, so it is reported instead of ignored.
    /// <c>nameof(...)</c> is not an invocation and is excluded.
    /// </summary>
    public IReadOnlyList<string> UnresolvedCalls()
    {
        var diagnostics = new List<string>();
        foreach (var invocation in Invocations)
        {
            if (IsNameOf(invocation))
            {
                continue;
            }

            var info = Model.GetSymbolInfo(invocation.Expression);
            if (info.Symbol is not null)
            {
                continue;
            }

            var reason = info.CandidateReason == CandidateReason.None
                ? "no symbol bound"
                : $"{info.CandidateReason}: {string.Join(", ", info.CandidateSymbols.Select(candidate => candidate.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))}";
            diagnostics.Add(
                $"{Display}: call at {SourceSetAnalysis.Describe(invocation)} does not resolve ({reason}); "
                + "reachability and prohibition checks are undecidable here");
        }

        return diagnostics;
    }

    public static bool IsNameOf(InvocationExpressionSyntax invocation) =>
        invocation.Expression is IdentifierNameSyntax name
        && string.Equals(name.Identifier.ValueText, "nameof", StringComparison.Ordinal)
        && invocation.ArgumentList.Arguments.Count == 1;

    private static bool IsSameMember(ISymbol candidate, ISymbol target) =>
        SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, target.OriginalDefinition);
}
