using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcpServer.Services.Models;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Testing;

/// <summary>Maps changed physical paths to projects and their transitively dependent test projects.</summary>
internal static class ProjectImpactMapper
{
    /// <summary>
    /// Uses the map and stale state of the same loaded solution after disk synchronization.
    /// Reload must supply the refreshed solution/map and both current watcher stale flags.
    /// Membership changes require reload; only graph-file changes select every test project.
    /// </summary>
    internal static async Task<ProjectImpactResult> MapAsync(
        Solution solution,
        WorkspaceInputMap inputMap,
        IEnumerable<string> changedPaths,
        string? loadedWorkspacePath,
        bool projectGraphStale,
        bool projectGraphStaleFromGraphFile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(inputMap);
        ArgumentNullException.ThrowIfNull(changedPaths);
        cancellationToken.ThrowIfCancellationRequested();

        var projects = new HashSet<ProjectId>();
        var paths = new Dictionary<string, ProjectPathImpact>(InputPathCanon.Comparer);
        var notCovered = new HashSet<string>(InputPathCanon.Comparer);
        var requiresReload = projectGraphStale || projectGraphStaleFromGraphFile;
        var workspacePath = InputPathCanon.TryCanonicalize(loadedWorkspacePath ?? solution.FilePath);
        var workspaceDirectory = workspacePath is null ? null : Path.GetDirectoryName(workspacePath);
        foreach (var rawPath in changedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = InputPathCanon.TryCanonicalize(rawPath)
                ?? throw new ArgumentException("Changed paths must be valid nonempty file paths.", nameof(changedPaths));
            if (IsRestoreInput(path))
            {
                continue;
            }

            var entry = inputMap.Find(path);
            // The loaded workspace prefix can itself contain artifacts; sibling project outputs still need filtering.
            // See docs/rapid-tia/epoch-1-project-level/task-04-level0-path-mapping-med.md.
            if (entry is null && WorkspaceDiskPathFilter.IsIgnoredPath(path, workspaceDirectory))
            {
                continue;
            }
            var owners = ProjectsForPath(solution, inputMap, entry, path);
            paths[path] = new ProjectPathImpact(path, owners);
            projects.UnionWith(owners);
            if (owners.Count == 0
                && !(projectGraphStaleFromGraphFile && WorkspaceDiskPathFilter.IsProjectGraphFile(path)))
            {
                notCovered.Add(path);
            }
        }

        var testProjects = await FindTestProjectsAsync(solution, cancellationToken).ConfigureAwait(false);
        var expanded = projectGraphStaleFromGraphFile
            ? solution.ProjectIds.Where(testProjects.Contains).ToArray()
            : ExpandProjects(solution, projects, testProjects);
        return new ProjectImpactResult(
            solution.ProjectIds.Where(projects.Contains).ToArray(),
            expanded,
            notCovered.Order(InputPathCanon.Comparer).ToArray(),
            requiresReload,
            requiresReload)
        {
            Paths = paths.Values.OrderBy(static impact => impact.Path, InputPathCanon.Comparer).ToArray(),
            ProjectGraphStaleFromGraphFile = projectGraphStaleFromGraphFile,
        };
    }

    /// <summary>Tests in each project itself, plus every transitively dependent test project.</summary>
    internal static IReadOnlyList<ProjectId> ExpandProjects(
        Solution solution,
        IEnumerable<ProjectId> projectIds,
        IReadOnlySet<ProjectId> testProjectIds)
    {
        var graph = solution.GetProjectDependencyGraph();
        var expanded = new HashSet<ProjectId>();
        foreach (var projectId in projectIds)
        {
            if (solution.GetProject(projectId) is null)
            {
                continue;
            }
            if (testProjectIds.Contains(projectId))
            {
                expanded.Add(projectId);
            }
            expanded.UnionWith(graph.GetProjectsThatTransitivelyDependOnThisProject(projectId)
                .Where(testProjectIds.Contains));
        }
        return solution.ProjectIds.Where(expanded.Contains).ToArray();
    }

    private static IReadOnlyList<ProjectId> ProjectsForPath(
        Solution solution,
        WorkspaceInputMap inputMap,
        InputPathEntry? entry,
        string path)
    {
        var documents = solution.GetDocumentIdsWithFilePath(path);
        if (documents.Length > 0)
        {
            return documents.Select(static document => document.ProjectId).Distinct().ToArray();
        }
        if (entry is not null)
        {
            return entry.Occurrences.Select(static occurrence => occurrence.ProjectId)
                .Where(id => solution.GetProject(id) is not null).Distinct().ToArray();
        }
        if (!WorkspaceDiskPathFilter.IsCSharpSource(path))
        {
            return [];
        }

        // An absent source may be deleted or excluded by Compile Remove after reload, not a new compile item.
        // Watcher state supplies reload advice; nearest-project ownership still covers deletions.
        // See docs/rapid-tia/epoch-1-project-level/task-05-response-composition-med.md (T04-C2).
        var candidates = inputMap.Projects
            .Where(project => project.FilePath is not null)
            .Select(project => (project.Id, Directory: Path.GetDirectoryName(project.FilePath!)!))
            .Where(project => WorkspaceDiskPathFilter.IsPathUnderDirectory(path, project.Directory, InputPathCanon.Comparison))
            .ToArray();
        var nearestLength = candidates.Length == 0 ? 0 : candidates.Max(static project => project.Directory.Length);
        return candidates.Where(project => project.Directory.Length == nearestLength)
            .Select(static project => project.Id).ToArray();
    }

    private static async Task<HashSet<ProjectId>> FindTestProjectsAsync(Solution solution, CancellationToken cancellationToken)
    {
        var projects = new HashSet<ProjectId>();
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!WorkspaceDiskPathFilter.IsCSharpSource(document.FilePath))
                {
                    continue;
                }
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (root is not null && model is not null
                    && root.DescendantNodes().OfType<MethodDeclarationSyntax>().Any(method =>
                        TestDiscoveryHelper.HasTestAttribute(method, model)
                        && model.GetDeclaredSymbol(method, cancellationToken) is not null))
                {
                    projects.Add(project.Id);
                    break;
                }
            }
        }
        return projects;
    }

    private static bool IsRestoreInput(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("project.assets.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("packages.lock.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("nuget.config", StringComparison.OrdinalIgnoreCase);
    }
}
