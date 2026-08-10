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
            var resolved = await TryResolveVstestFullyQualifiedNameAsync(
                solution, trimmedClass, trimmedMethod, cancellationToken).ConfigureAwait(false);
            if (resolved is not null)
            {
                // Always use contains (~): VSTest treats () in exact (=) values as expression grouping,
                // and Theories often append parameter text to the FQN.
                var matchKind = trimmedMethod is null ? "class" : "FQN";
                return (
                    $"FullyQualifiedName~{EscapeFilterValue(resolved)}",
                    $"Roslyn-resolved {matchKind} `{resolved}`");
            }
        }

        if (trimmedClass is not null && trimmedMethod is not null)
        {
            var suffix = BuildClassMethodContainsNeedle(trimmedClass, trimmedMethod);
            return (
                $"FullyQualifiedName~{EscapeFilterValue(suffix)}",
                $"Name suffix `{suffix}`");
        }

        if (trimmedClass is not null)
        {
            return (
                $"FullyQualifiedName~{EscapeFilterValue(trimmedClass)}",
                $"Class name contains `{trimmedClass}`");
        }

        var methodNeedle = BuildSimpleOrDottedContainsNeedle(trimmedMethod!);
        return (
            $"FullyQualifiedName~{EscapeFilterValue(methodNeedle)}",
            $"Method name contains `{methodNeedle}`");
    }

    private static async Task<string?> TryResolveVstestFullyQualifiedNameAsync(
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
            var simpleMethodName = GetSimpleName(methodName);
            var methods = await FindTestMethodsAsync(solution, simpleMethodName, cancellationToken).ConfigureAwait(false);
            if (methods.Count == 1)
            {
                return FormatVstestFullyQualifiedName(methods[0]);
            }

            return null;
        }

        if (typeSymbol is null)
        {
            return null;
        }

        if (methodName is null)
        {
            return FormatVstestFullyQualifiedName(typeSymbol);
        }

        var simpleName = GetSimpleName(methodName);
        var candidates = typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => string.Equals(m.Name, simpleName, StringComparison.Ordinal) && IsTestMethod(m))
            .ToList();

        return candidates.Count == 1 ? FormatVstestFullyQualifiedName(candidates[0]) : null;
    }

    private static async Task<INamedTypeSymbol?> FindTestClassAsync(
        Solution solution,
        string className,
        CancellationToken cancellationToken)
    {
        var simpleName = GetSimpleName(className);
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
                simpleName,
                ignoreCase: true,
                SymbolFilter.Type,
                cancellationToken).ConfigureAwait(false);
            declarations.AddRange(found);
        }

        return declarations
            .OfType<INamedTypeSymbol>()
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<INamedTypeSymbol>()
            .FirstOrDefault(t =>
            {
                var fqn = FormatVstestFullyQualifiedName(t);
                return string.Equals(t.Name, className, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t.Name, simpleName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fqn, className, StringComparison.Ordinal)
                    || fqn.EndsWith("." + className, StringComparison.Ordinal)
                    || fqn.EndsWith("." + simpleName, StringComparison.Ordinal);
            });
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

    /// <summary>
    /// VSTest / adapter FQN shape: <c>Namespace.Type.Method</c> without parentheses.
    /// Roslyn <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/> appends <c>()</c> for methods,
    /// which breaks <c>dotnet test --filter</c> (parens are expression grouping).
    /// </summary>
    internal static string FormatVstestFullyQualifiedName(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method)
        {
            var type = method.ContainingType;
            if (type is null)
            {
                return method.Name;
            }

            return $"{FormatVstestFullyQualifiedName(type)}.{method.Name}";
        }

        var formatted = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return StripGlobalPrefix(formatted);
    }

    internal static string BuildClassMethodContainsNeedle(string className, string methodName)
    {
        var cls = className.Trim().TrimStart('.');
        var method = GetSimpleName(methodName.Trim());
        var combined = $"{cls}.{method}";
        // Simple class → ".Class.Method"; already-qualified type → "Ns.Class.Method" (no extra leading '.').
        return cls.Contains('.', StringComparison.Ordinal) ? combined : "." + combined;
    }

    /// <summary>
    /// Simple identifiers get a leading <c>.</c> so <c>~.Method</c> matches the FQN suffix.
    /// Dotted paths must not get an extra leading <c>.</c> — <c>~.Ns.Class.Method</c> does not
    /// appear inside <c>Ns.Class.Method</c>.
    /// </summary>
    internal static string BuildSimpleOrDottedContainsNeedle(string value)
    {
        var trimmed = value.Trim().TrimStart('.');
        if (trimmed.Contains('.', StringComparison.Ordinal))
        {
            return trimmed;
        }

        return "." + trimmed;
    }

    internal static string EscapeFilterValue(string value)
    {
        // VSTest filter escape sequences (backslash first).
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal)
            .Replace("&", "\\&", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("=", "\\=", StringComparison.Ordinal)
            .Replace("!", "\\!", StringComparison.Ordinal)
            .Replace("~", "\\~", StringComparison.Ordinal);
    }

    private static string GetSimpleName(string name)
    {
        var trimmed = name.Trim().TrimStart('.');
        var idx = trimmed.LastIndexOf('.');
        return idx >= 0 ? trimmed[(idx + 1)..] : trimmed;
    }

    private static string StripGlobalPrefix(string formatted)
    {
        return formatted.StartsWith("global::", StringComparison.Ordinal)
            ? formatted["global::".Length..]
            : formatted;
    }
}
