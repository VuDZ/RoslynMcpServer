using System.ComponentModel;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Plugins;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests.Plugins;

/// <summary>
/// Assembly-load seam: a plan becomes a plugin type of its own load context, its <c>Register</c> writes into
/// the host service collection, and a plugin that cannot be loaded is skipped alone. The fixtures are copies
/// of the build output of <c>samples/RoslynMcpPlugin</c>; the sample assembly itself never enters the default
/// context of this process, only the load context of a plan.
/// </summary>
public sealed class PluginAssemblyLoadTests : IDisposable
{
    private const string SampleProjectName = "RoslynMcpPlugin";
    private const string SampleEntryFileName = SampleProjectName + ".dll";
    private const string SamplePluginId = "sample";
    private const string SamplePluginTypeName = "RoslynMcpPlugin.SamplePlugin";
    private const string SampleToolsTypeName = "RoslynMcpPlugin.SampleTools";
    private const string SampleToolPrefix = "sample_";
    private const string SampleToolName = "sample_loaded_workspace";
    private const string SampleToolMethodName = "LoadedWorkspace";
    private const string NoWorkspaceMessage = "No workspace is loaded.";
    private const string PrivateDependencyFileName = "Newtonsoft.Json.dll";
    private const string PrivateDependencyAssemblyName = "Newtonsoft.Json";
    private const string ObserverToolPrefix = "observe_";
    private const string ObserverToolName = "observe_document_text";
    private const string NoDocumentMessage = "no document";
    private const string OriginalSourceText = "namespace App; public sealed class Class1 { }";
    private const string ChangedSourceMarker = "ChangedByTheTest";
    private const string ChangedSourceText = "namespace App; public sealed class Class1 { public int " + ChangedSourceMarker + " { get; set; } }";
    private const string TargetFrameworkDirectoryName = "net10.0";
    private const string SolutionFileName = "RoslynMcpServer.sln";

    public PluginAssemblyLoadTests()
    {
        _copyRoot = Path.Combine(_root, "copies");
        _noDropInRoot = Path.Combine(_root, "host-without-drop-ins");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_copyRoot);
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
            // best-effort temp cleanup: a load context of this process holds the copy it was loaded from
        }
    }

    [Fact]
    public void Drop_in_plan_publishes_its_tool_and_reports_no_workspace_before_a_load()
    {
        var plan = DropInPlan("host-drop-in");
        Assert.False(plan.UsesShadowCopy);

        using var host = BuildHost([plan]);
        var outcome = Assert.Single(host.Outcomes);

        Assert.True(outcome.IsLoaded, outcome.SkipReason);
        Assert.Equal(SamplePluginId, outcome.Id);
        Assert.Equal(plan.EntryPath, outcome.EntryPath);
        Assert.Equal([SampleToolName], outcome.ToolNames);
        Assert.Null(outcome.SkipReason);

        // The collection is read after IOptions<McpServerOptions>.Value, which is the resolve that publishes
        // the registered McpServerTool services into it.
        var tools = ResolveRuntimeTools(host.Services);
        Assert.Contains(tools, tool => tool.ProtocolTool.Name == SampleToolName);
        Assert.Equal(McpToolCatalog.All.Count + 1, tools.Count);

        // Nothing was loaded on this host, so the tool answers for the SolutionManager the provider holds
        // instead of reporting a value of its own.
        Assert.Equal(NoWorkspaceMessage, InvokePluginTool(host.Services, outcome, SampleToolName));
    }

    [Fact]
    public void Framework_reference_the_plugin_does_not_carry_does_not_skip_the_plugin()
    {
        var plan = ShadowCopyPlan(CreateSampleCopy("sample-source"));
        var pluginDirectory = CopyDirectoryOf(plan.EntryPath);

        // The output of the plugin carries neither the contract assemblies nor the framework ones, so the
        // resolver of the plugin misses System.Runtime and System.ComponentModel.Primitives. The framework
        // stage serves them and the plugin still loads.
        Assert.False(File.Exists(Path.Combine(pluginDirectory, "System.Runtime.dll")));
        Assert.False(File.Exists(Path.Combine(pluginDirectory, "System.ComponentModel.Primitives.dll")));

        using var host = BuildHost([plan]);
        var outcome = Assert.Single(host.Outcomes);

        Assert.True(outcome.IsLoaded, outcome.SkipReason);

        // The tool body calls the BCL and the private dependency, so an answer proves both were resolved: the
        // first by the framework stage, the second from the plugin directory.
        Assert.Equal(NoWorkspaceMessage, InvokePluginTool(host.Services, outcome, SampleToolName));
    }

    [Fact]
    public void Plan_whose_id_differs_from_the_plugin_name_is_skipped()
    {
        // The same entry under a foreign id: the type of the sample reports the name 'sample', so Register is
        // never called for this plan.
        var plan = ShadowCopyPlan(CreateSampleCopy("sample-source")) with { Id = "foreign" };

        using var host = BuildHost([plan]);
        var reason = AssertSkipped(Assert.Single(host.Outcomes));

        Assert.Contains($"'{SamplePluginId}'", reason, StringComparison.Ordinal);
        Assert.Contains("'foreign'", reason, StringComparison.Ordinal);

        var tools = ResolveRuntimeTools(host.Services);
        Assert.DoesNotContain(tools, tool => tool.ProtocolTool.Name == SampleToolName);
        Assert.Equal(McpToolCatalog.All.Count, tools.Count);
    }

    [Fact]
    public void Plugin_type_that_is_not_in_the_entry_is_skipped()
    {
        var plan = ShadowCopyPlan(CreateSampleCopy("sample-source")) with { PluginType = "RoslynMcpPlugin.NotThere" };

        using var host = BuildHost([plan]);
        var outcome = Assert.Single(host.Outcomes);
        var reason = AssertSkipped(outcome);

        Assert.Contains("RoslynMcpPlugin.NotThere", reason, StringComparison.Ordinal);
        Assert.Contains("was not found", reason, StringComparison.Ordinal);
        Assert.Empty(outcome.ToolNames);
    }

    [Fact]
    public void Type_that_does_not_implement_the_plugin_interface_is_skipped()
    {
        // SampleTools exists in the entry and has a public constructor, so only the interface check can
        // produce this skip.
        var plan = ShadowCopyPlan(CreateSampleCopy("sample-source")) with { PluginType = SampleToolsTypeName };

        using var host = BuildHost([plan]);
        var reason = AssertSkipped(Assert.Single(host.Outcomes));

        Assert.Contains(SampleToolsTypeName, reason, StringComparison.Ordinal);
        Assert.Contains("does not implement", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Tool_host_the_container_cannot_build_rejects_the_plugin_and_frees_its_name()
    {
        var plan = ShadowCopyPlan(CreateSampleCopy("sample-source"));

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "full" });

        // The tool host of the sample takes SolutionManager, so a container without that service is a
        // container the host cannot build the tool host from: the plugin has to be refused as a whole instead
        // of publishing a tool that fails on the first agent call, and the reason names the method.
        builder.Services.RemoveAll<SolutionManager>();
        var baseline = builder.Services.Count;

        var outcome = Assert.Single(PluginAssemblyLoad.Load([plan], builder.Services));
        var reason = AssertSkipped(outcome);

        Assert.Contains(SampleToolMethodName, reason, StringComparison.Ordinal);
        Assert.Contains(SamplePluginId, reason, StringComparison.Ordinal);

        // Neither the tool of the plugin nor any other service it added stays in the container.
        Assert.Equal(baseline, builder.Services.Count);
        Assert.DoesNotContain(
            builder.Services,
            descriptor => descriptor.ServiceType.FullName?.StartsWith(SampleProjectName, StringComparison.Ordinal) == true);

        // The name the rejected plugin had taken is free again: the next plugin of this container takes it.
        var retried = RoslynMcpPluginRegistrar.Register(builder.Services, "retry", SampleToolPrefix, new FreeNamePlugin());
        Assert.Equal([SampleToolName], retried.RegisteredNames);
    }

    [Fact]
    public void Broken_plan_next_to_the_sample_does_not_stop_its_registration()
    {
        // A drop-in directory without its entry file: discovery reports where the plugin would load from and
        // does not check that the file is there, so this plan reaches the loader broken.
        var broken = DropInPlan("host-with-broken-drop-in", keepEntryFile: false);
        var sample = ShadowCopyPlan(CreateSampleCopy("sample-source"));

        using var host = BuildHost([broken, sample]);
        Assert.Equal(2, host.Outcomes.Count);

        var brokenReason = AssertSkipped(host.Outcomes[0]);
        Assert.Contains(broken.EntryPath, brokenReason, StringComparison.Ordinal);

        var sampleOutcome = host.Outcomes[1];
        Assert.True(sampleOutcome.IsLoaded, sampleOutcome.SkipReason);
        Assert.Equal([SampleToolName], sampleOutcome.ToolNames);
        Assert.Contains(ResolveRuntimeTools(host.Services), tool => tool.ProtocolTool.Name == SampleToolName);
    }

    [Fact]
    public void Missing_private_dependency_skips_the_plugin_instead_of_binding_the_host_copy()
    {
        // A same-named assembly is part of this test process: it arrives with the closure of the test
        // platform. Loading it here puts that fallback in the default context on purpose, so the skip below is
        // a statement about substitution and not about a host that holds no such assembly.
        var hostCopyPath = Path.Combine(AppContext.BaseDirectory, PrivateDependencyFileName);
        Assert.True(
            File.Exists(hostCopyPath),
            $"this fixture needs a host copy of '{PrivateDependencyFileName}' at '{hostCopyPath}'");

        var hostCopy = Assembly.LoadFrom(hostCopyPath);
        Assert.Equal(PrivateDependencyAssemblyName, hostCopy.GetName().Name);

        var sourceDirectory = CreateSampleCopy("missing-dependency");
        File.Delete(Path.Combine(sourceDirectory, PrivateDependencyFileName));
        var plan = ShadowCopyPlan(sourceDirectory);

        using var host = BuildHost([plan]);
        var reason = AssertSkipped(Assert.Single(host.Outcomes));

        Assert.Contains(PrivateDependencyAssemblyName, reason, StringComparison.Ordinal);

        var tools = ResolveRuntimeTools(host.Services);
        Assert.DoesNotContain(tools, tool => tool.ProtocolTool.Name == SampleToolName);
        Assert.Equal(McpToolCatalog.All.Count, tools.Count);
    }

    [Fact]
    public void Loaded_copy_survives_a_source_rewrite_and_the_next_load_reads_the_new_bytes()
    {
        var sourceDirectory = CreateSampleCopy("rewritten-source");
        var sourceEntryPath = Path.Combine(sourceDirectory, SampleEntryFileName);

        // Valid bytes of another managed assembly: they are what the next load has to read instead of the
        // sample, and they carry no plugin type, so that load ends as a skip rather than as a second sample.
        var replacementBytes = File.ReadAllBytes(Path.Combine(sourceDirectory, PrivateDependencyFileName));

        var firstPlan = ShadowCopyPlan(sourceDirectory);
        using var firstHost = BuildHost([firstPlan]);
        var firstOutcome = Assert.Single(firstHost.Outcomes);
        Assert.True(firstOutcome.IsLoaded, firstOutcome.SkipReason);

        var copyDirectory = CopyDirectoryOf(firstPlan.EntryPath);
        var entryAssembly = Assert.Single(firstOutcome.Registration.RegisteredTypes).Assembly;
        Assert.Equal(copyDirectory, Path.GetDirectoryName(entryAssembly.Location));
        Assert.NotEqual(sourceDirectory, Path.GetDirectoryName(entryAssembly.Location));

        // The private managed dependency is loaded from the copy of this plugin as well.
        var loadContext = Assert.IsAssignableFrom<AssemblyLoadContext>(
            AssemblyLoadContext.GetLoadContext(entryAssembly));
        var dependency = Assert.Single(
            loadContext.Assemblies,
            candidate => candidate.GetName().Name == PrivateDependencyAssemblyName);
        Assert.Equal(copyDirectory, Path.GetDirectoryName(dependency.Location));

        Assert.Equal(NoWorkspaceMessage, InvokePluginTool(firstHost.Services, firstOutcome, SampleToolName));

        // The build that produced the plugin replaces its file in the source directory while the process keeps
        // running. The loaded copy is what holds a file open, so the write succeeds and does not reach it.
        var copyBytes = File.ReadAllBytes(firstPlan.EntryPath);
        File.WriteAllBytes(sourceEntryPath, replacementBytes);
        Assert.Equal(replacementBytes, File.ReadAllBytes(sourceEntryPath));
        Assert.Equal(copyBytes, File.ReadAllBytes(firstPlan.EntryPath));

        // The loaded type of the first pass is alive and still answers after the source file changed.
        Assert.Equal(NoWorkspaceMessage, InvokePluginTool(firstHost.Services, firstOutcome, SampleToolName));

        // A second pass over the same source directory copies the new bytes and reads them: the plugin type of
        // the plan is not in that assembly, so the pass reports it instead of reusing the first copy.
        var secondPlan = ShadowCopyPlan(sourceDirectory);
        Assert.NotEqual(firstPlan.EntryPath, secondPlan.EntryPath);

        using var secondHost = BuildHost([secondPlan]);
        var secondOutcome = Assert.Single(secondHost.Outcomes);
        var secondReason = AssertSkipped(secondOutcome);

        Assert.Contains(SamplePluginTypeName, secondReason, StringComparison.Ordinal);
        Assert.Equal(replacementBytes, File.ReadAllBytes(secondOutcome.EntryPath));
    }

    [Fact]
    public async Task Plugin_and_test_tool_see_the_queued_disk_change_of_the_host_solution_manager()
    {
        MsBuildBootstrapper.Register();
        var workspace = CreateWorkspaceProject();
        var plan = ShadowCopyPlan(CreateSampleCopy("sample-source"));

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "full" });
        var loaded = Assert.Single(PluginAssemblyLoad.Load([plan], builder.Services));
        Assert.True(loaded.IsLoaded, loaded.SkipReason);

        // The tool of the sample reads the loaded workspace path only, so it never drains the queue of the disk
        // watcher. This test tool of the same container takes the sanitized entry, which does apply the queue,
        // and is what can observe the change.
        var observer = RoslynMcpPluginRegistrar.Register(
            builder.Services,
            "observer",
            ObserverToolPrefix,
            new ObserverPlugin(workspace.SourcePath));

        using var host = builder.Build();
        var tools = ResolveRuntimeTools(host.Services);
        Assert.Contains(tools, tool => tool.ProtocolTool.Name == SampleToolName);
        Assert.Contains(tools, tool => tool.ProtocolTool.Name == ObserverToolName);
        Assert.Equal([ObserverToolName], observer.RegisteredNames);

        var solutions = host.Services.GetRequiredService<SolutionManager>();
        await solutions.LoadAndPrepareAsync(workspace.ProjectPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);

        // The tool of the plugin reports the path of the manager the provider holds: a manager of its own would
        // report that no workspace is loaded.
        var loadedPath = solutions.GetLoadedWorkspacePath();
        Assert.False(string.IsNullOrEmpty(loadedPath), "the workspace of this test was not loaded");
        Assert.Equal(loadedPath, InvokePluginTool(host.Services, loaded, SampleToolName));

        // The document changes on disk and the watcher queues it. Nothing flushes that queue yet, and the tool
        // call in between must not: the next call that takes the sanitized entry still sees the new text.
        File.WriteAllText(workspace.SourcePath, ChangedSourceText);
        var delivered = await solutions.WaitForDirtySourceAsync(workspace.SourcePath, TimeSpan.FromSeconds(15));
        Assert.True(
            delivered.Delivered,
            $"the disk watcher did not queue '{workspace.SourcePath}' within {delivered.Elapsed}");

        Assert.Equal(loadedPath, InvokePluginTool(host.Services, loaded, SampleToolName));

        var documentText = await host.Services.GetRequiredService<ObserverTools>().DocumentText(CancellationToken.None);
        Assert.Contains(ChangedSourceMarker, documentText, StringComparison.Ordinal);
    }

    /// <summary>Builds a host whose collection the plugin pass filled, the way the host start does it.</summary>
    private static PluginHost BuildHost(IReadOnlyList<PluginLoadPlan> plans)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "full" });
        var outcomes = PluginAssemblyLoad.Load(plans, builder.Services);
        return new PluginHost(builder.Build(), outcomes);
    }

    /// <summary>
    /// Runtime tool collection of a built host. The SDK fills it from the registered <c>McpServerTool</c>
    /// services at the first <c>IOptions&lt;McpServerOptions&gt;.Value</c>, not at <c>Build</c>.
    /// </summary>
    private static McpRuntimeToolCollection ResolveRuntimeTools(IServiceProvider services)
    {
        _ = services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        return services.GetRequiredService<McpRuntimeToolCollection>();
    }

    /// <summary>Skip reason of an outcome that must not be loaded, or a failure naming the plugin.</summary>
    private static string AssertSkipped(PluginLoadOutcome outcome)
    {
        Assert.False(outcome.IsLoaded, $"plugin '{outcome.Id}' was expected to be skipped");
        return Assert.IsType<string>(outcome.SkipReason);
    }

    /// <summary>
    /// Invokes the tool method a loaded plugin published, on an instance of its tool host type resolved from
    /// the built provider: the SDK builds that same type from that same provider on every agent call, so this
    /// instance carries the dependencies a served call gets.
    /// </summary>
    private static string InvokePluginTool(IServiceProvider services, PluginLoadOutcome outcome, string toolName)
    {
        var toolType = Assert.Single(outcome.Registration.RegisteredTypes);
        var method = toolType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(candidate => candidate.GetCustomAttribute<McpServerToolAttribute>()?.Name == toolName);

        var instance = services.GetRequiredService(toolType);
        return Assert.IsType<string>(method.Invoke(instance, null));
    }

    /// <summary>One accepted plan, or a failure naming the source that produced none.</summary>
    private static PluginLoadPlan SinglePlan(PluginDiscoveryResult discovery)
    {
        Assert.Empty(discovery.Skipped);
        return Assert.Single(discovery.Plans);
    }

    /// <summary>Plan of a drop-in directory of this test, which is loaded where it lies.</summary>
    private PluginLoadPlan DropInPlan(string hostName, bool keepEntryFile = true)
    {
        var baseDirectory = Path.Combine(_root, hostName);
        var pluginDirectory = Path.Combine(baseDirectory, PluginDiscovery.DropInDirectoryName, SamplePluginId);
        CopyTree(SampleOutputDirectory(), pluginDirectory);
        if (!keepEntryFile)
        {
            File.Delete(Path.Combine(pluginDirectory, SampleEntryFileName));
        }

        return SinglePlan(PluginDiscovery.Discover(baseDirectory, [], environmentPaths: null, HostVersion()));
    }

    /// <summary>Plan of one explicit plugin directory, with the shadow copy the host makes for a dev path.</summary>
    private PluginLoadPlan ShadowCopyPlan(string sourceDirectory)
    {
        var discovery = PluginDiscovery.Discover(_noDropInRoot, [sourceDirectory], environmentPaths: null, HostVersion());
        var copy = PluginShadowCopy.Copy([SinglePlan(discovery)], _copyRoot);
        Assert.Empty(copy.Skipped);
        return Assert.Single(copy.Plans);
    }

    /// <summary>Copies the build output of the sample project into <c>{root}/{name}</c> and returns it.</summary>
    private string CreateSampleCopy(string name)
    {
        var sourceDirectory = Path.Combine(_root, name);
        CopyTree(SampleOutputDirectory(), sourceDirectory);
        return sourceDirectory;
    }

    /// <summary>The one project the disk-watcher test loads, with the document the observer tool reads.</summary>
    private TempProject CreateWorkspaceProject()
    {
        var directory = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(directory);

        var projectPath = Path.Combine(directory, "App.csproj");
        var sourcePath = Path.Combine(directory, "Class1.cs");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(sourcePath, OriginalSourceText);

        return new TempProject(projectPath, sourcePath);
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
    /// the sample project, so one build produced both; its output assembly stays out of this process.
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

    /// <summary>Product version of the host assembly of this process, the value the sample manifest states.</summary>
    private static Version HostVersion() =>
        typeof(McpToolCatalog).Assembly.GetName().Version
        ?? throw new InvalidOperationException("the host assembly of this process reports no version");

    private static string CopyDirectoryOf(string entryPath) => Path.GetDirectoryName(entryPath) ?? string.Empty;

    private sealed class PluginHost : IDisposable
    {
        public PluginHost(IHost host, IReadOnlyList<PluginLoadOutcome> outcomes)
        {
            _host = host;
            Outcomes = outcomes;
        }

        public IReadOnlyList<PluginLoadOutcome> Outcomes { get; }

        public IServiceProvider Services => _host.Services;

        public void Dispose() => _host.Dispose();

        private readonly IHost _host;
    }

    /// <summary>The project the disk-watcher test loads and the document it changes.</summary>
    private sealed record TempProject(string ProjectPath, string SourcePath);

    /// <summary>Document path the observer tool reads; a plugin registers its own services like this.</summary>
    private sealed record ObservedDocument(string Path);

    /// <summary>
    /// Tool of the container that takes the sanitized entry, which applies the queue of the disk watcher.
    /// </summary>
    private sealed class ObserverTools
    {
        public ObserverTools(ObservedDocument document, SolutionManager solutions)
        {
            _document = document;
            _solutions = solutions;
        }

        [McpServerTool(Name = ObserverToolName)]
        [Description("Returns the text of one document of the sanitized published solution.")]
        public async Task<string> DocumentText(CancellationToken cancellationToken)
        {
            var solution = await _solutions.GetSanitizedPublishedSolutionAsync(cancellationToken);
            var document = solution?.Projects
                .SelectMany(project => project.Documents)
                .FirstOrDefault(candidate => candidate.FilePath is not null
                                             && candidate.FilePath.Equals(_document.Path, _pathComparison));

            return document is null
                ? NoDocumentMessage
                : (await document.GetTextAsync(cancellationToken)).ToString();
        }

        private readonly ObservedDocument _document;
        private readonly SolutionManager _solutions;
    }

    private sealed class ObserverPlugin : IRoslynMcpPlugin
    {
        public ObserverPlugin(string documentPath)
        {
            _documentPath = documentPath;
        }

        public string Name => "observer";

        public void Register(RoslynMcpPluginContext context)
        {
            context.Services.AddSingleton(new ObservedDocument(_documentPath));
            context.AddToolsFrom<ObserverTools>();
        }

        private readonly string _documentPath;
    }

    private sealed class FreeNameTool
    {
        [McpServerTool(Name = SampleToolName)]
        public string LoadedWorkspace() => "free";
    }

    private sealed class FreeNamePlugin : IRoslynMcpPlugin
    {
        public string Name => "retry";

        public void Register(RoslynMcpPluginContext context) => context.AddToolsFrom<FreeNameTool>();
    }

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "PluginAssemblyLoadTests-" + Guid.NewGuid().ToString("N"));

    private readonly string _copyRoot;
    private readonly string _noDropInRoot;

    private static readonly StringComparison _pathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
