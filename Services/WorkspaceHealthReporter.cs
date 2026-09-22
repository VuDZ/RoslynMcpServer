using System.Text;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Hosting;

namespace RoslynMcpServer.Services;

/// <summary>Compact workspace health block for <c>load_workspace</c> responses.</summary>
public static class WorkspaceHealthReporter
{
    public static string BuildHealthSection(
        string workspacePath,
        Solution solution,
        McpToolActivationService activation,
        string? configuration = null,
        string? platform = null,
        string? targetFramework = null,
        string? buildArgs = null)
    {
        ArgumentNullException.ThrowIfNull(activation);
        var fullPath = Path.GetFullPath(workspacePath);
        var workDir = WorkspaceRootResolver.ResolveDotNetWorkingDirectory(fullPath);
        var globalJson = GlobalJsonSdkReader.FindGlobalJsonPath(workDir);
        var pinnedSdk = GlobalJsonSdkReader.TryGetPinnedSdkVersion(workDir);
        var sdkDir = GlobalJsonSdkReader.TryResolveSdkDirectory(workDir, prefer64Bit: Environment.Is64BitProcess);

        var sb = new StringBuilder();
        sb.AppendLine("### Workspace health");
        sb.AppendLine($"- **Solution/project:** `{fullPath}`");
        sb.AppendLine($"- **DotNet working directory:** `{workDir}`");
        sb.AppendLine($"- **Projects loaded:** {solution.Projects.Count()}");
        sb.AppendLine(
            $"- **MSBuild Configuration:** {(string.IsNullOrWhiteSpace(configuration) ? "(SDK/workspace default)" : $"`{configuration}`")}");
        sb.AppendLine(
            $"- **MSBuild Platform:** {(string.IsNullOrWhiteSpace(platform) ? "(SDK/workspace default)" : $"`{platform}`")}");
        sb.AppendLine(
            $"- **MSBuild TargetFramework:** {(string.IsNullOrWhiteSpace(targetFramework) ? "(SDK/workspace default)" : $"`{targetFramework}`")}");
        sb.AppendLine($"- **BuildArgs:** {DotNetBuildArguments.FormatMetadata(buildArgs)}");
        sb.AppendLine($"- **global.json:** {(globalJson is null ? "(not found)" : $"`{globalJson}`")}");
        sb.AppendLine($"- **Pinned SDK (global.json):** {(pinnedSdk ?? "(none)")}");
        sb.AppendLine($"- **Resolved SDK directory:** {(sdkDir ?? "(not resolved)")}");
        sb.AppendLine($"- **Restore assets:** {DescribeRestoreAssets(solution)}");
        sb.AppendLine($"- **Tool profile:** `{activation.Profile}`");
        sb.AppendLine($"- **Startup tool groups:** {activation.FormatStartupGroupsMarkdown()}");
        sb.AppendLine($"- **Dynamic tool groups:** {activation.FormatDynamicGroupsMarkdown()}");
        sb.AppendLine($"- **Registered MCP tools:** {activation.CurrentToolCount} (use `get_mcp_server_info` for binary path)");
        sb.AppendLine();
        sb.AppendLine(
            "> **Workflow:** Call `load_workspace` first. Build/test/run via `run_dotnet_build`, `run_dotnet_test`, `run_dotnet_run` — not raw shell `dotnet`. "
            + "Find usages: `find_usages` / `find_symbol_references` (alias: find_references).");
        return sb.ToString().TrimEnd();
    }

    private static string DescribeRestoreAssets(Solution solution)
    {
        var projects = solution.Projects
            .Where(p => !string.IsNullOrWhiteSpace(p.FilePath))
            .ToList();

        if (projects.Count == 0)
        {
            return "unknown (no project paths)";
        }

        var withAssets = 0;
        foreach (var project in projects)
        {
            var outputPath = project.CompilationOutputInfo.AssemblyPath ?? project.OutputFilePath;
            if (HasRestoreAssets(project.FilePath!, outputPath))
            {
                withAssets++;
            }
        }

        return withAssets == projects.Count
            ? $"ok ({withAssets}/{projects.Count} projects have project.assets.json)"
            : $"incomplete ({withAssets}/{projects.Count} — run `dotnet restore` at solution root, then `reset_workspace` + `load_workspace`)";
    }

    /// <summary>
    /// SDK projects keep <c>project.assets.json</c> under <c>obj</c> next to the project.
    /// Arcade and other redirected layouts put it under <c>artifacts/obj/&lt;ProjectName&gt;</c>
    /// or beside the <c>bin</c> folder of <paramref name="outputFilePath"/>.
    /// A missing directory is "no assets", not a failed <c>load_workspace</c>.
    /// </summary>
    internal static bool HasRestoreAssets(string projectFilePath, string? outputFilePath)
    {
        var projectDir = Path.GetDirectoryName(projectFilePath);
        if (projectDir is not null && DirectoryContainsAssets(Path.Combine(projectDir, "obj")))
        {
            return true;
        }

        if (OutputBinHasSiblingObjAssets(outputFilePath))
        {
            return true;
        }

        return ArcadeArtifactsHasAssets(projectFilePath);
    }

    private static bool DirectoryContainsAssets(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }

            if (File.Exists(Path.Combine(directory, "project.assets.json")))
            {
                return true;
            }

            return Directory.EnumerateFiles(directory, "project.assets.json", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool OutputBinHasSiblingObjAssets(string? outputFilePath)
    {
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            return false;
        }

        var current = Path.GetDirectoryName(outputFilePath);
        string? child = null;
        while (!string.IsNullOrEmpty(current))
        {
            if (string.Equals(Path.GetFileName(current), "bin", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(child))
            {
                var parent = Path.GetDirectoryName(current);
                if (parent is null)
                {
                    return false;
                }

                return DirectoryContainsAssets(Path.Combine(parent, "obj", child));
            }

            child = Path.GetFileName(current);
            var parentDir = Path.GetDirectoryName(current);
            if (string.Equals(parentDir, current, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            current = parentDir;
        }

        return false;
    }

    private static bool ArcadeArtifactsHasAssets(string projectFilePath)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
        if (string.IsNullOrEmpty(projectName))
        {
            return false;
        }

        var dir = Path.GetDirectoryName(projectFilePath);
        while (!string.IsNullOrEmpty(dir))
        {
            var assets = Path.Combine(dir, "artifacts", "obj", projectName, "project.assets.json");
            if (File.Exists(assets))
            {
                return true;
            }

            var parent = Path.GetDirectoryName(dir);
            if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            dir = parent;
        }

        return false;
    }
}
