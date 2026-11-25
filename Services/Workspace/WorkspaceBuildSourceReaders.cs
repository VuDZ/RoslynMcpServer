using System.Globalization;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Build;
using RoslynMcpServer.Services.Workspace.Models;
using BuildProject = Microsoft.Build.Evaluation.Project;
using Project = Microsoft.CodeAnalysis.Project;

namespace RoslynMcpServer.Services.Workspace;

internal static class WorkspaceBuildSourceReaders
{
    internal static async Task<bool> MatchAsync(
        string workspacePath,
        string? configuration,
        string? platform,
        string? solutionPlatform,
        string? buildArgs,
        IEnumerable<Project> loadedProjects,
        IReadOnlyList<Project> readers,
        IReadOnlyDictionary<ProjectId, ProjectLoadMetadata> metadata,
        string? sourcePath,
        CancellationToken cancellationToken)
    {
        // Extra CLI switches and response files can replace the evaluated build properties or targets.
        if (buildArgs is not null || HasResponseFile(workspacePath))
        {
            return false;
        }

        var sdk = await DotNetSdkResolver.ResolveAsync(
                DotNetHostResolver.ResolveDotNetExecutable(),
                WorkspaceRootResolver.ResolveDotNetWorkingDirectory(workspacePath),
                DotNetSdkResolver.DefaultTimeout,
                cancellationToken)
            .ConfigureAwait(false);
        var sdkPath = sdk.SdkDirectory;
        if (sdkPath is null
            || !InputPathCanon.Comparer.Equals(Path.GetDirectoryName(typeof(BuildProject).Assembly.Location), sdkPath)
            || File.Exists(Path.Combine(sdkPath, "MSBuild.rsp")))
        {
            return false;
        }

        var solution = Path.GetExtension(workspacePath).ToLowerInvariant() is ".sln" or ".slnx"
            ? SolutionFile.Parse(workspacePath)
            : null;
        // The workspace TFM filter is design-time only. The default CLI build sees all configured TFMs.
        var properties = MsBuildWorkspaceProperties.Create(configuration, platform);
        var solutionConfiguration = solution is null
            ? null
            : $"{configuration ?? solution.GetDefaultConfigurationName()}|{solutionPlatform ?? solution.GetDefaultPlatformName()}";
        var solutionProjects = solution?.ProjectsInOrder
            .Where(project => IsCSharpProject(project.AbsolutePath))
            .ToDictionary(project => project.AbsolutePath, InputPathCanon.Comparer);
        var projectPaths = loadedProjects
            .Where(project => project.Language == LanguageNames.CSharp && project.FilePath is not null)
            .Select(project => project.FilePath!)
            .Concat(solutionProjects?.Keys ?? Enumerable.Empty<string>())
            .ToHashSet(InputPathCanon.Comparer);
        var readersByPath = readers.ToLookup(project => project.FilePath!, InputPathCanon.Comparer);
        var loadedCodePages = readers.Select(project => metadata.GetValueOrDefault(project.Id).CodePage).ToHashSet();
        var matched = new HashSet<ProjectId>();

        using var collection = new ProjectCollection();
        foreach (var projectPath in projectPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var globals = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase);
            if (solutionProjects is not null)
            {
                if (!solutionProjects.TryGetValue(projectPath, out var solutionProject)
                    || !solutionProject.ProjectConfigurations.TryGetValue(solutionConfiguration!, out var mapping))
                {
                    return false;
                }

                globals["BuildingSolutionFile"] = "true";
                var solutionDirectory = Path.GetDirectoryName(workspacePath)!;
                globals["SolutionDir"] = Path.EndsInDirectorySeparator(solutionDirectory)
                    ? solutionDirectory
                    : solutionDirectory + Path.DirectorySeparatorChar;
                globals["SolutionExt"] = Path.GetExtension(workspacePath);
                globals["SolutionFileName"] = Path.GetFileName(workspacePath);
                globals["SolutionName"] = Path.GetFileNameWithoutExtension(workspacePath);
                globals["SolutionPath"] = workspacePath;
                globals["Configuration"] = mapping.ConfigurationName;
                globals["Platform"] = mapping.PlatformName;
            }

            var outer = collection.LoadProject(projectPath, globals, toolsVersion: null);
            if (!HasStaticInputs(outer, sdkPath, sourcePath, projectPaths))
            {
                return false;
            }

            var frameworks = outer.GetPropertyValue("TargetFrameworks")
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<BuildProject> candidates = frameworks.Length > 0 && outer.GetPropertyValue("TargetFramework").Length == 0
                ? frameworks.Select(framework => collection.LoadProject(
                    projectPath,
                    new Dictionary<string, string>(globals, StringComparer.OrdinalIgnoreCase) { ["TargetFramework"] = framework },
                    toolsVersion: null))
                : [outer];
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(candidate, outer) && !HasStaticInputs(candidate, sdkPath, sourcePath, projectPaths))
                {
                    return false;
                }

                if (sourcePath is not null && !candidate.GetItems("Compile").Any(item =>
                        InputPathCanon.Comparer.Equals(InputPathCanon.TryCanonicalize(item.GetMetadataValue("FullPath")), sourcePath)))
                {
                    continue;
                }

                var framework = candidate.GetPropertyValue("TargetFramework");
                var project = readersByPath[projectPath].SingleOrDefault(reader => metadata.TryGetValue(reader.Id, out var load)
                    && string.Equals(load.TargetFramework ?? string.Empty, framework, StringComparison.Ordinal));
                if (!HasStandardCompilerInputs(candidate, sdkPath)
                    || ReadCodePage(candidate) is not { } codePage || !loadedCodePages.Contains(codePage))
                {
                    return false;
                }

                if (project is not null && (!matched.Add(project.Id) || codePage != metadata[project.Id].CodePage))
                {
                    return false;
                }
            }
        }

        return matched.Count == readers.Count;
    }

    private static bool IsCSharpProject(string path) =>
        string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase);

    private static bool HasResponseFile(string workspacePath)
    {
        for (var directory = Path.GetDirectoryName(workspacePath); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (File.Exists(Path.Combine(directory, "Directory.Build.rsp")))
            {
                return true;
            }
        }

        return false;
    }

    private static int? ReadCodePage(BuildProject project)
    {
        var value = project.GetPropertyValue("CodePage");
        return value.Length == 0
            ? 0
            : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result > 0
                ? result
                : null;
    }

    private static bool HasStandardCompilerInputs(BuildProject project, string sdkPath)
    {
        var coreTargets = Path.Combine(sdkPath, "Roslyn", "Microsoft.CSharp.Core.targets");
        return project.Targets.TryGetValue("CoreCompile", out var compiler)
            && InputPathCanon.Comparer.Equals(compiler.FullPath, coreTargets)
            && _compilerOverrides.All(name => project.GetPropertyValue(name).Length == 0)
            && !project.GetLogicalProject().OfType<ProjectUsingTaskElement>().Any(task =>
                IsCompilerTask(task.TaskName) && !IsInToolset(task.ContainingProject.FullPath, sdkPath));
    }

    private static bool HasStaticInputs(BuildProject project, string sdkPath, string? sourcePath, HashSet<string> projectPaths)
    {
        // A reference that changes the global properties can build a different project instance.
        if (UsesInvocationProperties(project.Xml)
            || project.Imports.Any(import => !IsInToolset(import.ImportedProject.FullPath, sdkPath)
                && UsesInvocationProperties(import.ImportedProject))
            || _dispatchItems.Any(name => project.GetItems(name).Count != 0)
            || project.GetItems("ProjectReference").Any(item =>
                InputPathCanon.TryCanonicalize(item.GetMetadataValue("FullPath")) is not { } path
                || !projectPaths.Contains(path)
                || _referenceOverrides.Any(name => item.GetMetadataValue(name).Length != 0)))
        {
            return false;
        }

        if (string.Equals(project.GetPropertyValue("IsCrossTargetingBuild"), "true", StringComparison.OrdinalIgnoreCase))
        {
            var crossTargeting = Path.Combine(sdkPath, "Microsoft.Common.CrossTargeting.targets");
            if (_dispatchTargets.Any(name => !project.Targets.TryGetValue(name, out var target)
                    || !InputPathCanon.Comparer.Equals(target.FullPath, crossTargeting)))
            {
                return false;
            }
        }

        // Evaluation can read the ordinary property and item graph without executing target tasks.
        // Custom targets can change compiler settings or add source and project readers after evaluation.
        return project.Targets.Values.Where(target => !IsInToolset(target.FullPath, sdkPath)).All(target =>
            !target.Children.OfType<ProjectPropertyGroupTaskInstance>().SelectMany(group => group.Properties)
                .Any(property => IsBuildInputProperty(property.Name))
            && !target.Children.OfType<ProjectItemGroupTaskInstance>().SelectMany(group => group.Items)
                .Any(item => IsDispatchItem(item.ItemType)
                    || (IsTestSdkRemovalItem(item.ItemType) && !IsTestSdkTarget(project, target))
                    || (sourcePath is not null && IsCompileItem(item.ItemType)
                        && !IsTestSdkEntryPoint(project, target, item, sourcePath)))
            && !target.Tasks.Any(task => IsCompilerTask(task.Name)
                || task.Outputs.OfType<ProjectTaskOutputPropertyInstance>().Any(output =>
                    IsBuildInputProperty(output.PropertyName) || IsDynamicName(output.PropertyName))
                || task.Outputs.OfType<ProjectTaskOutputItemInstance>().Any(output =>
                    IsDynamicName(output.ItemType) || IsDispatchItem(output.ItemType)
                    || IsTestSdkRemovalItem(output.ItemType)
                    || (sourcePath is not null && IsCompileItem(output.ItemType)))));
    }

    private static bool UsesInvocationProperties(ProjectRootElement project)
    {
        // Some command-line MSBuild properties are supplied by the invocation or solution traversal.
        var xml = project.RawXml;
        return _invocationProperties.Any(name => xml.Contains("$(" + name + ")", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTestSdkEntryPoint(
        BuildProject project,
        ProjectTargetInstance target,
        ProjectItemGroupTaskItemInstance item,
        string sourcePath)
    {
        // The Test SDK replaces its generated Main. Its item changes do not affect other source files.
        if (!IsTestSdkTarget(project, target))
        {
            return false;
        }

        if (item.Include == "$(GeneratedProgramFile)" && item.Remove.Length == 0)
        {
            var generated = project.GetPropertyValue("GeneratedProgramFile");
            var entryPoint = Path.Combine(Path.GetDirectoryName(target.FullPath)!, "Microsoft.NET.Test.Sdk.Program.cs");
            return InputPathCanon.Comparer.Equals(InputPathCanon.TryCanonicalize(generated), entryPoint)
                && !InputPathCanon.Comparer.Equals(entryPoint, sourcePath);
        }

        return item.Remove == "@(RemoveExistingMicrosoftNETTestSdkProgram)" && item.Include.Length == 0
            && !string.Equals(Path.GetFileNameWithoutExtension(sourcePath), "Microsoft.NET.Test.Sdk.Program",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTestSdkTarget(BuildProject project, ProjectTargetInstance target)
    {
        var packageRoot = project.GetPropertyValue("NuGetPackageRoot");
        return packageRoot.Length != 0 && target.Name == "GenerateProgramFile"
            && string.Equals(Path.GetFileName(target.FullPath), "Microsoft.NET.Test.Sdk.targets", StringComparison.OrdinalIgnoreCase)
            && IsInToolset(target.FullPath, Path.Combine(packageRoot, "microsoft.net.test.sdk"));
    }

    private static bool IsTestSdkRemovalItem(string name) =>
        string.Equals(name, "RemoveExistingMicrosoftNETTestSdkProgram", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompileItem(string name) => string.Equals(name, "Compile", StringComparison.OrdinalIgnoreCase);

    private static bool IsDispatchItem(string name) =>
        string.Equals(name, "ProjectReference", StringComparison.OrdinalIgnoreCase)
        || _dispatchItems.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static bool IsDynamicName(string name) => name.IndexOfAny(['$', '@', '%']) >= 0;

    private static bool IsCompilerTask(string name) =>
        string.Equals(name, "Csc", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".Csc", StringComparison.OrdinalIgnoreCase);

    private static bool IsBuildInputProperty(string name) =>
        _buildInputProperties.Contains(name, StringComparer.OrdinalIgnoreCase)
        || _compilerOverrides.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static bool IsInToolset(string path, string sdkPath) =>
        Path.IsPathFullyQualified(path) && path.StartsWith(sdkPath + Path.DirectorySeparatorChar, InputPathCanon.Comparison);

    private static readonly string[] _compilerOverrides = ["CompilerResponseFile", "CscToolExe", "CscToolPath", "CscEnvironment"];

    private static readonly string[] _buildInputProperties =
    [
        "CodePage", "GeneratedProgramFile", "InnerBuildProperty", "InnerBuildPropertyValues", "InnerTargets",
        "TargetFramework", "TargetFrameworks",
    ];

    private static readonly string[] _dispatchItems =
    [
        "_InnerBuildProjects", "_MSBuildProjectReferenceExistent", "_ProjectReferenceWithConfiguration",
        "_TargetFramework", "_TargetFrameworkNormalized", "ProjectReferenceWithConfiguration",
    ];

    private static readonly string[] _dispatchTargets = ["_ComputeTargetFrameworkItems", "DispatchToInnerBuilds"];

    private static readonly string[] _referenceOverrides =
    [
        "AdditionalProperties", "GlobalPropertiesToRemove", "Properties", "SetConfiguration", "SetPlatform",
        "SetTargetFramework", "Targets", "UndefineProperties",
    ];

    private static readonly string[] _invocationProperties =
    [
        "CurrentSolutionConfigurationContents", "MSBuildInteractive", "MSBuildIsRestoring", "MSBuildNodeCount",
        "MSBuildStartupDirectory",
    ];
}
