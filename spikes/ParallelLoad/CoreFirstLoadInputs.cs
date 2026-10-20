using System.Xml.Linq;
using ParallelLoad.Models;

namespace ParallelLoad;

internal static class CoreFirstLoadInputs
{
    public static string SolutionDirectory(string solutionPath)
    {
        var directory = Path.GetDirectoryName(solutionPath)!;
        // Roslyn 5.9 SetSolutionProperties appends the separator only for an existing solution directory.
        return Directory.Exists(directory) ? Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar : directory;
    }

    public static string[] Phase1Requests(string solutionPath, CoreFirstPlan plan)
    {
        var directory = Path.GetDirectoryName(solutionPath)!;
        var loadable = plan.Core.Concat(plan.Groups.SelectMany(group => group)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Request unloadable solution entries once through LoadSolutionInfoAsync, preserving its requested diagnostics mode.
        var unloadable = XDocument.Load(solutionPath).Descendants("Project")
            .Select(element => Path.GetFullPath(element.Attribute("Path")!.Value, directory))
            .Where(path => !loadable.Contains(path));
        return plan.Core.Concat(unloadable).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
