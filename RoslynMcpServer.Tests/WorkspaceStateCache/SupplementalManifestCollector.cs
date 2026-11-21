using System.Text.Json;
using Microsoft.Build.Evaluation;
using RoslynMcpServer.Tests.WorkspaceStateCache.Models;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

/// <summary>
/// Test-only source probe, never a reusable capture. Public evaluation cannot attest remote consumption:
/// https://github.com/VuDZ/RoslynMcpServer/blob/main/docs/workspace-state-cache/epoch-1-disk-cache-mvp/spec.md
/// </summary>
internal static class SupplementalManifestCollector
{
    public static SupplementalEvaluation[] Collect(IEnumerable<string> projectPaths, int pathBudget = 4096)
    {
        // These are a candidate design-time context, not a claim of equality with every dependent build context.
        var globals = new Dictionary<string, string>
        {
            ["DesignTimeBuild"] = "true",
            ["BuildingInsideVisualStudio"] = "true",
            ["BuildProjectReferences"] = "false",
            ["SkipCompilerExecution"] = "true",
            ["ProvideCommandLineArgs"] = "true",
        };
        using var collection = new ProjectCollection(globals);
        var result = new List<SupplementalEvaluation>();
        foreach (var path in projectPaths)
        {
            var project = new Project(
                path,
                globals,
                null,
                collection,
                ProjectLoadSettings.RecordDuplicateButNotCircularImports);
            var imports = project.ImportsIncludingDuplicates.Select(import => import.ImportedProject.FullPath).ToArray();
            var compile = ReadItems(project, "Compile");
            if (imports.Length + compile.Length > pathBudget)
            {
                throw new InvalidDataException("The supplemental input path budget was exceeded.");
            }
            var properties = _propertyNames.ToDictionary(name => name, project.GetPropertyValue);
            var assetsPath = properties["ProjectAssetsFile"];
            var configs = ReadRestoredConfigs(assetsPath);
            result.Add(new SupplementalEvaluation(
                path,
                project.LastEvaluationId,
                new Dictionary<string, string>(project.GlobalProperties),
                properties,
                imports,
                compile,
                ReadItems(project, "ProjectReference"),
                ReadItems(project, "ReferencePath"),
                configs));
        }
        return result.ToArray();
    }

    private static string[] ReadItems(Project project, string itemType) => project.GetItems(itemType)
        .Select(item => Path.GetFullPath(item.EvaluatedInclude, project.DirectoryPath)).Order().ToArray();

    private static string[] ReadRestoredConfigs(string assetsPath)
    {
        if (!File.Exists(assetsPath))
        {
            throw new IOException("The candidate assets input is missing.");
        }
        if (new FileInfo(assetsPath).Length > 16 * 1024 * 1024)
        {
            throw new InvalidDataException("The candidate assets byte budget was exceeded.");
        }
        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        // Restore-time provenance is separate from current NuGet applicability and ordinary consumption.
        return document.RootElement.GetProperty("project").GetProperty("restore")
            .GetProperty("configFilePaths").EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    private static readonly string[] _propertyNames =
    [
        "Configuration", "Platform", "TargetFramework", "TargetFrameworks", "DefineConstants", "LangVersion",
        "Nullable", "ProjectAssetsFile", "NuGetLockFilePath", "RestorePackagesWithLockFile", "RestoreLockedMode",
        "MSBuildProjectExtensionsPath", "BaseIntermediateOutputPath", "IntermediateOutputPath",
        "NETCoreSdkVersion", "MSBuildSDKsPath", "MSBuildVersion",
    ];
}
