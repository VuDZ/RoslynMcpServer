using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcpServer.Services.Testing.Baseline;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using RoslynMcpServer.Services.Testing.Models;

namespace RoslynMcpServer.Services.Testing;

internal static class VstestTestInventory
{
    internal static async Task<IReadOnlyList<VstestTestClass>> BuildAsync(Solution solution, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<VstestTestClass>();
        foreach (var project in solution.Projects.Where(project => project.Language == LanguageNames.CSharp))
        {
            var types = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var tests = new Dictionary<ISymbol, BaselineMemberKey>(SymbolEqualityComparer.Default);
            var complete = true;
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (root is null || model is null)
                {
                    complete = false;
                    continue;
                }
                complete &= !root.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
                foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol type)
                    {
                        types.Add(type);
                    }
                }
                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (TestDiscoveryHelper.HasTestAttribute(method, model)
                        && model.GetDeclaredSymbol(method, cancellationToken) is IMethodSymbol symbol)
                    {
                        tests[symbol.OriginalDefinition] = BaselineDocumentSnapshotBuilder.Key(method, method.Identifier.ValueText);
                    }
                }
            }
            foreach (var type in types.Where(type => !type.IsAbstract && type.TypeKind == TypeKind.Class))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (keys, inheritedComplete) = TestsOf(type, tests, cancellationToken);
                if (keys.Count == 0)
                {
                    continue;
                }
                var (name, arguments, reason) = RuntimeIdentity(type);
                result.Add(new VstestTestClass(project.Id, keys, name, complete && inheritedComplete, reason, arguments));
            }
        }
        return result;
    }

    private static (IReadOnlyList<BaselineMemberKey> Keys, bool Complete) TestsOf(
        INamedTypeSymbol type,
        IReadOnlyDictionary<ISymbol, BaselineMemberKey> tests,
        CancellationToken cancellationToken)
    {
        var keys = new HashSet<BaselineMemberKey>();
        var complete = true;
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
            {
                if (tests.TryGetValue(method.OriginalDefinition, out var key))
                {
                    keys.Add(key);
                }
                else if (method.GetAttributes().Any(attribute => TestAttributeMatcher.IsTestAttributeType(attribute.AttributeClass)))
                {
                    // Referenced source bases belong to another compilation, outside this project's method dictionary.
                    var declaration = method.OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault()
                        ?.GetSyntax(cancellationToken) as MethodDeclarationSyntax;
                    if (declaration is null)
                    {
                        complete = false;
                    }
                    else
                    {
                        keys.Add(BaselineDocumentSnapshotBuilder.Key(declaration, declaration.Identifier.ValueText));
                    }
                }
            }
        }
        return (keys.ToArray(), complete);
    }

    private static (string? Name, bool Arguments, string? Reason) RuntimeIdentity(INamedTypeSymbol type)
    {
        var attributes = new List<AttributeData>();
        for (var owner = type; owner is not null; owner = owner.BaseType)
        {
            attributes.AddRange(owner.GetAttributes());
        }
        var fixtures = attributes.Where(attribute => IsNUnit(attribute, "TestFixtureAttribute")).ToArray();
        var source = attributes.Any(attribute => IsNUnit(attribute, "TestFixtureSourceAttribute"));
        var values = fixtures.Select(attribute => attribute.ConstructorArguments
            .SelectMany(value => value.Kind == TypedConstantKind.Array ? value.Values : [value]).ToArray()).ToArray();
        var arguments = source || values.Any(items => items.Length > type.Arity
            || items.Any(item => item.Kind != TypedConstantKind.Type));
        if (arguments)
        {
            return (null, true, "Runtime fixture constructor/source arguments require the whole project.");
        }
        var name = RuntimeTypeName(type);
        if (type.Arity == 0 && !HasGenericContainer(type))
        {
            return (name, false, null);
        }

        // shortcut: only the measured NUnit Int32 fixture form is known, extend after measuring other adapter forms.
        if (type.Arity == 1 && type.ContainingType is null && values.Length == 1 && values[0].Length == 1
            && values[0][0].Value is ITypeSymbol { SpecialType: SpecialType.System_Int32 })
        {
            return (name + "<Int32>", false, null);
        }
        return (null, false, "Unknown or unmeasured closed generic runtime identity; run the whole project.");
    }

    private static bool IsNUnit(AttributeData attribute, string name) =>
        attribute.AttributeClass is { } type && type.Name == name && type.ContainingNamespace.ToDisplayString() == "NUnit.Framework";

    private static bool HasGenericContainer(INamedTypeSymbol type) => type.ContainingType is { } parent
        && (parent.Arity > 0 || HasGenericContainer(parent));

    private static string RuntimeTypeName(INamedTypeSymbol type) => type.ContainingType is { } parent
        ? RuntimeTypeName(parent) + "+" + type.Name
        : (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".") + type.Name;
}
