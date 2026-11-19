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
        MemberNode? found = null;
        await new ProjectCompilations(solution).VisitTreesAsync(
            bindBodies: null,
            (project, model, root, token) =>
            {
                if (found is not null || project.Language != LanguageNames.CSharp)
                {
                    return Task.CompletedTask;
                }

                foreach (var node in root.DescendantNodes())
                {
                    ISymbol? symbol = node switch
                    {
                        MethodDeclarationSyntax method when method.Identifier.ValueText == name => model.GetDeclaredSymbol(method, token),
                        VariableDeclaratorSyntax variable when variable.Identifier.ValueText == name => model.GetDeclaredSymbol(variable, token),
                        _ => null,
                    };
                    if (symbol is not null && SymbolIds.TryNode(symbol, solution, map.Keys) is { } member)
                    {
                        found = member;
                        break;
                    }
                }

                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);
        return found;
    }

    public static async Task<IReadOnlyList<string>> ErrorsAsync(Solution solution, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        await new ProjectCompilations(solution).VisitAsync(
            bindBodies: null,
            (project, compilation, token) =>
            {
                errors.AddRange(compilation.GetDiagnostics(token)
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString()));
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);
        return errors;
    }
}
