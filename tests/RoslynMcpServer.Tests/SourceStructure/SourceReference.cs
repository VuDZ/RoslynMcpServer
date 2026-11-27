using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// One name reference found in the analyzed scope. The symbol is the verdict: an unresolved name is
/// recorded with its candidate reason instead of being dropped, so a check can report it as a
/// diagnostic rather than as absence of the reference it was looking for.
/// </summary>
internal sealed record SourceReference(
    ISymbol? Symbol,
    IReadOnlyList<ISymbol> Candidates,
    CandidateReason CandidateReason,
    SimpleNameSyntax Node)
{
    public bool IsResolved => Symbol is not null;

    public string FilePath => Node.SyntaxTree.FilePath;

    /// <summary>1-based line of the reference; line numbers are unaffected by line-ending normalization.</summary>
    public int Line => Node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    /// <summary>Member name as written at the reference site.</summary>
    public string Name => Node.Identifier.ValueText;

    public static SourceReference From(SemanticModel model, SimpleNameSyntax node)
    {
        var info = model.GetSymbolInfo(node);
        return new SourceReference(info.Symbol, info.CandidateSymbols, info.CandidateReason, node);
    }

    /// <summary>Reference site plus what the name bound to; used in violation and diagnostic text.</summary>
    public string Describe()
    {
        if (Symbol is not null)
        {
            return $"{FilePath}:{Line} `{Node}` -> {Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}";
        }

        var reason = CandidateReason == CandidateReason.None ? "unresolved" : CandidateReason.ToString();
        return $"{FilePath}:{Line} `{Node}` -> {reason}";
    }
}
