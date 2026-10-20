using Microsoft.CodeAnalysis;
using ParallelLoad.Models;

namespace ParallelLoad;

internal static class CoreFirstMerger
{
    public static string[] FindViolations(ProjectInfo[] core, CoreFirstGroup[] groups)
    {
        var violations = new List<string>();
        var corePaths = core.Select(project => project.FilePath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var coreIds = core.Select(project => project.Id).ToHashSet();
        var ownership = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            foreach (var path in group.Progress.LoadedPaths.Where(corePaths.Contains))
            {
                violations.Add($"Core project rebuilt by group {group.Index}: {path}");
            }

            foreach (var project in group.Projects)
            {
                if (string.Equals(project.FilePath, group.AggregatorPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (corePaths.Contains(project.FilePath!))
                {
                    if (!coreIds.Contains(project.Id))
                    {
                        violations.Add($"Core project returned with a new ID by group {group.Index}: {project.FilePath}");
                    }

                    continue;
                }

                if (!ownership.TryGetValue(project.FilePath!, out var owners))
                {
                    owners = [];
                    ownership.Add(project.FilePath!, owners);
                }

                owners.Add(group.Index);
            }
        }

        violations.AddRange(ownership.Where(pair => pair.Value.Count > 1)
            .Select(pair => $"Non-core project loaded by groups {string.Join(",", pair.Value.Order())}: {pair.Key}"));
        // Actual discovered dependencies may differ from the graph. Directory collisions are unsafe even for distinct files.
        violations.AddRange(ownership.GroupBy(pair => Path.GetDirectoryName(pair.Key), StringComparer.OrdinalIgnoreCase)
            .Where(directory => directory.SelectMany(pair => pair.Value).Distinct().Count() > 1)
            .Select(directory => $"Shared project directory loaded by multiple groups: {directory.Key}"));
        return violations.Distinct().Order(StringComparer.Ordinal).ToArray();
    }

    public static SolutionInfo Merge(ProjectInfo[] core, CoreFirstGroup[] groups, string solutionPath)
    {
        var corePaths = core.Select(project => project.FilePath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var aggregatorIds = groups.SelectMany(group => group.Projects.Where(project =>
            string.Equals(project.FilePath, group.AggregatorPath, StringComparison.OrdinalIgnoreCase)))
            .Select(project => project.Id).ToHashSet();
        var projects = core.Concat(groups.SelectMany(group => group.Projects.Where(project =>
            !corePaths.Contains(project.FilePath!) && !aggregatorIds.Contains(project.Id))))
            .GroupBy(project => ProjectMerger.Key(project.FilePath, project.Name))
            .Select(group => group.First().WithProjectReferences(group.First().ProjectReferences.Where(reference =>
                !aggregatorIds.Contains(reference.ProjectId)))).ToArray();
        // Maps created from one immutable core solution preserve shared IDs. Independent group IDs stay untouched.
        return SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), solutionPath, projects);
    }
}
