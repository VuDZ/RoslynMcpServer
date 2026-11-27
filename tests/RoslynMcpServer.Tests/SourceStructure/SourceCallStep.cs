using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// One executed call edge on a reachability path: the method containing the call site and the called
/// method as bound by the compiler, so overloads and same-named members of other types stay distinct.
/// </summary>
internal sealed record SourceCallStep(IMethodSymbol Caller, IMethodSymbol Callee, string FilePath, int Line)
{
    public static SourceCallStep At(IMethodSymbol caller, IMethodSymbol callee, Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax invocation) =>
        new(caller, callee, invocation.SyntaxTree.FilePath, invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1);

    public string Describe() =>
        $"{Caller.ContainingType.Name}.{Caller.Name} -> {Callee.ContainingType.Name}.{Callee.Name} at {FilePath}:{Line}";
}
