using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Plugins;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

/// <summary>
/// Plugin registration seam: a plugin already created in this process contributes tool names to the
/// same service collection and the same SDK tool collection as the built-in tools, without entering
/// the built-in catalog. The plugin and tool classes of these tests live in the test assembly;
/// <c>DiscoverAttributedTools</c> scans only the host assembly, so the catalog cannot drift because of
/// them.
/// </summary>
public sealed class PluginRegistrationTests
{
    private const string Prefix = "sample_";

    [Fact]
    public void Plugin_tool_joins_the_runtime_collection_and_the_catalog_does_not_grow()
    {
        using var host = BuildHost(new SingleToolPlugin());
        var tools = ResolveRuntimeTools(host);
        var sourceNames = McpToolCatalog.All.Select(descriptor => descriptor.Name).Distinct(StringComparer.Ordinal).ToArray();

        Assert.Equal(["sample_loaded_workspace"], host.Registration.RegisteredNames);
        Assert.Contains(tools, tool => tool.ProtocolTool.Name == "sample_loaded_workspace");
        Assert.Equal(sourceNames.Length + 1, tools.Count);
        Assert.Equal(sourceNames.Length, McpToolCatalog.All.Count);
        Assert.DoesNotContain(McpToolCatalog.All, descriptor => descriptor.Name == "sample_loaded_workspace");
        Assert.DoesNotContain(McpToolCatalog.HostTypes, type => type == typeof(SampleWorkspaceTool));
    }

    [Fact]
    public void Tool_name_without_the_plugin_prefix_never_reaches_the_collection()
    {
        using var host = BuildHost(new NoPrefixPlugin());
        var tools = ResolveRuntimeTools(host);

        Assert.Empty(host.Registration.RegisteredNames);
        Assert.DoesNotContain(tools, tool => tool.ProtocolTool.Name == "loaded_workspace");
        Assert.Equal(McpToolCatalog.All.Count, tools.Count);

        var skip = Assert.Single(host.Registration.Skipped);
        Assert.Equal("loaded_workspace", skip.ToolName);
        Assert.Equal(nameof(NoPrefixTool.LoadedWorkspace), skip.MethodName);
        Assert.Contains(Prefix, skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Remaining_names_of_the_same_call_register_when_one_name_is_skipped()
    {
        using var host = BuildHost(new MixedNamesPlugin());
        var tools = ResolveRuntimeTools(host);

        Assert.Equal(["sample_loaded_workspace"], host.Registration.RegisteredNames);
        Assert.Contains(tools, tool => tool.ProtocolTool.Name == "sample_loaded_workspace");
        Assert.DoesNotContain(tools, tool => tool.ProtocolTool.Name == "loaded_workspace");
        Assert.Equal(McpToolCatalog.All.Count + 1, tools.Count);

        var skip = Assert.Single(host.Registration.Skipped);
        Assert.Equal("loaded_workspace", skip.ToolName);
        Assert.Equal(nameof(MixedNamesTool.LoadedWorkspace), skip.MethodName);
    }

    [Fact]
    public void Tool_without_the_plugin_prefix_is_skipped_even_when_it_collides_with_the_catalog()
    {
        using var host = BuildHost(new CollisionPlugin());
        var tools = ResolveRuntimeTools(host);

        Assert.Empty(host.Registration.RegisteredNames);
        Assert.Single(tools, tool => tool.ProtocolTool.Name == "find_usages");
        Assert.Equal(McpToolCatalog.All.Count, tools.Count);

        var skip = Assert.Single(host.Registration.Skipped);
        Assert.Equal("find_usages", skip.ToolName);
        Assert.Contains(Prefix, skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_name_is_refused_even_when_the_plugin_prefix_allows_it()
    {
        // A manifest prefix cannot normally spell a built-in name, so this plugin carries the prefix
        // that matches its one tool. The name rule that fires is the catalog, not the prefix.
        using var host = BuildHost(new PrefixAllowsCollisionPlugin(), "find_");
        var tools = ResolveRuntimeTools(host);

        Assert.Empty(host.Registration.RegisteredNames);
        Assert.Single(tools, tool => tool.ProtocolTool.Name == "find_usages");
        Assert.Equal(McpToolCatalog.All.Count, tools.Count);

        var skip = Assert.Single(host.Registration.Skipped);
        Assert.Equal("find_usages", skip.ToolName);
        Assert.Equal(nameof(CatalogCollisionTool.FindUsages), skip.MethodName);
        Assert.Contains("catalog", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Second_plugin_with_the_same_tool_name_adds_nothing_to_the_same_container()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "full" });
        var first = RoslynMcpPluginRegistrar.Register(builder.Services, "first", Prefix, new SingleToolPlugin());
        var second = RoslynMcpPluginRegistrar.Register(builder.Services, "second", Prefix, new SingleToolPlugin());

        using var host = builder.Build();
        var tools = ResolveRuntimeTools(host);

        Assert.Equal(["sample_loaded_workspace"], first.RegisteredNames);
        Assert.Empty(second.RegisteredNames);
        Assert.Single(tools, tool => tool.ProtocolTool.Name == "sample_loaded_workspace");
        Assert.Equal(McpToolCatalog.All.Count + 1, tools.Count);

        var skip = Assert.Single(second.Skipped);
        Assert.Equal("sample_loaded_workspace", skip.ToolName);
        Assert.Contains("'first'", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Second_call_of_one_plugin_for_the_same_name_adds_nothing()
    {
        using var host = BuildHost(new RepeatedToolPlugin());
        var tools = ResolveRuntimeTools(host);

        Assert.Equal(["sample_loaded_workspace"], host.Registration.RegisteredNames);
        Assert.Single(tools, tool => tool.ProtocolTool.Name == "sample_loaded_workspace");
        Assert.Equal(McpToolCatalog.All.Count + 1, tools.Count);

        var skip = Assert.Single(host.Registration.Skipped);
        Assert.Equal("sample_loaded_workspace", skip.ToolName);
        Assert.Contains("already registered", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_tool_name_registers_again_in_a_second_host_of_the_process()
    {
        using var firstHost = BuildHost(new SingleToolPlugin());
        using var secondHost = BuildHost(new SingleToolPlugin());

        Assert.Equal(["sample_loaded_workspace"], firstHost.Registration.RegisteredNames);
        Assert.Equal(["sample_loaded_workspace"], secondHost.Registration.RegisteredNames);
        Assert.Contains(
            ResolveRuntimeTools(firstHost),
            tool => tool.ProtocolTool.Name == "sample_loaded_workspace");
        Assert.Contains(
            ResolveRuntimeTools(secondHost),
            tool => tool.ProtocolTool.Name == "sample_loaded_workspace");
    }

    [Fact]
    public void Failing_tool_host_leaves_no_tools_and_frees_the_name_in_that_container()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "full" });
        var failed = RoslynMcpPluginRegistrar.Register(builder.Services, "broken", Prefix, new BrokenToolPlugin());
        var retried = RoslynMcpPluginRegistrar.Register(builder.Services, "retry", Prefix, new SingleToolPlugin());

        using var host = builder.Build();
        var tools = ResolveRuntimeTools(host);

        Assert.Empty(failed.RegisteredNames);
        var failure = Assert.Single(failed.Failures);
        Assert.Contains(nameof(BrokenTool.Unroutable), failure, StringComparison.Ordinal);

        Assert.Equal(["sample_loaded_workspace"], retried.RegisteredNames);
        Assert.Single(tools, tool => tool.ProtocolTool.Name == "sample_loaded_workspace");
        Assert.Equal(McpToolCatalog.All.Count + 1, tools.Count);
    }

    [Fact]
    public void Failed_plugin_leaves_no_tool_of_its_earlier_successful_call()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "full" });
        var before = builder.Services.Count;
        var failed = RoslynMcpPluginRegistrar.Register(
            builder.Services,
            "partial",
            Prefix,
            new PartialPlugin());

        // The report keeps what the plugin published before it failed, but the container must not: the
        // rollback cuts off every descriptor the plugin added and frees the names it had taken.
        Assert.Equal(["sample_loaded_workspace"], failed.RegisteredNames);
        Assert.Contains(nameof(UnroutableTool.Unroutable), Assert.Single(failed.Failures), StringComparison.Ordinal);
        Assert.Equal(before, builder.Services.Count);
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(SampleWorkspaceTool));
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(SamplePluginServices));
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(UnroutableTool));

        // The plugin published one tool before it failed, and that name must be free again.
        var retried = RoslynMcpPluginRegistrar.Register(builder.Services, "retry", Prefix, new SingleToolPlugin());
        using var host = builder.Build();
        var tools = ResolveRuntimeTools(host);

        Assert.Equal(["sample_loaded_workspace"], retried.RegisteredNames);
        Assert.Single(tools, tool => tool.ProtocolTool.Name == "sample_loaded_workspace");
        Assert.Equal(McpToolCatalog.All.Count + 1, tools.Count);
    }

    [Fact]
    public void Throwing_Register_leaves_no_tools_and_frees_the_name()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "full" });
        var before = builder.Services.Count;
        var failed = RoslynMcpPluginRegistrar.Register(
            builder.Services,
            "throwing",
            Prefix,
            new ThrowingRegisterPlugin());
        var after = builder.Services.Count;

        // The report keeps what the plugin published before Register threw; the container keeps nothing,
        // including the singleton the plugin added for itself.
        Assert.Equal(["sample_loaded_workspace"], failed.RegisteredNames);
        Assert.Equal(before, after);
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(SamplePluginServices));
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(SampleWorkspaceTool));
        var failure = Assert.Single(failed.Failures);
        Assert.Contains("plugin 'throwing' was not loaded", failure, StringComparison.Ordinal);
        Assert.Contains("deliberate", failure, StringComparison.Ordinal);
        Assert.Contains("released tool names: sample_loaded_workspace", failure, StringComparison.Ordinal);

        var retried = RoslynMcpPluginRegistrar.Register(builder.Services, "retry", Prefix, new SingleToolPlugin());
        using var host = builder.Build();
        var tools = ResolveRuntimeTools(host);

        Assert.Equal(["sample_loaded_workspace"], retried.RegisteredNames);
        Assert.Single(tools, tool => tool.ProtocolTool.Name == "sample_loaded_workspace");
        Assert.Equal(McpToolCatalog.All.Count + 1, tools.Count);
    }

    [Fact]
    public void Plugin_singleton_and_SolutionManager_inside_the_tool_are_the_provider_instances()
    {
        using var host = BuildHost(new SingleToolPlugin());
        var tools = ResolveRuntimeTools(host);

        var pluginServices = host.Services.GetRequiredService<SamplePluginServices>();
        var solutions = host.Services.GetRequiredService<SolutionManager>();
        Assert.Single(tools, candidate => candidate.ProtocolTool.Name == "sample_loaded_workspace");

        var tool = host.Services.GetRequiredService<SampleWorkspaceTool>();
        Assert.Same(pluginServices, tool.PluginServices);
        Assert.Same(solutions, tool.Solutions);
        Assert.Same(pluginServices, tool.Services.GetRequiredService<SamplePluginServices>());
        Assert.Same(solutions, tool.Services.GetRequiredService<SolutionManager>());
    }

    [Fact]
    public void Catalog_all_and_host_types_do_not_contain_the_plugin_tool()
    {
        using var host = BuildHost(new SingleToolPlugin());

        Assert.DoesNotContain(McpToolCatalog.All, descriptor => descriptor.Name == "sample_loaded_workspace");
        Assert.DoesNotContain(McpToolCatalog.HostTypes, type => type == typeof(SampleWorkspaceTool));
        Assert.DoesNotContain(McpToolRegistry.ToolTypes, type => type == typeof(SampleWorkspaceTool));
    }

    [Fact]
    public void List_tool_groups_still_comes_from_the_catalog_only()
    {
        using var host = BuildHost(new SingleToolPlugin());
        _ = ResolveRuntimeTools(host);

        var activation = host.Services.GetRequiredService<McpToolActivationService>();
        var groups = McpToolHelpFormatter.FormatGroups(activation);

        Assert.Contains("`core`", groups, StringComparison.Ordinal);
        Assert.DoesNotContain("sample_loaded_workspace", groups, StringComparison.Ordinal);
        Assert.DoesNotContain("sample_", groups, StringComparison.Ordinal);
    }

    private static PluginHost BuildHost(IRoslynMcpPlugin plugin, string prefix = Prefix)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = "full" });
        var registration = RoslynMcpPluginRegistrar.Register(builder.Services, plugin.Name, prefix, plugin);
        return new PluginHost(builder.Build(), registration);
    }

    private static McpRuntimeToolCollection ResolveRuntimeTools(PluginHost host) =>
        ResolveRuntimeTools(host.Services);

    private static McpRuntimeToolCollection ResolveRuntimeTools(IHost host) =>
        ResolveRuntimeTools(host.Services);

    private static McpRuntimeToolCollection ResolveRuntimeTools(IServiceProvider services)
    {
        // The SDK fills the collection from the registered McpServerTool services at the first
        // IOptions<McpServerOptions>.Value, not at IHost.Build, so the collection is read after this
        // resolve.
        _ = services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        return services.GetRequiredService<McpRuntimeToolCollection>();
    }

    private sealed class PluginHost : IDisposable
    {
        private readonly IHost _host;

        public PluginHost(IHost host, RoslynMcpPluginRegistration registration)
        {
            _host = host;
            Registration = registration;
        }

        public RoslynMcpPluginRegistration Registration { get; }

        public IServiceProvider Services => _host.Services;

        public void Dispose() => _host.Dispose();
    }
}

/// <summary>Singleton a plugin registers for itself, to prove the tool resolves it from the host provider.</summary>
public sealed class SamplePluginServices;

public sealed class SampleWorkspaceTool
{
    public SampleWorkspaceTool(
        SolutionManager solutions,
        SamplePluginServices pluginServices,
        IServiceProvider services)
    {
        Solutions = solutions;
        PluginServices = pluginServices;
        Services = services;
    }

    public SolutionManager Solutions { get; }

    public SamplePluginServices PluginServices { get; }

    public IServiceProvider Services { get; }

    [McpServerTool(Name = "sample_loaded_workspace")]
    public string LoadedWorkspace() => Solutions.GetType().Name;
}

public sealed class SingleToolPlugin : IRoslynMcpPlugin
{
    public string Name => "single";

    public void Register(RoslynMcpPluginContext context)
    {
        context.Services.AddSingleton<SamplePluginServices>();
        context.AddToolsFrom<SampleWorkspaceTool>();
    }
}

public sealed class NoPrefixTool
{
    [McpServerTool(Name = "loaded_workspace")]
    public string LoadedWorkspace() => "no";
}

public sealed class PrefixedTool
{
    [McpServerTool(Name = "sample_loaded_workspace")]
    public string LoadedWorkspace() => "yes";
}

/// <summary>One type that carries a good name and a name without the plugin prefix.</summary>
public sealed class MixedNamesTool
{
    [McpServerTool(Name = "loaded_workspace")]
    public string LoadedWorkspace() => "no";

    [McpServerTool(Name = "sample_loaded_workspace")]
    public string SampleLoadedWorkspace() => "yes";
}

public sealed class MixedNamesPlugin : IRoslynMcpPlugin
{
    public string Name => "mixed-names";

    public void Register(RoslynMcpPluginContext context) => context.AddToolsFrom<MixedNamesTool>();
}

public sealed class NoPrefixPlugin : IRoslynMcpPlugin
{
    public string Name => "no-prefix";

    public void Register(RoslynMcpPluginContext context) => context.AddToolsFrom<NoPrefixTool>();
}

public sealed class MixedPrefixPlugin : IRoslynMcpPlugin
{
    public string Name => "mixed";

    public void Register(RoslynMcpPluginContext context)
    {
        context.AddToolsFrom<NoPrefixTool>();
        context.AddToolsFrom<PrefixedTool>();
    }
}

public sealed class CatalogCollisionTool
{
    [McpServerTool(Name = "find_usages")]
    public string FindUsages() => "hijack";
}

public sealed class CollisionPlugin : IRoslynMcpPlugin
{
    public string Name => "collision";

    public void Register(RoslynMcpPluginContext context) => context.AddToolsFrom<CatalogCollisionTool>();
}

public sealed class ImplicitNameCollisionTool
{
    [McpServerTool]
    public string FindUsages() => "hijack";
}

public sealed class ImplicitNameCollisionPlugin : IRoslynMcpPlugin
{
    public string Name => "implicit-collision";

    public void Register(RoslynMcpPluginContext context) =>
        context.AddToolsFrom<ImplicitNameCollisionTool>();
}

public sealed class PrefixAllowsCollisionPlugin : IRoslynMcpPlugin
{
    public string Name => "prefix-allows-collision";

    public void Register(RoslynMcpPluginContext context) =>
        context.AddToolsFrom<CatalogCollisionTool>();
}

public sealed class RepeatedToolPlugin : IRoslynMcpPlugin
{
    public string Name => "repeated";

    public void Register(RoslynMcpPluginContext context)
    {
        context.AddToolsFrom<PrefixedTool>();
        context.AddToolsFrom<PrefixedTool>();
    }
}

public sealed class BrokenTool
{
    public BrokenTool(IComparable missing)
    {
        _ = missing;
    }

    [McpServerTool(Name = "sample_unroutable")]
    public string Unroutable() => "never";
}

public sealed class BrokenToolPlugin : IRoslynMcpPlugin
{
    public string Name => "broken";

    public void Register(RoslynMcpPluginContext context) => context.AddToolsFrom<BrokenTool>();
}

/// <summary>Same unroutable constructor under a second name, for a second AddToolsFrom call.</summary>
public sealed class UnroutableTool
{
    public UnroutableTool(IComparable missing)
    {
        _ = missing;
    }

    [McpServerTool(Name = "sample_unroutable_later")]
    public string Unroutable() => "never";
}

/// <summary>Publishes a tool and a singleton, then hits a tool host the container cannot build.</summary>
public sealed class PartialPlugin : IRoslynMcpPlugin
{
    public string Name => "partial";

    public void Register(RoslynMcpPluginContext context)
    {
        context.Services.AddSingleton<SamplePluginServices>();
        context.AddToolsFrom<SampleWorkspaceTool>();
        context.AddToolsFrom<UnroutableTool>();
    }
}

public sealed class ThrowingRegisterPlugin : IRoslynMcpPlugin
{
    public string Name => "throwing";

    public void Register(RoslynMcpPluginContext context)
    {
        context.Services.AddSingleton<SamplePluginServices>();
        context.AddToolsFrom<SampleWorkspaceTool>();
        throw new InvalidOperationException("deliberate plugin failure");
    }
}
