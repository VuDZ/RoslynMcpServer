using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

/// <summary>
/// Stable project identity: path, configuration, platform, and target framework.
/// Roslyn <see cref="ProjectId"/> values are not stable across processes.
/// </summary>
internal sealed record ProjectKey(string ProjectPath, string Configuration, string Platform, string TargetFramework)
{
    public static ProjectKey From(Project project, string configuration, string platform)
    {
        var path = string.IsNullOrEmpty(project.FilePath) ? project.Name : Path.GetFullPath(project.FilePath);
        return new ProjectKey(path, configuration, platform, TargetFrameworkOf(project.Name));
    }

    /// <summary>
    /// MSBuildWorkspace names a multi-targeted project <c>Name (tfm)</c>. A single target framework has no suffix.
    /// </summary>
    public static string TargetFrameworkOf(string projectName)
    {
        var open = projectName.LastIndexOf(" (", StringComparison.Ordinal);
        if (open > 0 && projectName.EndsWith(')'))
        {
            return projectName[(open + 2)..^1];
        }

        return "unspecified";
    }
}
