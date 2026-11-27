using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Config;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Plugins;
using RoslynMcpServer.Services.Navigation;
using RoslynMcpServer.Services.Workspace;
using RoslynMcpServer.Services.Workspace.Models;

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
        => BuildInfoMarkdown(loadedSolution, activation, fileSettings, loadSource, loadInProgress, lazyLoadFailure, pluginStartup, diskWatchers: null);

    /// <inheritdoc cref="BuildInfoMarkdown(Solution?, McpToolActivationService, RoslynMcpFileSettings?, WorkspaceLoadSource, bool, string?, PluginStartup.Report?)"/>
    /// <param name="diskWatchers">
    /// Watchers of the current workspace session; <see langword="null"/> keeps the watcher lines out of the text.
    /// </param>
    public static string BuildInfoMarkdown(
        Solution? loadedSolution,
        McpToolActivationService activation,
        RoslynMcpFileSettings? fileSettings,
        WorkspaceLoadSource loadSource,
        bool loadInProgress,
        string? lazyLoadFailure,
        PluginStartup.Report? pluginStartup,
        WorkspaceDiskWatcherStatus? diskWatchers)
        => BuildInfoMarkdown(
            loadedSolution,
            activation,
            fileSettings,
            loadSource,
            loadInProgress,
            lazyLoadFailure,
            pluginStartup,
            diskWatchers,
            successfulPhysicalWorkspaceLoads: null);

    internal static string BuildInfoMarkdown(
        Solution? loadedSolution,
        McpToolActivationService activation,
        RoslynMcpFileSettings? fileSettings,
        WorkspaceLoadSource loadSource,
        bool loadInProgress,
        string? lazyLoadFailure,
        PluginStartup.Report? pluginStartup,
        WorkspaceDiskWatcherStatus? diskWatchers,
        long? successfulPhysicalWorkspaceLoads)
    {
        ArgumentNullException.ThrowIfNull(activation);
        var directories = ServerUserDirectories.FromEnvironment();
        var latestLog = FindLatestLog(directories.LogDirectory);

        var sb = new StringBuilder();
        sb.AppendLine("## Roslyn MCP server info");
        sb.AppendLine();
        AppendServerIdentity(sb);
        sb.AppendLine($"- **Base directory:** `{AppContext.BaseDirectory}`");
        sb.AppendLine($"- **Process working directory:** `{Environment.CurrentDirectory}`");
        sb.AppendLine($"- **User configuration directory:** `{directories.ConfigurationDirectory}`");
        sb.AppendLine($"- **User plugin directory:** `{directories.PluginDirectory}`");
        sb.AppendLine($"- **Log directory:** `{directories.LogDirectory}`");
        sb.AppendLine($"- **Usage directory:** `{directories.UsageDirectory}`");
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
        if (successfulPhysicalWorkspaceLoads is { } loadCount)
        {
            sb.AppendLine($"- **Process working set:** {FormatProcessWorkingSet()}");
            sb.AppendLine($"- **Successful physical workspace loads (since server start):** {loadCount}");
        }

        AppendDiskWatchersSection(sb, diskWatchers);

        AppendFileSettingsSection(sb, fileSettings, lazyLoadFailure);

        sb.AppendLine();
        sb.AppendLine("After code changes, publish the server and reconnect your MCP client.");
        return sb.ToString().TrimEnd();
    }

    private static void AppendServerIdentity(StringBuilder sb)
    {
        var assembly = typeof(McpServerInfoHelper).Assembly;
        var assemblyName = assembly.GetName();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var processPath = Environment.ProcessPath;
        var assemblyPath = assembly.Location;

        sb.AppendLine($"- **Assembly:** `{assemblyName.Name}` v{assemblyName.Version}");
        sb.AppendLine($"- **Informational version:** {FormatIdentityValue(informationalVersion)}");
        sb.AppendLine($"- **Module version ID:** `{assembly.ManifestModule.ModuleVersionId}`");
        sb.AppendLine($"- **Process path:** {FormatIdentityValue(processPath)}");

        // A locationless assembly can also be loaded from memory. Only the server's own entry assembly
        // in the default load context can use the single-file host as its backing file.
        // https://learn.microsoft.com/dotnet/core/deploying/single-file/overview
        if (assemblyPath.Length == 0
            && assembly == Assembly.GetEntryAssembly()
            && AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default)
        {
            sb.AppendLine("- **Server assembly path:** (embedded in single-file bundle)");
            sb.AppendLine($"- **Single-file bundle modified (UTC):** {FormatLastWriteTimeUtc(processPath)}");
            return;
        }

        sb.AppendLine($"- **Server assembly path:** {FormatIdentityValue(assemblyPath)}");
        sb.AppendLine($"- **Server assembly modified (UTC):** {FormatLastWriteTimeUtc(assemblyPath)}");
    }

    private static string FormatIdentityValue(string? value) =>
        string.IsNullOrEmpty(value) ? "(unavailable)" : $"`{value}`";

    private static string FormatLastWriteTimeUtc(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "(unavailable)";
        }

        var file = new FileInfo(path);
        return file.Exists ? file.LastWriteTimeUtc.ToString("O") : "(unavailable)";
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

        foreach (var directory in pluginStartup.DropInDirectories)
        {
            sb.AppendLine($"- **Plugin drop-in source:** `{directory}`");
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

    /// <summary>
    /// Appends the watcher count and up to <see cref="MaxListedWatcherFailures"/> failed directories with their
    /// reason. A Linux inotify limit fails dozens of directories with one message, so the rest are only counted.
    /// </summary>
    private static void AppendDiskWatchersSection(StringBuilder sb, WorkspaceDiskWatcherStatus? diskWatchers)
    {
        if (diskWatchers is null)
        {
            return;
        }

        sb.AppendLine($"- **Disk watchers:** {diskWatchers.Started} ({diskWatchers.Recursive} recursive)");
        if (diskWatchers.Failures.Count == 0)
        {
            return;
        }

        sb.AppendLine($"- **Disk watcher start failures:** {diskWatchers.Failures.Count}");
        foreach (var failure in diskWatchers.Failures.Take(MaxListedWatcherFailures))
        {
            sb.AppendLine($"- **Disk watcher failed:** `{failure.Directory}` — {failure.Reason}");
        }

        if (diskWatchers.Failures.Count > MaxListedWatcherFailures)
        {
            sb.AppendLine($"- **Disk watcher failures not listed:** {diskWatchers.Failures.Count - MaxListedWatcherFailures} (see the server log)");
        }
    }

    private const int MaxListedWatcherFailures = 5;

    private static string FormatProcessWorkingSet()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return FormattableString.Invariant($"{process.WorkingSet64 / (1024.0 * 1024.0):F1} MiB");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return "(unavailable)";
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
        sb.AppendLine("- **RoslynMcp.jsonc (user):** "
            + FormatFileProbe(fileSettings.UserFilePath, fileSettings.UserFilePresent));
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
        sb.AppendLine($"- **Merged require-solution:** {(fileSettings.RequireSolution ? "true" : "false")}");
        sb.AppendLine($"- **Merged direct-project-guidance:** {(fileSettings.DirectProjectGuidance is null ? "(default)" : "(custom)")}");
        sb.AppendLine($"- **Merged configuration:** {(fileSettings.Configuration is null ? "(none)" : $"`{fileSettings.Configuration}`")}");
        sb.AppendLine($"- **Merged platform:** {(fileSettings.Platform is null ? "(none)" : $"`{fileSettings.Platform}`")}");
        sb.AppendLine($"- **Merged target-framework:** {(fileSettings.TargetFramework is null ? "(none)" : $"`{fileSettings.TargetFramework}`")}");
        sb.AppendLine($"- **Merged max-results:** {(fileSettings.MaxResults is null ? "(none)" : fileSettings.MaxResults.Value.ToString())}");
        sb.AppendLine($"- **Merged preview:** {(fileSettings.Preview is null ? "(none)" : fileSettings.Preview.Value ? "true" : "false")}");
        sb.AppendLine($"- **Merged search-prewarm:** {(fileSettings.SearchPrewarm is null ? "(none)" : fileSettings.SearchPrewarm.Value ? "true" : "false")} (ROSLYN_MCP_SEARCH_PREWARM wins; default on)");
        var prewarmEnvironment = Environment.GetEnvironmentVariable(SearchIndexPrewarm.EnvironmentVariableName);
        var prewarmSource = !string.IsNullOrWhiteSpace(prewarmEnvironment) ? "environment"
            : fileSettings.SearchPrewarm is not null ? "RoslynMcp.jsonc"
            : "default";
        sb.AppendLine($"- **Search prewarm in effect:** {(SearchIndexPrewarm.IsEnabled(fileSettings.SearchPrewarm, prewarmEnvironment) ? "on" : "off")} ({prewarmSource})");
        sb.AppendLine($"- **Merged buildhost-optimizations:** {(fileSettings.BuildHostOptimizations is null ? "(none)" : fileSettings.BuildHostOptimizations.Value ? "true" : "false")} (ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS wins; default on)");
        sb.AppendLine($"- **Merged buildhost-evaluate-ahead-workers:** {(fileSettings.BuildHostEvaluateAheadWorkers is null ? "(none)" : fileSettings.BuildHostEvaluateAheadWorkers.Value.ToString())} (ROSLYN_MCP_BUILDHOST_EVALUATE_AHEAD_WORKERS wins; default {BuildHostOptimizations.DefaultEvaluateAheadWorkers}; 0 turns evaluation ahead off)");
        var buildHost = BuildHostOptimizations.Resolve(fileSettings.BuildHostOptimizations, fileSettings.BuildHostEvaluateAheadWorkers);
        sb.AppendLine($"- **BuildHost optimizations in effect:** references first {(buildHost.ReferencesFirst ? "on" : "off")}, evaluation ahead {(buildHost.EvaluateAheadWorkers > 0 ? $"{buildHost.EvaluateAheadWorkers} worker(s)" : "off")}, shared SDK resolution cache {(buildHost.SharedSdkCache ? "on" : "off")}");
        sb.AppendLine($"- **Merged ripgrep-path:** {(fileSettings.RipgrepPath is null ? "(none)" : $"`{fileSettings.RipgrepPath}`")} (stored; unused until search opts in)");

        if (!string.IsNullOrWhiteSpace(lazyLoadFailure))
        {
            sb.AppendLine("- **Lazy load failure:** see report below");
            sb.AppendLine();
            sb.AppendLine(lazyLoadFailure.TrimEnd());
        }
    }

    private static string? FindLatestLog(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "mcp-*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
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
