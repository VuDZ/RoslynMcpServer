using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;

namespace AffectedTestsProbe;

/// <summary>
/// Compiles one project at a time from its own syntax trees.
/// <see cref="Project.GetCompilationAsync"/> and <see cref="Document.GetSemanticModelAsync"/> are not used.
/// Both cache the bound tree on the workspace for the rest of the process, which is what made a compiler
/// solution retain tens of gigabytes after the facts had already been copied out.
/// The compilation given to the visitor is dropped when the project returns. A second compilation, which
/// never receives a semantic model, is kept only as a source reference for later projects. Document syntax
/// trees stay in the workspace. A Visual Basic project is kept as a reference and its bodies are not bound.
/// </summary>
internal sealed class ProjectCompilations
{
    public ProjectCompilations(Solution solution)
    {
        _solution = solution;
    }

    public Task VisitAsync(
        IReadOnlySet<ProjectId>? bindBodies,
        Func<Project, Compilation, CancellationToken, Task> visitCSharp,
        CancellationToken cancellationToken) =>
        VisitProjectsAsync(bindBodies, visitCSharp, cancellationToken);

    public Task VisitTreesAsync(
        IReadOnlySet<ProjectId>? bindBodies,
        Func<Project, SemanticModel, SyntaxNode, CancellationToken, Task> visit,
        CancellationToken cancellationToken) =>
        VisitProjectsAsync(
            bindBodies,
            async (project, compilation, token) =>
            {
                foreach (var tree in compilation.SyntaxTrees)
                {
                    var root = await tree.GetRootAsync(token).ConfigureAwait(false);
                    await visit(project, compilation.GetSemanticModel(tree), root, token).ConfigureAwait(false);
                }
            },
            cancellationToken);

    internal Compilation? Kept(ProjectId id) => _kept.TryGetValue(id, out var compilation) ? compilation : null;

    private async Task VisitProjectsAsync(
        IReadOnlySet<ProjectId>? bindBodies,
        Func<Project, Compilation, CancellationToken, Task> visitCSharp,
        CancellationToken cancellationToken)
    {
        var closure = Closure(bindBodies);
        var order = _solution.GetProjectDependencyGraph().GetTopologicallySortedProjects(cancellationToken);
        foreach (var id in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!closure.Contains(id))
            {
                continue;
            }

            var project = _solution.GetProject(id);
            if (project is null)
            {
                continue;
            }

            var compilation = await CreateAsync(project, cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            var bind = project.Language == LanguageNames.CSharp && (bindBodies is null || bindBodies.Contains(project.Id));
            if (!bind)
            {
                _kept.Add(project.Id, compilation);
                continue;
            }

            await visitCSharp(project, compilation, cancellationToken).ConfigureAwait(false);
            var unbound = await CreateAsync(project, cancellationToken).ConfigureAwait(false);
            if (unbound is not null)
            {
                _kept.Add(project.Id, unbound);
            }
        }
    }

    private HashSet<ProjectId> Closure(IReadOnlySet<ProjectId>? bindBodies)
    {
        if (bindBodies is null)
        {
            return _solution.Projects.Select(project => project.Id).ToHashSet();
        }

        var closure = new HashSet<ProjectId>(bindBodies);
        var graph = _solution.GetProjectDependencyGraph();
        foreach (var id in bindBodies)
        {
            foreach (var dependency in graph.GetProjectsThatThisProjectTransitivelyDependsOn(id))
            {
                closure.Add(dependency);
            }
        }

        return closure;
    }

    private async Task<Compilation?> CreateAsync(Project project, CancellationToken cancellationToken)
    {
        var trees = await TreesAsync(project, cancellationToken).ConfigureAwait(false);
        var references = References(project);
        var name = string.IsNullOrEmpty(project.AssemblyName) ? project.Name : project.AssemblyName;
        if (project.Language == LanguageNames.CSharp && project.CompilationOptions is CSharpCompilationOptions csharp)
        {
            return CSharpCompilation.Create(name, trees, references, csharp);
        }

        if (project.Language == LanguageNames.VisualBasic
            && project.CompilationOptions is VisualBasicCompilationOptions visualBasic)
        {
            return VisualBasicCompilation.Create(name, trees, references, visualBasic);
        }

        return null;
    }

    private List<MetadataReference> References(Project project)
    {
        var references = new List<MetadataReference>(project.MetadataReferences);
        foreach (var reference in project.ProjectReferences)
        {
            if (_kept.TryGetValue(reference.ProjectId, out var dependency))
            {
                references.Add(dependency.ToMetadataReference(reference.Aliases, reference.EmbedInteropTypes));
            }
        }

        return references;
    }

    private static async Task<List<SyntaxTree>> TreesAsync(Project project, CancellationToken cancellationToken)
    {
        var trees = new List<SyntaxTree>();
        var seen = new HashSet<SyntaxTree>();
        foreach (var document in project.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root?.SyntaxTree is { } tree && seen.Add(tree))
            {
                trees.Add(tree);
            }
        }

        return trees;
    }

    private readonly Solution _solution;
    private readonly Dictionary<ProjectId, Compilation> _kept = new();
}
