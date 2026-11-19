using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Narrowing.Models;

namespace RoslynMcpServer.Services.Testing.Narrowing;

/// <summary>The projects between the edited projects and one test project.</summary>
internal static class TestNarrowingPath
{
    /// <summary>
    /// The test project and every project it depends on that is edited or depends on an edited project, in
    /// solution order. Edited are the owners of the C# reasons epoch 1 gave for the test project, and the
    /// projects the test project depends on whose C# documents changed: an edit epoch 1 narrowed itself, such as
    /// a test method of a base class in another test project, still has to be built and compared.
    /// </summary>
    internal static IReadOnlyList<Project> Find(
        Solution solution,
        ProjectId testProject,
        IReadOnlyList<ProjectExpansionReason> reasons,
        IReadOnlyList<EditedSourceFile> editedFiles)
    {
        var graph = solution.GetProjectDependencyGraph();
        var upstream = graph.GetProjectsThatThisProjectTransitivelyDependsOn(testProject).Append(testProject).ToHashSet();
        var edited = reasons
            .Where(reason => reason.Kind == ProjectExpansionKind.CSharpSource && reason.Owner is not null)
            .Select(reason => reason.Owner!)
            .Concat(editedFiles.Select(file => file.ProjectId))
            .Where(upstream.Contains)
            .ToHashSet();
        return
        [
            .. solution.Projects.Where(project => project.Id == testProject
                || (upstream.Contains(project.Id)
                    && (edited.Contains(project.Id)
                        || graph.GetProjectsThatThisProjectTransitivelyDependsOn(project.Id).Any(edited.Contains)))),
        ];
    }

    /// <summary>
    /// The test project and every project it depends on, transitively, in solution order: the code a search from
    /// its test classes can pass through, besides the assemblies next to the test assembly.
    /// </summary>
    internal static IReadOnlyList<Project> Searched(Solution solution, ProjectId testProject)
    {
        var dependencies = solution.GetProjectDependencyGraph().GetProjectsThatThisProjectTransitivelyDependsOn(testProject);
        return [.. solution.Projects.Where(project => project.Id == testProject || dependencies.Contains(project.Id))];
    }
}
