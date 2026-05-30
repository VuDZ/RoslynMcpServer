using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcpServer.Services;

public static class TestFilterHelper
{
    public static async Task<(string Filter, string Description)> BuildFilterAsync(
        Solution? solution,
        string? className,
        string? methodName,
        CancellationToken cancellationToken)
    {
        var trimmedClass = string.IsNullOrWhiteSpace(className) ? null : className.Trim();
        var trimmedMethod = string.IsNullOrWhiteSpace(methodName) ? null : methodName.Trim();

        if (trimmedClass is null && trimmedMethod is null)
        {
            throw new ArgumentException("Provide at least one of `className` or `methodName`.");
        }

        if (solution is not null)
        {
            var resolved = await TryResolveFullyQualifiedTestNameAsync(
                solution, trimmedClass, trimmedMethod, cancellationToken).ConfigureAwait(false);
            if (resolved is not null)
            {
                if (trimmedMethod is null && trimmedClass is not null)
                {
                    return ($"FullyQualifiedName~{EscapeContains(resolved)}", $"Roslyn-resolved class `{resolved}`");
                }

                return ($"FullyQualifiedName={EscapeExact(resolved)}", $"Roslyn-resolved FQN `{resolved}`");
            }
        }

        if (trimmedClass is not null && trimmedMethod is not null)
        {
            var suffix = BuildSuffix(trimmedClass, trimmedMethod);
            return ($"FullyQualifiedName~{EscapeContains(suffix)}", $"Name suffix `.{trimmedClass}.{trimmedMethod}`");
        }

        if (trimmedClass is not null)
        {
            return ($"FullyQualifiedName~{EscapeContains(trimmedClass)}", $"Class name contains `{trimmedClass}`");
        }

        return ($"FullyQualifiedName~.{EscapeContains(trimmedMethod!)}", $"Method name suffix `.{trimmedMethod}`");
    }

    private static async Task<string?> TryResolveFullyQualifiedTestNameAsync(
        Solution solution,
        string? className,
        string? methodName,
        CancellationToken cancellationToken)
    {
        INamedTypeSymbol? typeSymbol = null;

        if (className is not null)
        {
            typeSymbol = await FindTestClassAsync(solution, className, cancellationToken).ConfigureAwait(false);
        }
        else if (methodName is not null)
        {
            var methods = await FindTestMethodsAsync(solution, methodName, cancellationToken).ConfigureAwait(false);
            if (methods.Count == 1)
            {
                return FormatFullyQualifiedName(methods[0]);
            }

            return null;
        }

        if (typeSymbol is null)
        {
            return null;
        }

        if (methodName is null)
        {
            return FormatFullyQualifiedName(typeSymbol);
        }

        var candidates = typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal) && IsTestMethod(m))
            .ToList();

        return candidates.Count == 1 ? FormatFullyQualifiedName(candidates[0]) : null;
    }

    private static async Task<INamedTypeSymbol?> FindTestClassAsync(
        Solution solution,
        string className,
        CancellationToken cancellationToken)
    {
        var declarations = new List<ISymbol>();
        foreach (var projectId in solution.ProjectIds)
        {
            var project = solution.GetProject(projectId);
            if (project is null)
            {
                continue;
            }

            var found = await SymbolFinder.FindDeclarationsAsync(
                project,
                className,
                ignoreCase: true,
                SymbolFilter.Type,
                cancellationToken).ConfigureAwait(false);
            declarations.AddRange(found);
        }

        return declarations
            .OfType<INamedTypeSymbol>()
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<INamedTypeSymbol>()
            .FirstOrDefault(t => string.Equals(t.Name, className, StringComparison.OrdinalIgnoreCase)
                || string.Equals(FormatFullyQualifiedName(t), className, StringComparison.Ordinal)
                || FormatFullyQualifiedName(t).EndsWith("." + className, StringComparison.Ordinal));
    }

    private static async Task<List<IMethodSymbol>> FindTestMethodsAsync(
        Solution solution,
        string methodName,
        CancellationToken cancellationToken)
    {
        var results = new List<IMethodSymbol>();
        foreach (var projectId in solution.ProjectIds)
        {
            var project = solution.GetProject(projectId);
            if (project is null)
            {
                continue;
            }

            var found = await SymbolFinder.FindDeclarationsAsync(
                project,
                methodName,
                ignoreCase: true,
                SymbolFilter.Member,
                cancellationToken).ConfigureAwait(false);

            results.AddRange(found
                .OfType<IMethodSymbol>()
                .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal) && IsTestMethod(m)));
        }

        return results
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<IMethodSymbol>()
            .ToList();
    }

    private static bool IsTestMethod(IMethodSymbol method)
    {
        return method.GetAttributes().Any(static attribute =>
        {
            var name = attribute.AttributeClass?.Name;
            return name is "FactAttribute" or "TheoryAttribute" or "TestAttribute" or "TestMethodAttribute"
                or "TestCaseAttribute" or "DataTestMethodAttribute" or "DataRowAttribute";
        });
    }

    private static string FormatFullyQualifiedName(ISymbol symbol)
    {
        var formatted = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return formatted.StartsWith("global::", StringComparison.Ordinal)
            ? formatted["global::".Length..]
            : formatted;
    }

    private static string BuildSuffix(string className, string methodName)
    {
        if (className.Contains('.', StringComparison.Ordinal))
        {
            return $"{className}.{methodName}";
        }

        return $".{className}.{methodName}";
    }

    private static string EscapeExact(string value) => value;

    private static string EscapeContains(string value) => value;
}
