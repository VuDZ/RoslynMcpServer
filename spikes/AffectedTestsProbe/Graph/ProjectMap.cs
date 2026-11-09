using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

internal sealed class ProjectMap
{
    private readonly Dictionary<ProjectKey, HashSet<ProjectKey>> _visible;

    public ProjectMap(Solution solution, string configuration, string platform)
    {
        Keys = new Dictionary<ProjectId, ProjectKey>();
        Projects = new Dictionary<ProjectKey, Project>();
        foreach (var project in solution.Projects)
        {
            var key = ProjectKey.From(project, configuration, platform);
            if (Projects.ContainsKey(key))
            {
                // Two projects can share a path when the workspace did not suffix the target framework.
                key = key with { TargetFramework = key.TargetFramework + "#" + project.Id.Id };
            }

            Keys.Add(project.Id, key);
            Projects.Add(key, project);
        }

        _visible = BuildVisible(solution);
    }

    public Dictionary<ProjectId, ProjectKey> Keys { get; }

    public Dictionary<ProjectKey, Project> Projects { get; }

    public IReadOnlyDictionary<ProjectKey, HashSet<ProjectKey>> Visible => _visible;

    public ProjectKey Key(ProjectId id) => Keys[id];

    private Dictionary<ProjectKey, HashSet<ProjectKey>> BuildVisible(Solution solution)
    {
        var graph = solution.GetProjectDependencyGraph();
        var visible = new Dictionary<ProjectKey, HashSet<ProjectKey>>();
        foreach (var project in solution.Projects)
        {
            var key = Keys[project.Id];
            var allowed = new HashSet<ProjectKey> { key };
            foreach (var dependency in graph.GetProjectsThatThisProjectTransitivelyDependsOn(project.Id))
            {
                if (Keys.TryGetValue(dependency, out var dependencyKey))
                {
                    allowed.Add(dependencyKey);
                }
            }

            visible.Add(key, allowed);
        }

        return visible;
    }
}
