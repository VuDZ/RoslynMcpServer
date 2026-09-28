using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Config;

namespace RoslynMcpServer.Plugins;

/// <summary>
/// The one host start step that turns the plugin sources of this process into loaded plugin tools, before
/// <c>IHost.Build</c>.
/// </summary>
/// <remarks>
/// <para>
/// The step calls discovery, the shadow copy of a development directory and
/// <see cref="PluginAssemblyLoad.Load"/> itself and keeps one report of the pass. Every refused source gets a
/// <c>plugin skipped</c> line on the writer the host passed in, at the moment the refusal is known; the same
/// lines reach the host log through <see cref="WriteSkipsToLog"/> once a logger exists, and no second
/// discovery pass is made for them.
/// </para>
/// <para>
/// A plugin that cannot be loaded is a skip of that plugin alone: the remaining plugins and the built-in
/// tools still start. An exception of this method itself is a start failure of the whole host, like a broken
/// built-in tool catalog, and the host entry point reports it instead of starting the host.
/// </para>
/// </remarks>
public static class PluginStartup
{
    /// <summary>
    /// Reads the plugin sources, copies a development directory, loads every accepted plugin into the host
    /// collection and returns the report of the pass.
    /// </summary>
    /// <param name="services">Host collection, after <c>AddRoslynMcpServerTools</c> and before <c>Build</c>.</param>
    /// <param name="settings">
    /// Settings already registered in <paramref name="services"/>; the <c>plugins</c> key of the file is the
    /// second plugin source.
    /// </param>
    /// <param name="baseDirectory">Host base directory; the drop-in root is <c>{baseDirectory}/plugins</c>.</param>
    /// <param name="environmentPaths">
    /// Raw <c>ROSLYN_MCP_PLUGINS</c> value; <see langword="null"/> or empty means the variable is not set.
    /// </param>
    /// <param name="stderr">Writer that receives one line per refused source, at the moment of the refusal.</param>
    /// <returns>
    /// The loaded plugins with their accepted tools, and the refused sources with their reasons. The report is
    /// also registered in <paramref name="services"/>, which is where the tool surface reads it from.
    /// </returns>
    public static Report Run(
        IServiceCollection services,
        RoslynMcpFileSettings settings,
        string baseDirectory,
        string? environmentPaths,
        TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(stderr);

        var loaded = new List<LoadedPlugin>();
        var skipped = new List<SkippedPlugin>();

        var discovery = PluginDiscovery.Discover(baseDirectory, settings.Plugins, environmentPaths, HostVersion());
        RecordSkips(discovery.Skipped, stderr, skipped);

        var copies = PluginShadowCopy.Copy(discovery.Plans);
        RecordSkips(copies.Skipped, stderr, skipped);

        foreach (var outcome in PluginAssemblyLoad.Load(copies.Plans, services))
        {
            if (outcome.IsLoaded)
            {
                loaded.Add(Describe(outcome));
                continue;
            }

            // A refusal of Register or of a tool host has already reached stderr from the registrar as
            // `plugin '<id>' was not loaded`. The line of this pass is written for that skip as well, so the
            // report, the log and the reader of stderr all name the same source and the same reason.
            RecordSkip(outcome.Id, outcome.EntryPath, outcome.SkipReason, stderr, skipped);
        }

        var report = new Report
        {
            Loaded = [.. loaded],
            Skipped = [.. skipped],
        };

        // Tool host types are created from this provider on every agent call, so the report travels to the
        // tool surface as one of its services.
        services.AddSingleton(report);
        return report;
    }

    /// <summary>
    /// Writes the skip lines of <paramref name="report"/> to the host log.
    /// </summary>
    /// <param name="report">Report of the start pass of this process.</param>
    /// <param name="loggerFactory">Logger factory of the built host; the log file exists only after <c>Build</c>.</param>
    /// <remarks>
    /// One pass over the report, with no discovery and no assembly load: the strings are the ones stderr
    /// already received, so a log reader and a stderr reader see the same source name and the same reason.
    /// </remarks>
    public static void WriteSkipsToLog(Report report, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (report.Skipped.Count == 0)
        {
            return;
        }

        var logger = loggerFactory.CreateLogger(typeof(PluginStartup));
        foreach (var skip in report.Skipped)
        {
            logger.LogWarning("{PluginSkipLine}", FormatSkipLine(skip.Plugin, skip.Reason));
        }
    }

    /// <summary>Writes and records one line per refused source of one stage of the pass.</summary>
    private static void RecordSkips(
        IReadOnlyList<PluginDiscoverySkip> skips,
        TextWriter stderr,
        List<SkippedPlugin> skipped)
    {
        foreach (var skip in skips)
        {
            RecordSkip(skip.PluginId, skip.SourcePath, skip.Reason, stderr, skipped);
        }
    }

    /// <summary>Writes and records one refused source, named by its plugin id or by its path.</summary>
    private static void RecordSkip(
        string? pluginId,
        string sourcePath,
        string? reason,
        TextWriter stderr,
        List<SkippedPlugin> skipped)
    {
        // A source refused before its manifest was read has no id, and its path is what a reader can act on;
        // a skip of the loader always has one.
        var plugin = string.IsNullOrWhiteSpace(pluginId) ? sourcePath : pluginId;
        var skip = new SkippedPlugin { Plugin = plugin, Reason = reason ?? string.Empty };
        stderr.WriteLine(FormatSkipLine(skip.Plugin, skip.Reason));
        skipped.Add(skip);
    }

    /// <summary>The one skip line of this host: written to stderr at the skip and to the log after Build.</summary>
    private static string FormatSkipLine(string plugin, string reason) =>
        $"[RoslynMcp] plugin skipped ({plugin}): {reason}";

    /// <summary>Report entry of a loaded plan: the file that was opened and the tools it published.</summary>
    private static LoadedPlugin Describe(PluginLoadOutcome outcome) =>
        new()
        {
            Id = outcome.Id,
            EntryPath = outcome.EntryPath,
            ToolNames = outcome.ToolNames,
            Tools = DescribeTools(outcome),
        };

    /// <summary>The accepted tool methods of one loaded plan, in registration order.</summary>
    /// <remarks>
    /// The report carries the method rather than text taken from the JSON Schema the SDK builds for the tool,
    /// because the description and the parameters of a plugin tool are its attributes. Only names that reached
    /// the container are taken: a method whose name was refused during registration is not a tool of this
    /// plugin, and a rejected plugin keeps names in its registration report that must not appear here.
    /// </remarks>
    private static IReadOnlyList<Tool> DescribeTools(PluginLoadOutcome outcome)
    {
        var accepted = outcome.ToolNames.ToHashSet(StringComparer.Ordinal);
        var tools = new List<Tool>();

        foreach (var hostType in outcome.Registration.RegisteredTypes)
        {
            foreach (var method in hostType.GetMethods(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>()?.Name is not { Length: > 0 } name
                    || !accepted.Contains(name))
                {
                    continue;
                }

                tools.Add(new Tool { Name = name, Method = method });
            }
        }

        return [.. tools];
    }

    /// <summary>Product version of this host, the value a manifest compares with <c>minHostVersion</c>.</summary>
    private static Version HostVersion() =>
        typeof(PluginStartup).Assembly.GetName().Version
        ?? throw new InvalidOperationException("the host assembly reports no version");

    /// <summary>Report of one plugin start pass: what this process loaded and what it refused.</summary>
    public sealed record Report
    {
        /// <summary>Report of a pass that read no plugin source: no loaded plugin and no skip.</summary>
        public static Report Empty { get; } = new();

        /// <summary>Loaded plugins, in load order.</summary>
        public IReadOnlyList<LoadedPlugin> Loaded { get; init; } = [];

        /// <summary>Refused sources, in the order they were refused.</summary>
        public IReadOnlyList<SkippedPlugin> Skipped { get; init; } = [];

        /// <summary>True when this pass loaded no plugin and refused no source.</summary>
        public bool IsEmpty => Loaded.Count == 0 && Skipped.Count == 0;

        /// <summary>Published names of every loaded plugin, in load order.</summary>
        public IReadOnlyList<string> ToolNames => [.. Loaded.SelectMany(plugin => plugin.ToolNames)];

        /// <summary>Finds the accepted tool of a loaded plugin by the name it published.</summary>
        /// <param name="toolName">Name as it appears in <c>tools/list</c>.</param>
        /// <param name="tool">Accepted tool, or <see langword="null"/> when no loaded plugin published that name.</param>
        /// <returns><see langword="true"/> when a loaded plugin of this pass published the name.</returns>
        public bool TryFindTool(string toolName, [NotNullWhen(true)] out Tool? tool)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

            foreach (var plugin in Loaded)
            {
                foreach (var candidate in plugin.Tools)
                {
                    if (candidate.Name.Equals(toolName, StringComparison.Ordinal))
                    {
                        tool = candidate;
                        return true;
                    }
                }
            }

            tool = null;
            return false;
        }
    }

    /// <summary>One plugin of this pass that is in the container, with the tools it published.</summary>
    public sealed record LoadedPlugin
    {
        /// <summary>Plugin id from the manifest.</summary>
        public required string Id { get; init; }

        /// <summary>
        /// Entry file the loader opened: the copy of the plugin directory when the plan had one, otherwise the
        /// file the plugin was found in.
        /// </summary>
        public required string EntryPath { get; init; }

        /// <summary>Tool names the plugin published into the host collection, in registration order.</summary>
        public IReadOnlyList<string> ToolNames { get; init; } = [];

        /// <summary>Accepted tool methods of this plugin, which is where tool help reads its text from.</summary>
        public IReadOnlyList<Tool> Tools { get; init; } = [];
    }

    /// <summary>One source that produced no plugin, with the reason a reader can act on.</summary>
    public sealed record SkippedPlugin
    {
        /// <summary>Plugin id of the refused source, or its path when no manifest named an id.</summary>
        public required string Plugin { get; init; }

        /// <summary>Why this source produced no plugin.</summary>
        public required string Reason { get; init; }
    }

    /// <summary>One accepted tool of a loaded plugin: the published name and the method behind it.</summary>
    public sealed record Tool
    {
        /// <summary>Name as it appears in <c>tools/list</c>, as the plugin published it.</summary>
        public required string Name { get; init; }

        /// <summary>Method of the plugin tool host type that the SDK calls for that name.</summary>
        public required MethodInfo Method { get; init; }
    }
}
