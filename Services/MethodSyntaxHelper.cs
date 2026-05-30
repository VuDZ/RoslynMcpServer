using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Services;

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

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        if (parameterTypes is null || parameterTypes.Count == 0)
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
            if (paramType is null)
            {
                return false;
            }

            if (!TypeNamesEquivalent(paramType.ToString(), expectedTypes[i].Trim(), paramType, model))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TypeNamesEquivalent(
        string syntaxTypeName,
        string expected,
        TypeSyntax paramType,
        SemanticModel? model)
    {
        if (string.Equals(syntaxTypeName, expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (model is null)
        {
            return false;
        }

        var symbol = model.GetTypeInfo(paramType).Type;
        if (symbol is null)
        {
            return false;
        }

        var minimal = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var fullyQualified = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return string.Equals(minimal, expected, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullyQualified, expected, StringComparison.OrdinalIgnoreCase)
            || string.Equals(symbol.Name, expected, StringComparison.OrdinalIgnoreCase);
    }
}
