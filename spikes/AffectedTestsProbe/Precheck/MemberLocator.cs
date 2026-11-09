using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AffectedTestsProbe;

internal static class MemberLocator
{
    public static async Task<MemberNode?> FindAsync(
        Solution solution,
        ProjectMap map,
        string name,
        CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (model is null || root is null)
                {
                    continue;
                }

                foreach (var node in root.DescendantNodes())
                {
                    ISymbol? symbol = node switch
                    {
                        MethodDeclarationSyntax method when method.Identifier.ValueText == name => model.GetDeclaredSymbol(method, cancellationToken),
                        VariableDeclaratorSyntax variable when variable.Identifier.ValueText == name => model.GetDeclaredSymbol(variable, cancellationToken),
                        _ => null,
                    };
                    if (symbol is not null && SymbolIds.TryNode(symbol, solution, map.Keys) is { } member)
                    {
                        return member;
                    }
                }
            }
        }

        return null;
    }

    public static async Task<IReadOnlyList<string>> ErrorsAsync(Solution solution, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            errors.AddRange(compilation.GetDiagnostics(cancellationToken)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString()));
        }

        return errors;
    }
}
