using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Services;

/// <summary>
/// Single source of truth for "is this attribute a test attribute", shared by
/// <see cref="TestDiscoveryHelper"/> (<c>get_test_list</c>) and <see cref="TestFilterHelper"/>
/// (<c>run_specific_test</c> / <c>run_test_by_filter</c>). Keeping one matcher prevents the
/// discovery list and the filter resolution from drifting apart.
/// </summary>
internal static class TestAttributeMatcher
{
    /// <summary>
    /// Root attributes of the supported frameworks, without the <c>Attribute</c> suffix. Custom
    /// attributes are matched by walking the base chain, so <c>[WpfFact]</c> /
    /// <c>[AnalyzerLifecycleFact]</c> count as tests. MSTest <c>DataTestMethodAttribute</c> is
    /// covered by <c>TestMethodAttribute</c>; <c>DataRowAttribute</c> is deliberately **not** here —
    /// it does not mark a test on its own.
    /// </summary>
    private static readonly HashSet<string> TestAttributeRoots = new(StringComparer.Ordinal)
    {
        "Fact",           // xUnit (FactAttribute)
        "Theory",         // xUnit (TheoryAttribute)
        "Test",           // NUnit (TestAttribute)
        "TestCase",       // NUnit (TestCaseAttribute)
        "TestCaseSource", // NUnit (TestCaseSourceAttribute)
        "TestMethod"      // MSTest (TestMethodAttribute)
    };

    /// <summary>True when <paramref name="type"/> or any base type is a framework test attribute.</summary>
    internal static bool IsTestAttributeType(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (IsTestAttributeRootName(current.OriginalDefinition.Name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the attribute *type* for a syntax node. <c>GetSymbolInfo</c> on an
    /// <see cref="AttributeSyntax"/> returns the attribute constructor whose <c>Name</c> is
    /// <c>.ctor</c>, so the containing type (not the symbol name) is the useful answer.
    /// Returns <c>null</c> when the attribute did not bind, so callers fall back to
    /// <see cref="IsTestAttributeSyntaxName"/>.
    /// </summary>
    internal static INamedTypeSymbol? ResolveAttributeType(AttributeSyntax attr, SemanticModel model)
    {
        var symbol = model.GetSymbolInfo(attr).Symbol;
        var type = symbol switch
        {
            IMethodSymbol constructor => constructor.ContainingType,
            INamedTypeSymbol named => named,
            _ => model.GetTypeInfo(attr).Type as INamedTypeSymbol
        };

        // Without metadata references Roslyn still produces an *error* type (e.g. `Fact`), whose
        // base chain is empty. Treat that as unbound so the syntactic name decides.
        return type is null || type.TypeKind == TypeKind.Error ? null : type;
    }

    /// <summary>
    /// Fallback for a workspace where the attribute did not bind at all: strip any qualifier and
    /// the <c>Attribute</c> suffix, then compare against the roots. The hierarchy is unknowable
    /// here, so a *custom* derived attribute is not matched — an unavoidable limit of an unbound
    /// compilation, not an over-broad matcher.
    /// </summary>
    internal static bool IsTestAttributeSyntaxName(string syntaxName) =>
        IsTestAttributeRootName(syntaxName);

    private static bool IsTestAttributeRootName(string typeOrSyntaxName)
    {
        var simple = typeOrSyntaxName.Trim();
        var lastDot = simple.LastIndexOf('.');
        if (lastDot >= 0)
        {
            simple = simple[(lastDot + 1)..];
        }

        // C# attribute lookup accepts both `Fact` and `FactAttribute`; normalize both spellings.
        if (simple.EndsWith("Attribute", StringComparison.Ordinal))
        {
            simple = simple[..^"Attribute".Length];
        }

        return TestAttributeRoots.Contains(simple);
    }
}
