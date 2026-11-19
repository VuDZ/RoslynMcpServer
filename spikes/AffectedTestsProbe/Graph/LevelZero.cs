using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

/// <summary>
/// Level 0 is every test of the projects that depend on a project containing the member's file,
/// intersected with the selected test scope. Keys whose tests are outside that scope are reported apart.
/// </summary>
internal static class LevelZero
{
    public static LevelZeroSplit Split(Solution solution, ProjectMap map, RootIndex roots, ISymbol symbol)
    {
        var origin = SourceDocument(solution, symbol);
        var path = origin?.FilePath;
        var containing = new List<Project>();
        foreach (var project in solution.Projects)
        {
            if (string.IsNullOrEmpty(path))
            {
                if (origin?.Project.Id == project.Id)
                {
                    containing.Add(project);
                }

                continue;
            }

            if (project.Documents.Any(document =>
                    string.Equals(document.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                containing.Add(project);
            }
        }

        var testProjects = roots.Classes.Select(testClass => testClass.Project).ToHashSet();
        var inScope = new HashSet<ProjectKey>();
        var outside = new List<ProjectKey>();
        var graph = solution.GetProjectDependencyGraph();
        foreach (var project in containing)
        {
            var dependents = new HashSet<ProjectId> { project.Id };
            foreach (var dependent in graph.GetProjectsThatTransitivelyDependOnThisProject(project.Id))
            {
                dependents.Add(dependent);
            }

            var scoped = false;
            foreach (var dependent in dependents)
            {
                if (map.Keys.TryGetValue(dependent, out var dependentKey) && testProjects.Contains(dependentKey))
                {
                    inScope.Add(dependentKey);
                    scoped = true;
                }
            }

            if (!scoped)
            {
                outside.Add(map.Key(project.Id));
            }
        }

        var units = roots.Units.Where(unit => inScope.Contains(unit.Project)).ToArray();
        return new LevelZeroSplit(units, outside);
    }

    private static Document? SourceDocument(Solution solution, ISymbol symbol)
    {
        var tree = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree
            ?? symbol.ContainingType?.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree;
        return tree is null ? null : solution.GetDocument(tree);
    }
}
