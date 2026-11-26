using System.Collections;
using System.Xml.Linq;
using Microsoft.Build.Construction;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Evaluation.Context;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Build;

internal static class TestOutputPreflight
{
    /// <summary>Reports only a confirmed missing output in the ordinary VSTest no-build route.</summary>
    internal static (string AssemblyPath, string Configuration, string Platform, string Framework, string? SolutionConfiguration)?
        FindMissing(
            string targetPath,
            string workingDirectory,
            string? configuration,
            string? platform,
            DotNetSdkEnvironment.SdkPinInfo sdk,
            CancellationToken cancellationToken)
    {
        try
        {
            if (!MatchesInProcessSdk(sdk))
            {
                return null;
            }

            using var collection = new ProjectCollection();
            var evaluation = new ProjectOptions
            {
                ProjectCollection = collection,
                EvaluationContext = EvaluationContext.Create(EvaluationContext.SharingPolicy.Shared),
            };
            foreach (var (projectPath, properties, solutionConfiguration) in GetProjects(targetPath, configuration, platform))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var projectGlobalJson = GlobalJsonSdkReader.FindGlobalJsonPath(Path.GetDirectoryName(projectPath)!);
                if (!InputPathCanon.Comparer.Equals(projectGlobalJson, sdk.GlobalJsonPath))
                {
                    // A nested global.json can select another SDK during evaluation of an imported project.
                    continue;
                }

                properties["VSTestNoBuild"] = "true";
                evaluation.GlobalProperties = properties;
                var outer = Project.FromFile(projectPath, evaluation);
                foreach (var project in GetInnerProjects(outer, properties, evaluation, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (GetMissingAssembly(project) is not { } assemblyPath)
                    {
                        continue;
                    }

                    if (HasAutoResponseArguments(targetPath, workingDirectory, sdk.SdkDirectory!)
                        || (properties.TryGetValue("SolutionPath", out var solutionPath)
                            && (HasUnmodeledSolutionEnvironment(configuration, platform) || HasCustomSolutionImports(solutionPath))))
                    {
                        return null;
                    }

                    if (UsesStandardVstestTargets(project, sdk)
                        && (ReferenceEquals(outer, project) || UsesStandardVstestTargets(outer, sdk, crossTargeting: true)))
                    {
                        return (assemblyPath, project.GetPropertyValue("Configuration"), project.GetPropertyValue("Platform"),
                            project.GetPropertyValue("TargetFramework"), solutionConfiguration);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidProjectFileException or IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or KeyNotFoundException or NotSupportedException)
        {
            // MSBuild and VSTest report evaluation failures through the regular execution path.
        }

        return null;
    }

    private static bool HasAutoResponseArguments(string targetPath, string workingDirectory, string sdkDirectory)
    {
        // Project evaluation does not receive command-line defaults such as /p:OutDir from auto-response files.
        // https://learn.microsoft.com/visualstudio/msbuild/msbuild-response-files
        var targetDirectory = Path.GetDirectoryName(targetPath)!;
        return ResponseFileHasArguments(Path.Combine(sdkDirectory, "MSBuild.rsp")) == true
            || (!OperatingSystem.IsWindows() && ResponseFileHasArguments(Path.Combine(sdkDirectory, "msbuild.rsp")) == true)
            || HasDirectoryResponseArguments(targetDirectory)
            || (!PathsMatch(targetDirectory, workingDirectory) && HasDirectoryResponseArguments(workingDirectory));
    }

    private static bool HasDirectoryResponseArguments(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var arguments = ResponseFileHasArguments(Path.Combine(current.FullName, "Directory.Build.rsp"));
            if (arguments is { } hasArguments)
            {
                return hasArguments;
            }
        }

        return false;
    }

    private static bool? ResponseFileHasArguments(string path)
    {
        try
        {
            return File.ReadLines(path).Any(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static bool HasUnmodeledSolutionEnvironment(string? configuration, string? platform) =>
        (configuration is null || platform is null)
        && Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().Any(variable =>
            variable.Value is string { Length: > 0 } && variable.Key is string name
            && ((configuration is null && name.Equals("Configuration", StringComparison.OrdinalIgnoreCase))
                || (platform is null && name.Equals("Platform", StringComparison.OrdinalIgnoreCase))));

    private static bool HasCustomSolutionImports(string solutionPath)
    {
        // The CLI evaluates these imports on the generated solution project before dispatching to each test project.
        // https://learn.microsoft.com/visualstudio/msbuild/customize-solution-build
        var directory = Path.GetDirectoryName(solutionPath)!;
        if (HasSidecarTargets(Path.GetFileName(solutionPath))
            || HasSidecarTargets(Path.GetFileNameWithoutExtension(solutionPath) + ".sln")
            || Environment.GetEnvironmentVariable("DirectorySolutionPropsPath") is { Length: > 0 }
            || Environment.GetEnvironmentVariable("DirectorySolutionTargetsPath") is { Length: > 0 })
        {
            return true;
        }

        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            if (IsFilePresent(Path.Combine(current.FullName, "Directory.Solution.props"))
                || IsFilePresent(Path.Combine(current.FullName, "Directory.Solution.targets")))
            {
                return true;
            }
        }

        return false;

        bool HasSidecarTargets(string solutionName) =>
            IsFilePresent(Path.Combine(directory, "before." + solutionName + ".targets"))
            || IsFilePresent(Path.Combine(directory, "after." + solutionName + ".targets"));
    }

    private static bool MatchesInProcessSdk(DotNetSdkEnvironment.SdkPinInfo sdk)
    {
        // The server and the dotnet subprocess can resolve different SDKs, even for the same version.
        if (sdk.SdkDirectory is null || !PathsMatch(Path.GetDirectoryName(typeof(Project).Assembly.Location), sdk.SdkDirectory))
        {
            return false;
        }

        var inherited = DotNetSdkEnvironment.CaptureInheritedOverrides();
        return (inherited.MsBuildExePath is null || PathsMatch(inherited.MsBuildExePath, sdk.MsBuildDllPath))
            && (inherited.MsBuildExtensionsPath is null || PathsMatch(inherited.MsBuildExtensionsPath, sdk.SdkDirectory))
            && (inherited.MsBuildSdksPath is null || PathsMatch(inherited.MsBuildSdksPath, sdk.SdksDirectory))
            && (inherited.ResolverSdksDir is null || PathsMatch(inherited.ResolverSdksDir, sdk.SdksDirectory))
            && (inherited.ResolverCliDir is null || PathsMatch(inherited.ResolverCliDir, sdk.DotNetRoot))
            && (inherited.ResolverSdksVer is null || inherited.ResolverSdksVer == sdk.ResolvedVersion);
    }

    private static bool PathsMatch(string? left, string? right) =>
        Path.IsPathFullyQualified(left ?? string.Empty)
        && Path.IsPathFullyQualified(right ?? string.Empty)
        && DotNetSdkEnvironment.PathsEqual(
            Path.TrimEndingDirectorySeparator(left!),
            Path.TrimEndingDirectorySeparator(right!));

    private static IReadOnlyList<(string Path, Dictionary<string, string> Properties, string? SolutionConfiguration)> GetProjects(
        string targetPath,
        string? configuration,
        string? platform)
    {
        var kind = WorkspaceInputPath.Classify(targetPath);
        if (kind == WorkspaceInputKind.CSharpProject)
        {
            return [(targetPath, MsBuildWorkspaceProperties.Create(configuration, platform), null)];
        }

        if (!WorkspaceInputPath.IsSolutionOrFilter(kind))
        {
            return [];
        }

        var solutionPath = targetPath;
        HashSet<string>? selectedProjects = null;
        if (kind == WorkspaceInputKind.SolutionFilter)
        {
            var filter = SolutionFilterPath.TryReadWithProjects(targetPath);
            if (filter.BackingSolution is null || filter.Projects is null)
            {
                return [];
            }

            solutionPath = filter.BackingSolution;
            selectedProjects = filter.Projects;
        }

        // Parsing the original filter lets MSBuild reject unknown project entries before checking outputs.
        var solution = SolutionFile.Parse(targetPath);
        var effectiveConfiguration = configuration ?? solution.GetDefaultConfigurationName();
        var effectivePlatform = platform ?? solution.GetDefaultPlatformName();
        var solutionConfiguration = $"{effectiveConfiguration}|{effectivePlatform}";
        if (!solution.SolutionConfigurations.Any(pair =>
            pair.FullName.Equals(solutionConfiguration, StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        var contents = SolutionConfigurationContents(solution, solutionConfiguration, selectedProjects);
        var projects = new List<(string, Dictionary<string, string>, string?)>();
        foreach (var project in solution.ProjectsInOrder)
        {
            if (IsManagedProject(project.AbsolutePath)
                && IsSelected(project, selectedProjects)
                && project.ProjectConfigurations.TryGetValue(solutionConfiguration, out var mapping)
                && mapping.IncludeInBuild)
            {
                projects.Add((project.AbsolutePath, SolutionProperties(mapping, solutionPath, contents), effectiveConfiguration));
            }
        }

        return projects;
    }

    private static bool IsManagedProject(string path) =>
        WorkspaceInputPath.Classify(path) == WorkspaceInputKind.CSharpProject
        || Path.GetExtension(path).Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".vbproj", StringComparison.OrdinalIgnoreCase);

    private static bool IsSelected(ProjectInSolution project, HashSet<string>? selectedProjects) =>
        selectedProjects is null || selectedProjects.Contains(project.RelativePath.Replace('\\', '/'));

    private static string SolutionConfigurationContents(
        SolutionFile solution,
        string configuration,
        HashSet<string>? selectedProjects)
    {
        // Import conditions can depend on solution context even when the per-project mapping is already known.
        // https://github.com/dotnet/msbuild/blob/main/src/Build/Construction/Solution/SolutionProjectGenerator.cs
        var contents = new XElement("SolutionConfiguration");
        foreach (var project in solution.ProjectsInOrder)
        {
            if (!project.ProjectConfigurations.TryGetValue(configuration, out var mapping))
            {
                continue;
            }

            var entry = new XElement("ProjectConfiguration",
                new XAttribute("Project", project.ProjectGuid),
                new XAttribute("AbsolutePath", project.AbsolutePath),
                new XAttribute("BuildProjectInSolution", mapping.IncludeInBuild.ToString()),
                mapping.FullName);
            foreach (var dependencyId in project.Dependencies)
            {
                var dependency = solution.ProjectsByGuid[dependencyId];
                if (dependency.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat
                    && IsSelected(dependency, selectedProjects)
                    && dependency.ProjectConfigurations.TryGetValue(configuration, out var dependencyMapping)
                    && dependencyMapping.IncludeInBuild)
                {
                    entry.Add(new XElement("ProjectDependency", new XAttribute("Project", dependencyId)));
                }
            }

            contents.Add(entry);
        }

        return contents.ToString();
    }

    private static Dictionary<string, string> SolutionProperties(
        ProjectConfigurationInSolution mapping,
        string solutionPath,
        string solutionConfigurationContents)
    {
        var properties = MsBuildWorkspaceProperties.Create(mapping.ConfigurationName, mapping.PlatformName);
        properties["BuildingSolutionFile"] = "true";
        properties["CurrentSolutionConfigurationContents"] = solutionConfigurationContents;
        properties["SolutionDir"] = Path.GetDirectoryName(solutionPath) + Path.DirectorySeparatorChar;
        properties["SolutionExt"] = Path.GetExtension(solutionPath);
        properties["SolutionFileName"] = Path.GetFileName(solutionPath);
        properties["SolutionName"] = Path.GetFileNameWithoutExtension(solutionPath);
        properties["SolutionPath"] = solutionPath;
        return properties;
    }

    private static IReadOnlyList<Project> GetInnerProjects(
        Project outer,
        Dictionary<string, string> properties,
        ProjectOptions evaluation,
        CancellationToken cancellationToken)
    {
        if (!IsTrue(outer, "IsCrossTargetingBuild"))
        {
            return [outer];
        }
        if (outer.GetPropertyValue("InnerVSTestTargets").Length != 0)
        {
            return [];
        }

        // VSTest checks IsTestProject inside each framework, where SDK/package imports can change it.
        var frameworks = outer.GetPropertyValue("TargetFrameworks")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var projects = new List<Project>();
        foreach (var framework in frameworks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var innerProperties = new Dictionary<string, string>(properties, properties.Comparer)
            {
                ["TargetFramework"] = framework,
            };
            evaluation.GlobalProperties = innerProperties;
            projects.Add(Project.FromFile(outer.FullPath, evaluation));
        }

        return projects;
    }

    private static string? GetMissingAssembly(Project project)
    {
        if (!IsTrue(project, "IsTestProject") || IsTrue(project, "TestingPlatformDotnetTestSupport")
            || IsTrue(project, "IsTestingPlatformApplication"))
        {
            return null;
        }

        var targetPath = project.GetPropertyValue("TargetPath");
        return Path.IsPathFullyQualified(targetPath) && !IsFilePresent(targetPath) ? targetPath : null;
    }

    private static bool IsFilePresent(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) == 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private const string TestSdkProgramTarget = "GenerateProgramFile";

    private static bool UsesStandardVstestTargets(
        Project project,
        DotNetSdkEnvironment.SdkPinInfo sdk,
        bool crossTargeting = false)
    {
        if (HasCustomProjectInputs(project, sdk.SdkDirectory!))
        {
            return false;
        }

        var targets = crossTargeting ? _crossTargetingVstestTargets : _vstestTargets;
        var imports = crossTargeting
            ? Path.Combine(sdk.SdkDirectory!, "Current", "Microsoft.Common.CrossTargeting.targets", "ImportAfter",
                "Microsoft.TestPlatform.CrossTargeting.targets")
            : Path.Combine(sdk.SdkDirectory!, "Microsoft.TestPlatform.targets");
        foreach (var target in targets)
        {
            if (!project.Targets.TryGetValue(target, out var definition) || !PathsMatch(definition.FullPath, imports))
            {
                return false;
            }
        }

        const string computeFrameworks = "_ComputeTargetFrameworkItems";
        if (crossTargeting && (!project.Targets.TryGetValue(computeFrameworks, out var compute)
            || !PathsMatch(compute.FullPath, Path.Combine(sdk.SdkDirectory!, "Microsoft.Common.CrossTargeting.targets"))))
        {
            return false;
        }

        // A project hook can change the framework set or supply the assembly at execution time.
        var hasProgramTarget = project.Targets.ContainsKey(TestSdkProgramTarget);
        return !project.Targets.Values.Any(target => HasHook(target.BeforeTargets) || HasHook(target.AfterTargets));

        bool HasHook(string names) => names.Length != 0
            && project.ExpandString(names).Split(';', StringSplitOptions.TrimEntries)
                .Any(name => targets.Contains(name)
                    || (crossTargeting && name.Equals(computeFrameworks, StringComparison.OrdinalIgnoreCase))
                    || (hasProgramTarget && name.Equals(TestSdkProgramTarget, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasCustomProjectInputs(Project project, string sdkDirectory) =>
        UsesUnmodeledTestInputs(project, project.Xml)
        || project.Imports.Any(import =>
            !WorkspaceDiskPathFilter.IsPathUnderDirectory(
                import.ImportedProject.FullPath, sdkDirectory, InputPathCanon.Comparison)
            && UsesUnmodeledTestInputs(project, import.ImportedProject));

    private static bool UsesUnmodeledTestInputs(Project project, ProjectRootElement source)
    {
        if (!string.IsNullOrWhiteSpace(source.InitialTargets) && !IsStandardProgramGeneration(project, source))
        {
            return true;
        }

        // CLI-supplied values and its startup directory can select a different output or import.
        var xml = source.RawXml;
        return xml.Contains("$(VSTestTestCaseFilter)", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("$(VSTestLogger)", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("$(VSTestResultsDirectory)", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("$(VSTestVerbosity)", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("$(MSBuildStartupDirectory)", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStandardProgramGeneration(Project project, ProjectRootElement source)
    {
        // Microsoft.NET.Test.Sdk adds an initial target to insert a compiler entry point. Check the
        // evaluated definition so a project can still override or hook the target at run time.
        if (!source.InitialTargets.Trim().Equals(TestSdkProgramTarget, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(source.FullPath).Equals("Microsoft.NET.Test.Sdk.targets", StringComparison.OrdinalIgnoreCase)
            || !project.Targets.TryGetValue(TestSdkProgramTarget, out var target)
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

    private static bool IsTrue(Project project, string property) =>
        string.Equals(project.GetPropertyValue(property), "true", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> _vstestTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "VSTest", "_VSTestConsole", "_VSTestMSBuild", "ShowInfoMessageIfProjectHasNoIsTestProjectProperty",
        "ShowCallOfVSTestTaskWithParameter", "_TestRunStart",
    };

    private static readonly HashSet<string> _crossTargetingVstestTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "VSTest", "DispatchToInnerBuildsWithVSTestTarget", "SetVSTestInnerTarget",
    };
}
