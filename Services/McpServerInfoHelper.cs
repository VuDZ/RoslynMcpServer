using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services;

public static class McpServerInfoHelper
{
    public static string BuildInfoMarkdown(Solution? loadedSolution)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var exePath = Environment.ProcessPath ?? assembly.Location;
        var exeTime = File.Exists(exePath) ? File.GetLastWriteTime(exePath) : (DateTime?)null;
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        var latestLog = Directory.Exists(logDir)
            ? Directory.EnumerateFiles(logDir, "mcp-*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;

        var toolCount = WorkspaceHealthReporter.CountRegisteredTools();
        var sb = new StringBuilder();
        sb.AppendLine("## Roslyn MCP server info");
        sb.AppendLine();
        sb.AppendLine($"- **Assembly:** `{assembly.GetName().Name}` v{assembly.GetName().Version}");
        sb.AppendLine($"- **Process path:** `{exePath}`");
        if (exeTime is not null)
        {
            sb.AppendLine($"- **Binary modified (local):** {exeTime:O}");
        }

        sb.AppendLine($"- **Base directory:** `{AppContext.BaseDirectory}`");
        sb.AppendLine($"- **Registered MCP tools:** {toolCount}");
        sb.AppendLine($"- **Latest log file:** {(latestLog is null ? "(none yet)" : $"`{latestLog}`")}");
        sb.AppendLine($"- **Workspace loaded:** {(loadedSolution is null ? "no" : $"yes ({loadedSolution.ProjectIds.Count} projects)")}");
        sb.AppendLine();
        sb.AppendLine("After code changes run `dotnet publish -c Release -r win-x64`, then Reload MCP in Cursor.");
        return sb.ToString().TrimEnd();
    }

}
