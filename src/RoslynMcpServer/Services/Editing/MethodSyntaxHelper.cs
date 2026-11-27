using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Services.Editing;

internal static class MethodSyntaxHelper
{
    public static MethodDeclarationSyntax FindMethod(
        ClassDeclarationSyntax classDecl,
        string methodName,
        IReadOnlyList<string>? parameterTypes,
        SemanticModel? model)
    {
        var candidates = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(m => string.Equals(m.Identifier.Text, methodName, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"Method `{methodName}` not found in class `{classDecl.Identifier.Text}`.");
        }

        // A supplied signature constrains the target even when only one candidate exists.
        if (parameterTypes is null && candidates.Count == 1)
        {
            return candidates[0];
        }

        if (parameterTypes is null)
        {
            throw new InvalidOperationException(BuildOverloadError(
                methodName,
                "Specify parameterTypes to disambiguate overloads.",
                candidates));
        }

        var matches = candidates
            .Where(m => ParametersMatch(m, parameterTypes, model))
            .ToList();

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(BuildOverloadError(
                methodName,
                $"No overload matches parameterTypes [{string.Join(", ", parameterTypes)}].",
                candidates));
        }

        throw new InvalidOperationException(BuildOverloadError(
            methodName,
            "Multiple overloads match parameterTypes; refine the type list.",
            matches));
    }

    public static BlockSyntax ParseNewMethodBody(string newBody)
    {
        if (string.IsNullOrWhiteSpace(newBody))
        {
            throw new ArgumentException("newBody is empty.");
        }

        var trimmed = newBody.Trim();
        var parsed = trimmed.StartsWith('{')
            ? SyntaxFactory.ParseStatement(trimmed)
            : SyntaxFactory.ParseStatement($"{{ {trimmed} }}");

        if (parsed is not BlockSyntax block)
        {
            throw new InvalidOperationException("newBody must parse to a block of statements.");
        }

        ThrowIfSyntaxErrors(block, "newBody");
        return block;
    }

    public static void ThrowIfSyntaxErrors(SyntaxNode node, string contextLabel)
    {
        var errors = node.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToList();

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"{contextLabel} has syntax errors: {string.Join("; ", errors)}");
        }
    }

    public static string FormatSignature(MethodDeclarationSyntax method)
    {
        var parameters = method.ParameterList.Parameters
            .Select(p => p.Type?.ToString() ?? "?")
            .ToArray();
        return $"{method.Identifier.Text}({string.Join(", ", parameters)})";
    }

    private static string BuildOverloadError(
        string methodName,
        string headline,
        IReadOnlyList<MethodDeclarationSyntax> candidates)
    {
        var signatures = string.Join(
            Environment.NewLine,
            candidates.Select(m => $"- {FormatSignature(m)}"));
        return $"Method `{methodName}`: {headline}{Environment.NewLine}Available signatures:{Environment.NewLine}{signatures}";
    }

    private static bool ParametersMatch(
        MethodDeclarationSyntax method,
        IReadOnlyList<string> expectedTypes,
        SemanticModel? model)
    {
        var parameters = method.ParameterList.Parameters;
        if (parameters.Count != expectedTypes.Count)
        {
            return false;
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            var paramType = parameters[i].Type;
            if (paramType is null || model is null || !TypeNamesEquivalent(paramType, expectedTypes[i], model))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TypeNamesEquivalent(TypeSyntax parameterType, string expectedType, SemanticModel model)
    {
        var selector = SyntaxFactory.ParseTypeName(expectedType);
        if (selector.ContainsDiagnostics)
        {
            return false;
        }

        // Display names lose the namespaces of generic arguments and the owners of nested types.
        // See docs/project-review-2026-11-25/task-39-p1-bind-overload-type-selector-hi.md.
        var binding = model.GetSpeculativeSymbolInfo(parameterType.SpanStart, selector, SpeculativeBindingOption.BindAsTypeOrNamespace);
        var requestedType = binding.Symbol as ITypeSymbol;
        if (selector is IdentifierNameSyntax name && binding.Symbol is null && binding.CandidateReason == CandidateReason.None)
        {
            // Preserve short CLR names such as Int32 without a using System directive.
            var coreLibrary = model.Compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
            requestedType = coreLibrary.GetTypeByMetadataName($"System.{name.Identifier.ValueText}");
        }

        return requestedType is not null && IsResolved(requestedType)
            && SymbolEqualityComparer.Default.Equals(requestedType, model.GetTypeInfo(parameterType).Type);
    }

    private static bool IsResolved(ITypeSymbol type) => type switch
    {
        IErrorTypeSymbol => false,
        INamedTypeSymbol named => (named.ContainingType is null || IsResolved(named.ContainingType))
            && named.TypeArguments.All(IsResolved),
        IArrayTypeSymbol array => IsResolved(array.ElementType),
        IPointerTypeSymbol pointer => IsResolved(pointer.PointedAtType),
        IFunctionPointerTypeSymbol pointer => IsResolved(pointer.Signature.ReturnType)
            && pointer.Signature.Parameters.All(parameter => IsResolved(parameter.Type)),
        _ => true
    };
}
