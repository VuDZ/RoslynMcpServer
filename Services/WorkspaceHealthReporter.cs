using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcpServer.Hosting;

namespace RoslynMcpServer.Services;

/// <summary>Compact workspace health block for <c>load_workspace</c> responses.</summary>
public static class WorkspaceHealthReporter
{
    public static string BuildHealthSection(string workspacePath, Solution solution)
    {
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
        sb.AppendLine($"- **global.json:** {(globalJson is null ? "(not found)" : $"`{globalJson}`")}");
        sb.AppendLine($"- **Pinned SDK (global.json):** {(pinnedSdk ?? "(none)")}");
        sb.AppendLine($"- **Resolved SDK directory:** {(sdkDir ?? "(not resolved)")}");
        sb.AppendLine($"- **Restore assets:** {DescribeRestoreAssets(solution)}");
        sb.AppendLine($"- **Registered MCP tools:** {CountRegisteredTools()} (use `get_mcp_server_info` for binary path)");
        sb.AppendLine();
        sb.AppendLine(
            "> **Workflow:** Call `load_workspace` first. Build/test/run via `run_dotnet_build`, `run_dotnet_test`, `run_dotnet_run` — not raw shell `dotnet`. "
            + "Find usages: `find_usages` / `find_symbol_references` (alias: find_references).");
        return sb.ToString().TrimEnd();
    }

    private static string DescribeRestoreAssets(Solution solution)
    {
        var projectPaths = solution.Projects
            .Select(p => p.FilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();

        if (projectPaths.Count == 0)
        {
            return "unknown (no project paths)";
        }

        var withAssets = 0;
        foreach (var csproj in projectPaths)
        {
            var projectDir = Path.GetDirectoryName(csproj);
            if (projectDir is null)
            {
                continue;
            }

            if (Directory.EnumerateFiles(Path.Combine(projectDir, "obj"), "project.assets.json", SearchOption.AllDirectories)
                .Any())
            {
                withAssets++;
            }
        }

        return withAssets == projectPaths.Count
            ? $"ok ({withAssets}/{projectPaths.Count} projects have obj/project.assets.json)"
            : $"incomplete ({withAssets}/{projectPaths.Count} — run `dotnet restore` at solution root, then `reset_workspace` + `load_workspace`)";
    }

    public static int CountRegisteredTools()
    {
        return McpToolRegistry.ToolTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Count(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);
    }
}
