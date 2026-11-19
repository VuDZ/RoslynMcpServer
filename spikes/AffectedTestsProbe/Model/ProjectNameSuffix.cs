namespace AffectedTestsProbe;

/// <summary>
/// Splits a workspace project name into the project name and its target framework.
/// MSBuildWorkspace writes a multi-targeted project as <c>Name(tfm)</c> with no space.
/// A spaced <c>Name (tfm)</c> is the same suffix. A project with one framework has no suffix.
/// </summary>
internal static class ProjectNameSuffix
{
    public static (string Name, string TargetFramework) Split(string projectName)
    {
        var open = projectName.LastIndexOf('(');
        if (open > 0 && projectName.EndsWith(')'))
        {
            var name = projectName[..open].TrimEnd();
            var framework = projectName[(open + 1)..^1].Trim();
            if (name.Length > 0 && framework.Length > 0)
            {
                return (name, framework);
            }
        }

        return (projectName, "unspecified");
    }
}
