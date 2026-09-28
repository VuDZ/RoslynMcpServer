using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using RoslynMcpServer.Config;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Plugins;
using RoslynMcpServer.Services;
using RoslynMcpServer.Tools;
using Serilog;
using Xunit;

namespace RoslynMcpServer.Tests;

/// <summary>
/// Host start seam: one start method discovers, copies and loads plugins before <c>Build</c>, writes one
/// <c>plugin skipped</c> line per refused source to stderr at the moment of the skip and the same lines to the
/// host log after <c>Build</c>, and hands the report to <c>get_mcp_server_info</c> and <c>get_tool_help</c>.
/// </summary>
/// <remarks>
/// The plugin fixture is the build output of <c>samples/RoslynMcpPlugin</c> copied into a temporary host
/// directory, and the sample assembly is loaded from that copy by a load context of its own, never into the
/// default context of this process. The last test starts the host entry point as a real child process and
/// drives the stdio transport, so the plugin path is proven for the process an agent talks to and not only for
/// this test host.
/// </remarks>
public sealed class PluginStartupTests : IDisposable
{
    private const string SampleProjectName = "RoslynMcpPlugin";
    private const string SampleEntryFileName = SampleProjectName + ".dll";
    private const string SamplePluginId = "sample";
    private const string SampleToolName = "sample_loaded_workspace";
    private const string SampleToolDescription = "Returns the loaded workspace path, or says that none is loaded.";
    private const string NoWorkspaceMessage = "No workspace is loaded.";
    private const string PrivateDependencyFileName = "Newtonsoft.Json.dll";
    private const string HostAssemblyFileName = "RoslynMcpServer.dll";
    private const string BrokenPluginId = "broken";
    private const string BrokenToolPrefix = "broken_";
    private const string MissingPluginTypeName = "RoslynMcpPlugin.NotThere";
    private const string MinimumHostVersion = "1.0";
    private const string ProbePluginId = "probe";
    private const string ProbeToolName = "probe_read_document";
    private const string ProbeToolDescription = "Returns the text of one document.";
    private const string ProbeFilePathDescription = "Path of the document to read.";
    private const string ProbeMaxResultsDescription = "Maximum number of results to return.";
    private const string LogDirectoryName = "logs";
    private const string LogFileSearchPattern = "mcp-*.log";
    private const int LogTailLineCount = 2000;
    private const string TargetFrameworkDirectoryName = "net10.0";
    private const string SolutionFileName = "RoslynMcpServer.sln";
    private const string SessionPropertyValue = "plugin-startup-test";
    private const string ClientName = "plugin-startup-smoke";
    private const string ClientVersion = "1.0.0";
    private const string InitializedNotification = "notifications/initialized";

    /// <summary>
    /// Protocol revision this client asks for: the one the MCP SDK of this repository serves, so the server
    /// answers with the same value instead of negotiating a lower one.
    /// </summary>
    private const string ProtocolVersion = "2025-11-25";

    public PluginStartupTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup: a load context of this process holds the plugin copy it was loaded from
        }
    }

    [Fact]
    public void Drop_in_plugin_is_loaded_before_build_and_its_tool_joins_the_collection()
    {
        var baseDirectory = CreateHostDirectory("drop-in");
        var pluginDirectory = CopySamplePlugin(baseDirectory, SamplePluginId);

        // The fixture is the build output of the sample: entry assembly, dependency graph, manifest and the
        // private managed dependency, with no contract assembly of the host in the copy.
        Assert.True(
            File.Exists(Path.Combine(pluginDirectory, PrivateDependencyFileName)),
            $"the sample output carries no '{PrivateDependencyFileName}'");
        Assert.False(
            File.Exists(Path.Combine(pluginDirectory, HostAssemblyFileName)),
            $"'{HostAssemblyFileName}' must not travel with a plugin");

        using var host = StartHost(McpToolProfileOptions.FullProfile, baseDirectory, environmentPaths: null);

        // A loaded plugin writes nothing to stderr: the tool list is what get_mcp_server_info shows.
        Assert.Empty(host.Stderr);
        Assert.Empty(host.Report.Skipped);

        var loaded = Assert.Single(host.Report.Loaded);
        Assert.Equal(SamplePluginId, loaded.Id);
        Assert.Equal(Path.Combine(pluginDirectory, SampleEntryFileName), loaded.EntryPath);
        Assert.Equal([SampleToolName], loaded.ToolNames);
        Assert.Equal([SampleToolName], loaded.Tools.Select(tool => tool.Name).ToArray());

        // The collection is read after IOptions<McpServerOptions>.Value, the resolve that publishes the
        // registered McpServerTool services into it.
        var tools = ResolveRuntimeTools(host.Services);
        Assert.Contains(tools, tool => tool.ProtocolTool.Name == SampleToolName);
        Assert.All(
            McpToolCatalog.All,
            descriptor => Assert.Contains(tools, tool => tool.ProtocolTool.Name == descriptor.Name));
        Assert.Equal(McpToolCatalog.All.Count + 1, tools.Count);
    }

    [Fact]
    public async Task Info_of_the_start_report_names_the_plugin_its_entry_file_and_its_tool()
    {
        var baseDirectory = CreateHostDirectory("info");
        CopySamplePlugin(baseDirectory, SamplePluginId);

        using var host = StartHost(McpToolProfileOptions.FullProfile, baseDirectory, environmentPaths: null);
        var loaded = Assert.Single(host.Report.Loaded);

        var info = await ReadInfoAsync(host);

        // The entry file is the one that was opened: for a drop-in directory that is the file inside the
        // temporary directory of this test.
        Assert.Contains($"- **Plugin `{SamplePluginId}`:** `{loaded.EntryPath}`", info, StringComparison.Ordinal);
        Assert.StartsWith(baseDirectory, loaded.EntryPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            Path.Combine(PluginDiscovery.DropInDirectoryName, SamplePluginId),
            loaded.EntryPath,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(SampleToolName, info, StringComparison.Ordinal);

        // The count covers the plugin tool, because it is in the same collection as the built-in tools.
        Assert.Contains($"- **Registered MCP tools:** {McpToolCatalog.All.Count + 1}", info, StringComparison.Ordinal);
        Assert.DoesNotContain("- **Plugins:** (none)", info, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_without_a_plugins_directory_reports_none_and_still_builds()
    {
        var baseDirectory = CreateHostDirectory("no-drop-in");
        Assert.False(Directory.Exists(Path.Combine(baseDirectory, PluginDiscovery.DropInDirectoryName)));

        using var host = StartHost(McpToolProfileOptions.FullProfile, baseDirectory, environmentPaths: null);

        Assert.True(host.Report.IsEmpty);
        Assert.Empty(host.Stderr);

        var info = await ReadInfoAsync(host);

        Assert.Contains("- **Plugins:** (none)", info, StringComparison.Ordinal);
        Assert.Equal(McpToolCatalog.All.Count, ResolveRuntimeTools(host.Services).Count);
    }

    [Fact]
    public async Task Broken_neighbor_is_skipped_to_stderr_info_and_the_log_file()
    {
        var baseDirectory = CreateHostDirectory("broken-neighbor");
        CopySamplePlugin(baseDirectory, SamplePluginId);
        WriteBrokenManifest(CopySamplePlugin(baseDirectory, BrokenPluginId));

        using var host = StartHost(McpToolProfileOptions.FullProfile, baseDirectory, environmentPaths: null);

        // The skip reached the captured stderr at the moment it happened, before Build, and it names the
        // neighbor alone: the loaded plugin of the same pass writes no line.
        var skip = Assert.Single(host.Report.Skipped);
        Assert.Equal(BrokenPluginId, skip.Plugin);
        Assert.Contains($"plugin type '{MissingPluginTypeName}' was not found", skip.Reason, StringComparison.Ordinal);

        var skipLine = $"[RoslynMcp] plugin skipped ({BrokenPluginId}): {skip.Reason}";
        Assert.Equal(skipLine + Environment.NewLine, host.Stderr);

        // The tool of the loaded plugin is untouched by the broken neighbor, and the neighbor contributes no
        // tool name and no metadata of its own: its copy carries the same entry assembly.
        var loaded = Assert.Single(host.Report.Loaded);
        Assert.Equal(SamplePluginId, loaded.Id);
        Assert.Equal([SampleToolName], loaded.ToolNames);
        Assert.Equal([SampleToolName], host.Report.ToolNames);
        Assert.True(host.Report.TryFindTool(SampleToolName, out _));
        Assert.Contains(ResolveRuntimeTools(host.Services), tool => tool.ProtocolTool.Name == SampleToolName);

        var info = await ReadInfoAsync(host);
        Assert.Contains($"- **Plugin `{SamplePluginId}`:**", info, StringComparison.Ordinal);
        Assert.Contains($"- **Plugin skipped `{BrokenPluginId}`:**", info, StringComparison.Ordinal);
        Assert.Contains(MissingPluginTypeName, info, StringComparison.Ordinal);

        // The acceptance reads the log file of a successful Build, not only the captured stderr.
        var logPath = NewestLogFile(baseDirectory);
        Assert.Contains(skipLine, ReadLogFile(logPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_of_a_loaded_plugin_tool_comes_from_the_start_report()
    {
        var baseDirectory = CreateHostDirectory("help");
        CopySamplePlugin(baseDirectory, SamplePluginId);

        using var host = StartHost(McpToolProfileOptions.FullProfile, baseDirectory, environmentPaths: null);
        _ = ResolveRuntimeTools(host.Services);
        var help = host.Services.GetRequiredService<ToolHelpTools>();

        var markdown = await help.GetToolHelp(SampleToolName);

        Assert.Contains($"# `{SampleToolName}`", markdown, StringComparison.Ordinal);
        Assert.Contains("- Kind: plugin", markdown, StringComparison.Ordinal);
        Assert.Contains("- Group: `plugin`", markdown, StringComparison.Ordinal);
        Assert.Contains(SampleToolDescription, markdown, StringComparison.Ordinal);
        Assert.Contains("## Parameters", markdown, StringComparison.Ordinal);
        Assert.Contains("None.", markdown, StringComparison.Ordinal);

        // The tool is not a catalog entry, has no article of its own, and is no group line either.
        Assert.DoesNotContain(McpToolCatalog.All, descriptor => descriptor.Name == SampleToolName);
        Assert.DoesNotContain(SampleToolName, McpToolHelpCatalog.Entries.Keys);
        Assert.DoesNotContain(SampleToolName, await help.ListToolGroups(), StringComparison.Ordinal);

        // A near miss of the plugin tool name is suggested like a near miss of a catalog name.
        var unknown = await help.GetToolHelp(SampleToolName[..^1]);
        Assert.Contains($"Unknown tool `{SampleToolName[..^1]}`", unknown, StringComparison.Ordinal);
        Assert.Contains($"`{SampleToolName}`", unknown, StringComparison.Ordinal);
    }

    [Fact]
    public void Plugin_tool_help_renders_the_parameters_of_the_accepted_method()
    {
        // The tool of the sample takes no parameter, so the parameter lines of a plugin tool are read from a
        // report entry of this test: the formatter takes its text from the method of the entry and never from
        // the JSON Schema the SDK builds for the tool.
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = McpToolProfileOptions.FullProfile });
        using var host = builder.Build();

        var report = new PluginStartup.Report
        {
            Loaded =
            [
                new PluginStartup.LoadedPlugin
                {
                    Id = ProbePluginId,
                    EntryPath = Path.Combine(_root, "probe.dll"),
                    ToolNames = [ProbeToolName],
                    Tools = [new PluginStartup.Tool { Name = ProbeToolName, Method = ProbeMethod() }],
                },
            ],
        };

        var markdown = McpToolHelpFormatter.FormatToolHelp(
            ProbeToolName,
            host.Services.GetRequiredService<McpToolActivationService>(),
            report);

        Assert.Contains($"# `{ProbeToolName}`", markdown, StringComparison.Ordinal);
        Assert.Contains("- Kind: plugin", markdown, StringComparison.Ordinal);
        Assert.Contains(ProbeToolDescription, markdown, StringComparison.Ordinal);
        Assert.Contains($"`filePath` (string, required): {ProbeFilePathDescription}", markdown, StringComparison.Ordinal);
        Assert.Contains($"`maxResults` (integer, optional, default 25): {ProbeMaxResultsDescription}", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("None.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejected_plugin_keeps_its_names_out_of_the_report_and_out_of_help()
    {
        var baseDirectory = CreateHostDirectory("rejected");
        CopySamplePlugin(baseDirectory, SamplePluginId);

        // The tool host of the sample takes SolutionManager. A container without that service cannot build the
        // tool host, so Register is refused after the name was published: the registration report still lists
        // the name while the container holds neither the tool nor the service.
        using var host = StartHost(
            McpToolProfileOptions.FullProfile,
            baseDirectory,
            environmentPaths: null,
            services => services.RemoveAll<SolutionManager>());

        var skip = Assert.Single(host.Report.Skipped);
        Assert.Equal(SamplePluginId, skip.Plugin);
        Assert.Contains($"plugin '{SamplePluginId}' was not loaded", skip.Reason, StringComparison.Ordinal);

        // The line of the start pass exists even though the registrar already wrote its own line, and the log
        // after Build receives this line rather than that one.
        Assert.Equal($"[RoslynMcp] plugin skipped ({SamplePluginId}): {skip.Reason}{Environment.NewLine}", host.Stderr);

        Assert.Empty(host.Report.Loaded);
        Assert.Empty(host.Report.ToolNames);
        Assert.False(host.Report.TryFindTool(SampleToolName, out _));

        var tools = ResolveRuntimeTools(host.Services);
        Assert.DoesNotContain(tools, tool => tool.ProtocolTool.Name == SampleToolName);
        Assert.Equal(McpToolCatalog.All.Count, tools.Count);

        var help = McpToolHelpFormatter.FormatToolHelp(
            SampleToolName,
            host.Services.GetRequiredService<McpToolActivationService>(),
            host.Report);

        Assert.Contains($"Unknown tool `{SampleToolName}`", help, StringComparison.Ordinal);
        Assert.DoesNotContain(SampleToolDescription, help, StringComparison.Ordinal);
        Assert.DoesNotContain("## Parameters", help, StringComparison.Ordinal);
    }

    [Fact]
    public void Lite_profile_built_by_the_same_start_keeps_the_plugin_tool()
    {
        var baseDirectory = CreateHostDirectory("lite");
        CopySamplePlugin(baseDirectory, SamplePluginId);

        using var host = StartHost(McpToolProfileOptions.LiteProfile, baseDirectory, environmentPaths: null);

        var tools = ResolveRuntimeTools(host.Services);
        Assert.Contains(tools, tool => tool.ProtocolTool.Name == SampleToolName);
        Assert.Equal(McpToolCatalog.All.Count(descriptor => descriptor.InLiteCore) + 1, tools.Count);
        Assert.DoesNotContain(tools, tool => tool.ProtocolTool.Name == "search_code");
    }

    [Fact]
    public async Task Development_path_is_loaded_from_its_copy_and_info_names_that_copy()
    {
        var baseDirectory = CreateHostDirectory("dev-path");
        var sourceDirectory = CreateSampleCopy("sample-dev-source");

        using var host = StartHost(McpToolProfileOptions.FullProfile, baseDirectory, environmentPaths: sourceDirectory);

        var loaded = Assert.Single(host.Report.Loaded);
        Assert.Equal(SampleEntryFileName, Path.GetFileName(loaded.EntryPath));
        Assert.NotEqual(sourceDirectory, Path.GetDirectoryName(loaded.EntryPath));
        Assert.True(File.Exists(loaded.EntryPath), $"the loaded copy '{loaded.EntryPath}' does not exist");

        var info = await ReadInfoAsync(host);
        Assert.Contains($"- **Plugin `{SamplePluginId}`:** `{loaded.EntryPath}`", info, StringComparison.Ordinal);
        Assert.Contains(ResolveRuntimeTools(host.Services), tool => tool.ProtocolTool.Name == SampleToolName);
    }

    /// <summary>
    /// The host entry point as a real process: the sample plugin reaches it through <c>ROSLYN_MCP_PLUGINS</c>
    /// only, its tool is listed by the first <c>tools/list</c> and answers <c>tools/call</c>, and stdout holds
    /// nothing but protocol frames.
    /// </summary>
    [Fact]
    public async Task Host_process_serves_the_sample_plugin_tool_over_stdio()
    {
        var baseDirectory = CreateHostDirectory("smoke");
        var workingDirectory = Path.Combine(baseDirectory, "cwd");
        Directory.CreateDirectory(workingDirectory);

        using var client = HostProcessClient.Start(HostEntryPath(), workingDirectory, SampleOutputDirectory());

        var initialize = await client.RequestAsync(1, "initialize", InitializeParameters());
        Assert.Equal(ProtocolVersion, initialize.GetProperty("protocolVersion").GetString());

        await client.NotifyAsync(InitializedNotification);

        var list = await client.RequestAsync(2, "tools/list", parameters: null);
        var names = list
            .GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains(SampleToolName, names);

        var call = await client.RequestAsync(3, "tools/call", new { name = SampleToolName, arguments = new { } });
        Assert.False(
            call.TryGetProperty("isError", out var isError) && isError.GetBoolean(),
            "the plugin tool call was reported as an error");

        var content = call.GetProperty("content").EnumerateArray().ToArray();
        Assert.Equal(NoWorkspaceMessage, Assert.Single(content).GetProperty("text").GetString());

        // The transport carries protocol frames only: every line stdout delivered is one JSON-RPC message.
        Assert.Empty(client.NonProtocolStdoutLines);
    }

    /// <summary>
    /// Runs the host start of one temporary base directory the way <c>Program.cs</c> does: the log file of that
    /// directory, the plugin pass after the tools are registered and before <c>Build</c>, and the skip lines
    /// into the log right after <c>Build</c>.
    /// </summary>
    private static StartedHost StartHost(
        string profile,
        string baseDirectory,
        string? environmentPaths,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog((_, configuration) => configuration
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .Enrich.WithProperty(HarnessSessionId.PropertyName, SessionPropertyValue)
            .WriteTo.File(
                Path.Combine(baseDirectory, LogDirectoryName, "mcp-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: HarnessSessionId.LogOutputTemplate));

        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = profile });
        configureServices?.Invoke(builder.Services);

        var stderr = new StringWriter();
        var report = PluginStartup.Run(builder.Services, RoslynMcpFileSettings.Empty, baseDirectory, environmentPaths, stderr);

        var host = builder.Build();
        PluginStartup.WriteSkipsToLog(report, host.Services.GetRequiredService<ILoggerFactory>());
        return new StartedHost(host, report, stderr.ToString());
    }

    /// <summary>
    /// Runtime tool collection of a built host, after the resolve that fills it from the registered
    /// <c>McpServerTool</c> services. The SDK fills it at that resolve, not at <c>Build</c>.
    /// </summary>
    private static McpRuntimeToolCollection ResolveRuntimeTools(IServiceProvider services)
    {
        _ = services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        return services.GetRequiredService<McpRuntimeToolCollection>();
    }

    /// <summary>Info text of one started host, read through the tool the agent calls.</summary>
    private static async Task<string> ReadInfoAsync(StartedHost host)
    {
        _ = ResolveRuntimeTools(host.Services);
        return await host.Services.GetRequiredService<ServerLifecycleTools>().GetMcpServerInfo();
    }

    /// <summary>Newest <c>logs/mcp-*.log</c> of one host base directory, or a failure naming the directory.</summary>
    private static string NewestLogFile(string baseDirectory)
    {
        var logDirectory = Path.Combine(baseDirectory, LogDirectoryName);
        Assert.True(Directory.Exists(logDirectory), $"the host wrote no log directory '{logDirectory}'");

        var logPath = Directory
            .EnumerateFiles(logDirectory, LogFileSearchPattern)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        Assert.True(logPath is not null, $"no '{LogFileSearchPattern}' file was written under '{logDirectory}'");
        return logPath!;
    }

    /// <summary>
    /// Text of a log file the observed host wrote. The sink of that host holds the file open while the host
    /// still runs, and it opens it for writing rather than for sharing, so the read goes through the shared
    /// stream of <see cref="LogTailReader"/> instead of a plain read that would be denied.
    /// </summary>
    private static string ReadLogFile(string logPath) =>
        LogTailReader.ReadTail(logPath, LogTailLineCount, filterKeyword: null, CancellationToken.None);

    /// <summary>Initialize parameters of this client: no capability, because it only lists and calls.</summary>
    private static object InitializeParameters() =>
        new
        {
            protocolVersion = ProtocolVersion,
            capabilities = new { },
            clientInfo = new { name = ClientName, version = ClientVersion },
        };

    /// <summary>
    /// Entry assembly of the host next to this test assembly. The test project references the host project, so
    /// the build of this run delivered the runtime configuration and the dependency graph of the host there as
    /// well.
    /// </summary>
    private static string HostEntryPath() => Path.Combine(AppContext.BaseDirectory, HostAssemblyFileName);

    /// <summary>Method the probe tool of this test publishes, which is what a report entry carries.</summary>
    private static MethodInfo ProbeMethod() =>
        typeof(ProbeTools).GetMethod(nameof(ProbeTools.ReadDocument))
        ?? throw new InvalidOperationException($"'{nameof(ProbeTools.ReadDocument)}' was not found on the probe tool type");

    /// <summary>Copies the build output of the sample into the drop-in directory of a host directory.</summary>
    private static string CopySamplePlugin(string baseDirectory, string pluginId)
    {
        var pluginDirectory = Path.Combine(baseDirectory, PluginDiscovery.DropInDirectoryName, pluginId);
        CopyTree(SampleOutputDirectory(), pluginDirectory);
        return pluginDirectory;
    }

    /// <summary>
    /// Replaces the manifest of a copied sample so the entry is found but names a plugin type it does not
    /// have: discovery accepts this directory and the loader refuses it before Register runs.
    /// </summary>
    private static void WriteBrokenManifest(string pluginDirectory)
    {
        File.WriteAllText(
            Path.Combine(pluginDirectory, PluginManifest.FileName),
            $$"""
            {
              "id": "{{BrokenPluginId}}",
              "entry": "{{SampleEntryFileName}}",
              "pluginType": "{{MissingPluginTypeName}}",
              "toolPrefix": "{{BrokenToolPrefix}}",
              "minHostVersion": "{{MinimumHostVersion}}"
            }
            """);
    }

    private static void CopyTree(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(sourceDirectory))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
            if (Directory.Exists(sourcePath))
            {
                CopyTree(sourcePath, destinationPath);
                continue;
            }

            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    /// <summary>
    /// Build output of the sample project for the configuration of this test run. The test project references
    /// the sample project, so one build produced both; the sample assembly itself stays out of this process.
    /// </summary>
    private static string SampleOutputDirectory()
    {
        var outputDirectory = Path.Combine(
            RepositoryRoot(),
            "samples",
            SampleProjectName,
            "bin",
            BuildConfigurationName(),
            TargetFrameworkDirectoryName);

        Assert.True(Directory.Exists(outputDirectory), $"the sample plugin output '{outputDirectory}' does not exist");
        return outputDirectory;
    }

    /// <summary>Configuration of this run: the directory named directly under the <c>bin</c> of the repository.</summary>
    private static string BuildConfigurationName()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.Parent?.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) == true)
            {
                return directory.Name;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"no 'bin' directory was found above '{AppContext.BaseDirectory}'");
    }

    /// <summary>Repository root: the nearest directory above the test output holding the solution file.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"'{SolutionFileName}' was not found above '{AppContext.BaseDirectory}'");
    }

    /// <summary>A directory of this test under the temporary root, created before it is returned.</summary>
    private string CreateHostDirectory(string name)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>One copy of the sample output under the temporary root of this test.</summary>
    private string CreateSampleCopy(string name)
    {
        var directory = Path.Combine(_root, name);
        CopyTree(SampleOutputDirectory(), directory);
        return directory;
    }

    /// <summary>One started host of this test: the report of its start pass and what that pass wrote to stderr.</summary>
    private sealed class StartedHost : IDisposable
    {
        public StartedHost(IHost host, PluginStartup.Report report, string stderr)
        {
            _host = host;
            Report = report;
            Stderr = stderr;
        }

        public PluginStartup.Report Report { get; }

        public string Stderr { get; }

        public IServiceProvider Services => _host.Services;

        public void Dispose() => _host.Dispose();

        private readonly IHost _host;
    }

    /// <summary>
    /// Child process of the host entry point, driven over the stdio transport: one JSON-RPC message per line
    /// on stdin, one per line on stdout. Every stdout line is kept, so the caller can assert that the process
    /// writes nothing else there.
    /// </summary>
    private sealed class HostProcessClient : IDisposable
    {
        private HostProcessClient(Process process)
        {
            _process = process;
            _process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    lock (_stderr)
                    {
                        _stderr.AppendLine(args.Data);
                    }
                }
            };
            _process.BeginErrorReadLine();
        }

        /// <summary>stdout lines that are not one JSON-RPC message of this protocol.</summary>
        public IReadOnlyList<string> NonProtocolStdoutLines
        {
            get
            {
                lock (_nonProtocolLines)
                {
                    return [.. _nonProtocolLines];
                }
            }
        }

        public string StderrSnapshot
        {
            get
            {
                lock (_stderr)
                {
                    return _stderr.ToString();
                }
            }
        }

        /// <summary>
        /// Starts the host entry point with the plugin sources of <paramref name="pluginPaths"/>, which is the
        /// only ROSLYN_MCP_* value of that child environment.
        /// </summary>
        public static HostProcessClient Start(string hostEntryPath, string workingDirectory, string pluginPaths)
        {
            var runtimeConfigPath = Path.ChangeExtension(hostEntryPath, ".runtimeconfig.json");
            if (!File.Exists(hostEntryPath) || !File.Exists(runtimeConfigPath))
            {
                throw new FileNotFoundException(
                    $"the host entry '{hostEntryPath}' was not built next to this test assembly; build the test project so its host reference is built too.",
                    hostEntryPath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = DotNetHostResolver.ResolveDotNetExecutable(),
                Arguments = "exec \"" + hostEntryPath + "\"",
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            };

            // The child environment inherits the variables of this test run, and a profile or a workspace root
            // of the machine running the tests would change what the process reads. Only the plugin variable
            // of this test is left in place.
            foreach (var name in startInfo.Environment.Keys
                         .Where(name => name.StartsWith("ROSLYN_MCP_", StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                startInfo.Environment.Remove(name);
            }

            startInfo.Environment[PluginDiscovery.EnvironmentVariableName] = pluginPaths;

            var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException($"the host process '{hostEntryPath}' could not be started");
            }

            return new HostProcessClient(process);
        }

        /// <summary>Sends one request and returns its result; a notification of the same stream is skipped.</summary>
        public async Task<JsonElement> RequestAsync(int id, string method, object? parameters)
        {
            await WriteAsync(id, method, parameters).ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(ResponseTimeout);
            while (true)
            {
                var line = await ReadLineAsync(method, timeout.Token).ConfigureAwait(false);
                var message = ParseProtocolLine(line);
                if (message is null)
                {
                    continue;
                }

                using (message)
                {
                    if (!message.RootElement.TryGetProperty("id", out var idElement)
                        || !idElement.TryGetInt32(out var responseId)
                        || responseId != id)
                    {
                        continue;
                    }

                    if (message.RootElement.TryGetProperty("error", out var error))
                    {
                        throw new InvalidOperationException(
                            $"the host refused '{method}' with {error.GetRawText()}; stderr: {StderrSnapshot}");
                    }

                    return message.RootElement.GetProperty("result").Clone();
                }
            }
        }

        /// <summary>Sends one notification, which has no answer.</summary>
        public Task NotifyAsync(string method) => WriteAsync(id: null, method, parameters: null);

        public void Dispose()
        {
            KillProcessTree();
            _process.Dispose();
        }

        private async Task WriteAsync(int? id, string method, object? parameters)
        {
            var message = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
            };

            if (id is not null)
            {
                message["id"] = id.Value;
            }

            if (parameters is not null)
            {
                message["params"] = parameters;
            }

            await _process.StandardInput
                .WriteLineAsync(JsonSerializer.Serialize(message).AsMemory())
                .ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);
        }

        private async Task<string> ReadLineAsync(string method, CancellationToken cancellationToken)
        {
            string? line;
            try
            {
                line = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillProcessTree();
                throw new TimeoutException(
                    $"the host did not answer '{method}' within {ResponseTimeout.TotalSeconds:F0} s; stderr: {StderrSnapshot}");
            }

            if (line is null)
            {
                KillProcessTree();
                throw new InvalidOperationException(
                    $"the host closed stdout before it answered '{method}'; stderr: {StderrSnapshot}");
            }

            return line;
        }

        /// <summary>
        /// One stdout line as a message of this protocol, or <see langword="null"/> for a line that is not one.
        /// Such a line is recorded: writing anything else to stdout is a defect of the host, not of this client.
        /// </summary>
        private JsonDocument? ParseProtocolLine(string line)
        {
            if (line.Length == 0)
            {
                return null;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                RecordNonProtocolLine(line);
                return null;
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("jsonrpc", out var version)
                || version.ValueKind != JsonValueKind.String
                || !version.GetString()!.Equals("2.0", StringComparison.Ordinal))
            {
                document.Dispose();
                RecordNonProtocolLine(line);
                return null;
            }

            return document;
        }

        private void RecordNonProtocolLine(string line)
        {
            lock (_nonProtocolLines)
            {
                _nonProtocolLines.Add(line);
            }
        }

        private void KillProcessTree()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit((int)KillWaitTimeout.TotalMilliseconds);
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited between the check and the kill.
            }
        }

        private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(60);

        private static readonly TimeSpan KillWaitTimeout = TimeSpan.FromSeconds(15);

        private readonly Process _process;
        private readonly StringBuilder _stderr = new();
        private readonly List<string> _nonProtocolLines = [];
    }

    /// <summary>Tool host type of this test used as a report entry: one tool with two parameters.</summary>
    private sealed class ProbeTools
    {
        [McpServerTool(Name = ProbeToolName, Title = "Probe tool")]
        [Description(ProbeToolDescription)]
        public string ReadDocument(
            [Description(ProbeFilePathDescription)] string filePath,
            [Description(ProbeMaxResultsDescription)] int maxResults = 25) => filePath + maxResults;
    }

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "PluginStartupTests-" + Guid.NewGuid().ToString("N"));
}
