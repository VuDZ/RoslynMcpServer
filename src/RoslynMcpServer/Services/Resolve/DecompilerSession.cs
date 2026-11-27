using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.TypeSystem;

namespace RoslynMcpServer.Services.Resolve;

/// <summary>A decompiler for one assembly together with the references its resolver could not find.</summary>
public sealed class DecompilerSession
{
    public CSharpDecompiler Decompiler { get; }

    /// <summary>
    /// References shown as unknown types in the output, as <c>Name Version</c>. Read it after decompiling:
    /// the type system may resolve further references while decompiling.
    /// </summary>
    public IReadOnlyList<string> UnresolvedReferences => _resolver.UnresolvedReferences;

    internal DecompilerSession(
        CSharpDecompiler decompiler,
        NuGetFallbackAssemblyResolver resolver,
        CSharpFormattingOptions formattingOptions)
    {
        Decompiler = decompiler;
        _resolver = resolver;
        _formattingOptions = formattingOptions;
    }

    internal string FormatCode(SyntaxTree syntaxTree)
    {
        var code = syntaxTree.ToString(_formattingOptions);
        return syntaxTree.Descendants.Any(IsInvalidCSharp)
            ? $"// Incomplete decompilation: ILSpy could not reconstruct valid C# for every instruction.{Environment.NewLine}{code}"
            : code;
    }

    private static bool IsInvalidCSharp(AstNode node) => node switch
    {
        ErrorExpression => true,
        // ILSpy can leave lowered runtime-async catch dispatch that throws a System.Object.
        // https://github.com/icsharpcode/ILSpy/issues/3745
        ThrowExpression expression => expression.Expression.GetResolveResult().Type.IsKnownType(KnownTypeCode.Object),
        ThrowStatement { Expression: { } expression } => expression.GetResolveResult().Type.IsKnownType(KnownTypeCode.Object),
        _ => false,
    };

    private readonly NuGetFallbackAssemblyResolver _resolver;
    private readonly CSharpFormattingOptions _formattingOptions;
}
