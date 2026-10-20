using Microsoft.CodeAnalysis;

namespace ParallelLoad;

internal static class ProjectMerger
{
    public static string Key(string? path, string name) => (path ?? "").ToUpperInvariant() + "|" + name;

    public static SolutionInfo Merge(SolutionInfo[] solutions, string[][] requested, string solutionPath)
    {
        var canonical = new Dictionary<string, ProjectInfo>();
        var requestedSets = requested.Select(paths => paths.ToHashSet(StringComparer.OrdinalIgnoreCase)).ToArray();
        var candidates = solutions.SelectMany((solution, group) => solution.Projects.Select(project => (project, group)));
        foreach (var (project, group) in candidates)
        {
            var key = Key(project.FilePath, project.Name);
            if (!canonical.ContainsKey(key) || requestedSets[group].Contains(project.FilePath!))
            {
                canonical[key] = project;
            }
        }

        // Independent loaders have independent IDs. Resolve through each source solution, then through its target key.
        var sourceKeys = solutions.SelectMany(solution => solution.Projects)
            .ToDictionary(project => project.Id, project => Key(project.FilePath, project.Name));
        var merged = canonical.Values.Select(project => project.WithProjectReferences(project.ProjectReferences.Select(reference =>
        {
            if (!sourceKeys.TryGetValue(reference.ProjectId, out var key))
            {
                // Roslyn retains opaque references to unsupported native projects in ProjectInfo.
                // Project.ProjectReferences filters these absent targets. Preserve the original missing ID,
                // just as baseline AddSolution does; inventing a target or dropping the reference changes that state.
                return reference;
            }

            var target = canonical[key];
            return new ProjectReference(target.Id, reference.Aliases, reference.EmbedInteropTypes);
        }))).ToArray();
        return SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), solutionPath, merged);
    }
}
