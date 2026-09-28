using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Config;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Plugins;

namespace RoslynMcpServer.Services;

public static class McpServerInfoHelper
{
    public static string BuildInfoMarkdown(Solution? loadedSolution, McpToolActivationService activation)
        => BuildInfoMarkdown(loadedSolution, activation, fileSettings: null, loadSource: WorkspaceLoadSource.None, loadInProgress: false, lazyLoadFailure: null);

    public static string BuildInfoMarkdown(
        Solution? loadedSolution,
        McpToolActivationService activation,
        RoslynMcpFileSettings? fileSettings,
        WorkspaceLoadSource loadSource,
        bool loadInProgress,
        string? lazyLoadFailure)
        => BuildInfoMarkdown(loadedSolution, activation, fileSettings, loadSource, loadInProgress, lazyLoadFailure, pluginStartup: null);

    /// <summary>
    /// Info text of this host, with the plugins of the start pass of this process.
    /// </summary>
    /// <param name="loadedSolution">Published solution, or <see langword="null"/> when none is loaded.</param>
    /// <param name="activation">Tool surface of this process; the tool count is read from its live collection.</param>
    /// <param name="fileSettings">Settings read from <c>RoslynMcp.jsonc</c>, or <see langword="null"/>.</param>
    /// <param name="loadSource">Where the loaded solution came from.</param>
    /// <param name="loadInProgress">True while a lazy load is running.</param>
    /// <param name="lazyLoadFailure">Report of a failed lazy load, or <see langword="null"/>.</param>
    /// <param name="pluginStartup">
    /// Report of the plugin start pass of this process. Its lines are appended to the current text and never
    /// replace one; <see langword="null"/> keeps the plugin section out of the text, which is what the
    /// overloads without a report do.
    /// </param>
    /// <returns>Markdown of the current host state.</returns>
    public static string BuildInfoMarkdown(
        Solution? loadedSolution,
        McpToolActivationService activation,
        RoslynMcpFileSettings? fileSettings,
        WorkspaceLoadSource loadSource,
        bool loadInProgress,
        string? lazyLoadFailure,
        PluginStartup.Report? pluginStartup)
    {
        ArgumentNullException.ThrowIfNull(activation);
        var assembly = Assembly.GetExecutingAssembly();
        var exePath = Environment.ProcessPath ?? assembly.Location;
        var exeTime = File.Exists(exePath) ? File.GetLastWriteTime(exePath) : (DateTime?)null;
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        var latestLog = Directory.Exists(logDir)
            ? Directory.EnumerateFiles(logDir, "mcp-*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;

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
        sb.AppendLine($"- **Process working directory:** `{Environment.CurrentDirectory}`");
        sb.AppendLine($"- **Tool profile:** `{activation.Profile}`");
        sb.AppendLine($"- **Startup tool groups:** {activation.FormatStartupGroupsMarkdown()}");
        sb.AppendLine($"- **Dynamic tool groups:** {activation.FormatDynamicGroupsMarkdown()}");
        sb.AppendLine($"- **Registered MCP tools:** {activation.CurrentToolCount}");
        sb.AppendLine($"- **Latest log file:** {(latestLog is null ? "(none yet)" : $"`{latestLog}`")}");

        AppendPluginsSection(sb, pluginStartup);

        if (loadInProgress)
        {
            sb.AppendLine("- **Workspace loaded:** loading");
        }
        else if (loadedSolution is null)
        {
            sb.AppendLine("- **Workspace loaded:** no");
        }
        else
        {
            sb.AppendLine($"- **Workspace loaded:** yes ({loadedSolution.ProjectIds.Count} projects)");
        }

        sb.AppendLine($"- **Workspace load source:** {FormatLoadSource(loadSource, loadedSolution is not null, loadInProgress)}");

        AppendFileSettingsSection(sb, fileSettings, lazyLoadFailure);

        sb.AppendLine();
        sb.AppendLine("After code changes run `dotnet publish -c Release -r win-x64`, then Reload MCP in Cursor.");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Appends the plugin lines of one start pass: the entry file and the tool names of every loaded plugin,
    /// then one line per refused source with its reason.
    /// </summary>
    /// <param name="sb">Info text under construction.</param>
    /// <param name="pluginStartup">Report of the start pass, or <see langword="null"/> to add no line at all.</param>
    private static void AppendPluginsSection(StringBuilder sb, PluginStartup.Report? pluginStartup)
    {
        if (pluginStartup is null)
        {
            return;
        }

        if (pluginStartup.IsEmpty)
        {
            sb.AppendLine("- **Plugins:** (none)");
            return;
        }

        foreach (var plugin in pluginStartup.Loaded)
        {
            var tools = plugin.ToolNames.Count == 0
                ? "(none)"
                : string.Join(", ", plugin.ToolNames.Select(name => $"`{name}`"));

            sb.AppendLine($"- **Plugin `{plugin.Id}`:** `{plugin.EntryPath}` — tools: {tools}");
        }

        foreach (var skip in pluginStartup.Skipped)
        {
            sb.AppendLine($"- **Plugin skipped `{skip.Plugin}`:** {skip.Reason}");
        }
    }

    private static string FormatLoadSource(WorkspaceLoadSource source, bool hasSolution, bool loadInProgress)
    {
        if (loadInProgress)
        {
            return "loading";
        }

        if (!hasSolution)
        {
            return "not loaded";
        }

        return source switch
        {
            WorkspaceLoadSource.ConfigFile => "RoslynMcp.jsonc (lazy)",
            WorkspaceLoadSource.ExplicitLoad => "load_workspace (explicit)",
            _ => "unknown",
        };
    }

    private static void AppendFileSettingsSection(
        StringBuilder sb,
        RoslynMcpFileSettings? fileSettings,
        string? lazyLoadFailure)
    {
        if (fileSettings is null || fileSettings == RoslynMcpFileSettings.Empty)
        {
            sb.AppendLine("- **RoslynMcp.jsonc:** (none)");
            return;
        }

        sb.AppendLine("- **RoslynMcp.jsonc (exe):** "
            + FormatFileProbe(fileSettings.ExecutableFilePath, fileSettings.ExecutableFilePresent));
        sb.AppendLine("- **RoslynMcp.jsonc (cwd):** "
            + FormatFileProbe(fileSettings.WorkingDirectoryFilePath, fileSettings.WorkingDirectoryFilePresent));

        if (fileSettings.ParseFailures.Count > 0)
        {
            foreach (var failure in fileSettings.ParseFailures)
            {
                sb.AppendLine($"- **RoslynMcp.jsonc parse failure:** `{failure.Path}` — {failure.Message}");
            }
        }

        if (fileSettings.UnknownKeys.Count > 0)
        {
            sb.AppendLine("- **RoslynMcp.jsonc unknown keys:** "
                + string.Join(", ", fileSettings.UnknownKeys.Select(k => $"`{k}`")));
        }

        sb.AppendLine($"- **Merged workspace-path:** {(fileSettings.WorkspacePath is null ? "(none)" : $"`{fileSettings.WorkspacePath}`")}");
        sb.AppendLine($"- **Merged configuration:** {(fileSettings.Configuration is null ? "(none)" : $"`{fileSettings.Configuration}`")}");
        sb.AppendLine($"- **Merged platform:** {(fileSettings.Platform is null ? "(none)" : $"`{fileSettings.Platform}`")}");
        sb.AppendLine($"- **Merged target-framework:** {(fileSettings.TargetFramework is null ? "(none)" : $"`{fileSettings.TargetFramework}`")}");
        sb.AppendLine($"- **Merged max-results:** {(fileSettings.MaxResults is null ? "(none)" : fileSettings.MaxResults.Value.ToString())}");
        sb.AppendLine($"- **Merged preview:** {(fileSettings.Preview is null ? "(none)" : fileSettings.Preview.Value ? "true" : "false")}");
        sb.AppendLine($"- **Merged ripgrep-path:** {(fileSettings.RipgrepPath is null ? "(none)" : $"`{fileSettings.RipgrepPath}`")} (stored; unused until search opts in)");

        if (!string.IsNullOrWhiteSpace(lazyLoadFailure))
        {
            sb.AppendLine("- **Lazy load failure:** see report below");
            sb.AppendLine();
            sb.AppendLine(lazyLoadFailure.TrimEnd());
        }
    }

    private static string FormatFileProbe(string? path, bool present)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "(not probed)";
        }

        return present ? $"`{path}` (present)" : $"`{path}` (absent)";
    }
}
