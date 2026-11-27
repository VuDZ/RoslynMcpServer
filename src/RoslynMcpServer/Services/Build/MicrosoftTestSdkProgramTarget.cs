using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;

namespace RoslynMcpServer.Services.Build;

internal static class MicrosoftTestSdkProgramTarget
{
    internal const string Name = "GenerateProgramFile";

    internal static bool IsStandard(Project project, ProjectRootElement source)
    {
        // Microsoft.NET.Test.Sdk adds an initial target to insert a compiler entry point. Verify
        // the effective target so a project override cannot inherit the package's exemption.
        if (!source.InitialTargets.Trim().Equals(Name, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(source.FullPath).Equals("Microsoft.NET.Test.Sdk.targets", StringComparison.OrdinalIgnoreCase)
            || !project.Targets.TryGetValue(Name, out var target)
            || !PathsMatch(target.FullPath, source.FullPath)
            || target.DependsOnTargets.Length != 0)
        {
            return false;
        }

        var build = new FileInfo(source.FullPath).Directory?.Parent;
        var package = build?.Parent?.Parent;
        if (build?.Name.Equals("build", StringComparison.OrdinalIgnoreCase) != true
            || package?.Name.Equals("microsoft.net.test.sdk", StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        var packageFolders = project.GetPropertyValue("NuGetPackageFolders")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(project.GetPropertyValue("NuGetPackageRoot"));
        return packageFolders.Any(folder => PathsMatch(package.Parent?.FullName, folder))
            && target.Children.All(child => child switch
            {
                ProjectItemGroupTaskInstance group => group.Items.All(item =>
                    item.ItemType is "Compile" or "CompileAfter" or "ProgramCompiles" or "RemoveExistingMicrosoftNETTestSdkProgram"),
                ProjectTaskInstance task => task.Name == "Warning" && task.Outputs.Count == 0,
                _ => false,
            });
    }

    private static bool PathsMatch(string? left, string? right) =>
        Path.IsPathFullyQualified(left ?? string.Empty)
        && Path.IsPathFullyQualified(right ?? string.Empty)
        && DotNetSdkEnvironment.PathsEqual(
            Path.TrimEndingDirectorySeparator(left!),
            Path.TrimEndingDirectorySeparator(right!));
}
