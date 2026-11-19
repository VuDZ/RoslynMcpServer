using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

/// <summary>
/// How usable the loaded solution is without a build. A project without <c>System.Object</c> has no
/// framework references, usually because restore did not run. Declaration errors (for example CS0246)
/// show missing packages or types; errors inside method bodies are not counted, because finding them
/// would bind every body of the solution.
/// </summary>
internal static class LoadQuality
{
    public static async Task<List<ProjectQuality>> MeasureAsync(Solution solution, CancellationToken cancellationToken)
    {
        var projects = new List<ProjectQuality>();
        foreach (var project in solution.Projects.Where(project => project.Language == LanguageNames.CSharp))
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            var noFramework = compilation.GetSpecialType(SpecialType.System_Object).TypeKind == TypeKind.Error;
            var codes = compilation.GetDeclarationDiagnostics(cancellationToken)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .GroupBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            projects.Add(new ProjectQuality(project.Name, noFramework, codes));
        }

        return projects;
    }
}
