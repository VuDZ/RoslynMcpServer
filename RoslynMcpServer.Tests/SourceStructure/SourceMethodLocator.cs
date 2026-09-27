using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Locates a type and a method declaration in C# source through the syntax tree and walks the nodes
/// of the found body. A parse error, a missing target and an ambiguous target are returned as
/// diagnostic text: a method that was not found must not look like a satisfied contract.
/// </summary>
internal static class SourceMethodLocator
{
    /// <summary>Parses the source and locates the type by namespace and exact name.</summary>
    public static (TypeDeclarationSyntax? Type, string? Error) LocateType(SourceTarget target)
    {
        var tree = CSharpSyntaxTree.ParseText(target.SourceText, path: target.SourcePath);
        var syntaxErrors = tree.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(Describe)
            .ToList();
        if (syntaxErrors.Count > 0)
        {
            return (null, $"{target.SourcePath}: исходник не разобран: {string.Join("; ", syntaxErrors)}");
        }

        var candidates = tree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(type => type.Identifier.ValueText == target.TypeName && NamespaceOf(type) == target.NamespaceName)
            .ToList();
        if (candidates.Count == 0)
        {
            return (null, $"{target.NamespaceName}.{target.TypeName}: тип не найден в {target.SourcePath}");
        }

        if (candidates.Count > 1)
        {
            return (null,
                $"{target.NamespaceName}.{target.TypeName}: найдено {candidates.Count} объявлений типа; "
                + $"partial-типы эта проверка не объединяет ({target.SourcePath})");
        }

        return (candidates[0], null);
    }

    /// <summary>
    /// Locates a method of the type by exact name. Overloads produce a diagnostic instead of a random
    /// pick: a contract check must not silently verify the wrong method.
    /// </summary>
    public static (MethodDeclarationSyntax? Method, string? Error) LocateMethod(
        TypeDeclarationSyntax type,
        string typeDisplayName,
        string methodName)
    {
        var candidates = type.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText == methodName)
            .ToList();
        if (candidates.Count == 0)
        {
            return (null, $"{typeDisplayName}.{methodName}: метод не найден");
        }

        if (candidates.Count > 1)
        {
            return (null,
                $"{typeDisplayName}.{methodName}: объявлений с этим именем — {candidates.Count}, "
                + "нужна однозначная сигнатура");
        }

        return (candidates[0], null);
    }

    /// <summary>Method body or its expression body; null when there is nothing to check.</summary>
    public static SyntaxNode? TryGetScope(MethodDeclarationSyntax method) =>
        (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression;

    /// <summary>
    /// Body nodes without the contents of local functions and lambdas: a call inside an uncalled
    /// function or a lambda is not a call in the immediate method body. Transitive calls are not
    /// examined here.
    /// </summary>
    public static IEnumerable<SyntaxNode> DirectNodes(SyntaxNode scope) =>
        scope.DescendantNodes(descendIntoChildren: node => !IsDeferredScope(node));

    public static IEnumerable<InvocationExpressionSyntax> DirectInvocations(SyntaxNode scope) =>
        DirectNodes(scope).OfType<InvocationExpressionSyntax>();

    /// <summary>
    /// Name of the invoked member: <c>Wait</c> for <c>_workspaceLock.Wait()</c> and <c>Helper</c> for
    /// <c>Helper()</c>.
    /// </summary>
    public static string? InvokedMemberName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            SimpleNameSyntax name => name.Identifier.ValueText,
            _ => null,
        };

    /// <summary>
    /// Receiver of the call without the <c>this.</c> qualifier; null when the call has none or is
    /// qualified with <c>this</c> alone.
    /// </summary>
    public static string? ReceiverText(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax access
            ? NormalizeReceiver(access.Expression.ToString())
            : null;

    /// <summary>True for <c>Helper()</c> and <c>this.Helper()</c>.</summary>
    public static bool IsSelfCall(InvocationExpressionSyntax invocation, string memberName) =>
        InvokedMemberName(invocation) == memberName && string.IsNullOrEmpty(ReceiverText(invocation));

    /// <summary>True for <c>receiver.Member(...)</c> and <c>this.receiver.Member(...)</c>.</summary>
    public static bool IsMemberCall(InvocationExpressionSyntax invocation, string receiver, string memberName) =>
        InvokedMemberName(invocation) == memberName
        && string.Equals(ReceiverText(invocation), receiver, StringComparison.Ordinal);

    /// <summary>
    /// References to the whole name: both a call and a method group. The comparison uses the full
    /// identifier, so <c>Results</c> does not count as a reference to <c>Result</c>.
    /// </summary>
    public static IReadOnlyList<SimpleNameSyntax> DirectNameReferences(SyntaxNode scope, string name) =>
        DirectNodes(scope)
            .OfType<SimpleNameSyntax>()
            .Where(node => string.Equals(node.Identifier.ValueText, name, StringComparison.Ordinal))
            .ToList();

    /// <summary>True when the node lies entirely inside <paramref name="candidate"/>.</summary>
    public static bool IsInside(SyntaxNode node, SyntaxNode candidate) =>
        candidate.Span.Contains(node.Span);

    public static int LineOf(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    /// <summary>
    /// <c>this.Helper()</c> renders its receiver as <c>this</c>, not as <c>this.</c>, so the bare
    /// qualifier is normalized to null as well: without that, an equivalent <c>this.</c> qualification
    /// would look like a call on some other receiver.
    /// </summary>
    private static string? NormalizeReceiver(string receiver) =>
        receiver switch
        {
            "this" => null,
            _ when receiver.StartsWith("this.", StringComparison.Ordinal) => receiver["this.".Length..],
            _ => receiver,
        };

    private static string Describe(Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource)
        {
            return $"{diagnostic.Id} {diagnostic.GetMessage()}";
        }

        var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
        return $"{diagnostic.Id} {diagnostic.GetMessage()} (строка {line})";
    }

    /// <summary>Local function or lambda: its body runs somewhere other than where it is declared.</summary>
    private static bool IsDeferredScope(SyntaxNode node) =>
        node is LocalFunctionStatementSyntax or LambdaExpressionSyntax or AnonymousMethodExpressionSyntax;

    private static string NamespaceOf(TypeDeclarationSyntax type) =>
        type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? string.Empty;
}
