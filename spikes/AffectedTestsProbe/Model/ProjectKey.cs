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
    /// The framework suffix of a workspace project name. A single target framework has no suffix.
    /// </summary>
    public static string TargetFrameworkOf(string projectName) => ProjectNameSuffix.Split(projectName).TargetFramework;
}
