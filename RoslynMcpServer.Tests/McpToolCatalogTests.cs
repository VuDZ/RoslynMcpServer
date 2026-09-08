using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Services;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests;

public sealed class McpToolCatalogTests(ITestOutputHelper output)
{
    public const int FullCatalogBudgetBytes = 45 * 1024;
    public const int LiteCatalogBudgetBytes = 20 * 1024;

    [Fact]
    public void Default_profile_is_full()
    {
        var surface = McpToolCatalog.CreateSurface(new McpToolProfileOptions());
        Assert.Equal(McpToolProfileOptions.FullProfile, surface.Profile);
        Assert.Empty(surface.StartupGroups);
        Assert.Equal(McpToolCatalog.All.Count, surface.RegisteredToolCount);
    }

    [Fact]
    public void Empty_and_whitespace_profile_is_full()
    {
        Assert.Equal(McpToolProfileOptions.FullProfile, McpToolCatalog.CreateSurface(new McpToolProfileOptions { Profile = "" }).Profile);
        Assert.Equal(McpToolProfileOptions.FullProfile, McpToolCatalog.CreateSurface(new McpToolProfileOptions { Profile = "  " }).Profile);
    }

    [Fact]
    public void Explicit_full_contains_every_pre_existing_tool()
    {
        var surface = McpToolCatalog.CreateSurface(new McpToolProfileOptions { Profile = "full" });
        var reflected = McpToolCatalog.DiscoverAttributedTools()
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        var registered = surface.RegisteredTools
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(61, reflected.Length);
        Assert.Equal(reflected, registered);
    }

    [Fact]
    public void Lite_contains_only_agreed_core()
    {
        var surface = McpToolCatalog.CreateSurface(new McpToolProfileOptions { Profile = "lite" });
        var expected = new[]
        {
            "get_mcp_server_info",
            "list_tool_groups",
            "get_tool_help",
            "load_workspace",
            "reset_workspace",
            "get_code_skeleton",
            "get_class_skeleton",
            "get_diagnostics_for_file",
            "find_symbol_definition",
            "find_usages",
            "find_symbol_references",
            "find_implementations",
            "get_call_graph",
            "run_dotnet_build",
            "run_dotnet_test",
            "run_specific_test",
            "get_changed_files",
        };

        Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal), surface.RegisteredTools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(surface.RegisteredTools, d => Assert.True(d.InLiteCore));
        Assert.DoesNotContain(surface.RegisteredTools, d => d.Name == "decompile_type");
        Assert.DoesNotContain(surface.RegisteredTools, d => d.Name == "search_code");
        Assert.DoesNotContain(surface.RegisteredTools, d => d.Name == "enable_tool_group");
        Assert.Contains(surface.RegisteredTools, d => d.Name == "list_tool_groups");
        Assert.Contains(surface.RegisteredTools, d => d.Name == "get_tool_help");
    }

    [Fact]
    public void Startup_groups_expand_lite()
    {
        var surface = McpToolCatalog.CreateSurface(new McpToolProfileOptions
        {
            Profile = "lite",
            Groups = "decompile,nuget",
        });

        Assert.Equal(["decompile", "nuget"], surface.StartupGroups);
        Assert.Contains(surface.RegisteredTools, d => d.Name == "decompile_type");
        Assert.Contains(surface.RegisteredTools, d => d.Name == "list_nuget_packages");
        Assert.Contains(surface.RegisteredTools, d => d.Name == "add_package_reference");
        Assert.DoesNotContain(surface.RegisteredTools, d => d.Name == "search_code");
        Assert.DoesNotContain(surface.RegisteredTools, d => d.Name == "run_dotnet_run");
        Assert.True(surface.RegisteredToolCount > McpToolCatalog.All.Count(d => d.InLiteCore));
    }

    [Fact]
    public void Startup_groups_are_noop_in_full()
    {
        var surface = McpToolCatalog.CreateSurface(new McpToolProfileOptions
        {
            Profile = "full",
            Groups = "decompile,nuget",
        });

        Assert.Equal(["decompile", "nuget"], surface.StartupGroups);
        Assert.Equal(McpToolCatalog.All.Count, surface.RegisteredToolCount);
    }

    [Fact]
    public void Invalid_profile_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            McpToolCatalog.CreateSurface(new McpToolProfileOptions { Profile = "tiny" }));

        Assert.Contains("Unknown tool profile 'tiny'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("full", ex.Message, StringComparison.Ordinal);
        Assert.Contains("lite", ex.Message, StringComparison.Ordinal);
        Assert.Contains(McpToolProfileOptions.ProfileVariableName, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_group_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            McpToolCatalog.CreateSurface(new McpToolProfileOptions { Groups = "decompile,widgets" }));

        Assert.Contains("Unknown tool group 'widgets'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("files", ex.Message, StringComparison.Ordinal);
        Assert.Contains(McpToolProfileOptions.GroupsVariableName, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Casing_whitespace_and_duplicate_groups_are_normalized()
    {
        var surface = McpToolCatalog.CreateSurface(new McpToolProfileOptions
        {
            Profile = " LITE ",
            Groups = " Decompile , nuget, DECOMPILE , files ",
        });

        Assert.Equal(McpToolProfileOptions.LiteProfile, surface.Profile);
        Assert.Equal(["decompile", "nuget", "files"], surface.StartupGroups);
        Assert.Contains(surface.RegisteredTools, d => d.Name == "decompile_type");
        Assert.Contains(surface.RegisteredTools, d => d.Name == "search_code");
        Assert.Contains(surface.RegisteredTools, d => d.Name == "list_nuget_packages");
    }

    [Fact]
    public void Public_tool_names_are_unique()
    {
        var duplicates = McpToolCatalog.All
            .GroupBy(d => d.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Reserved_bootstrap_names_are_not_registered()
    {
        foreach (var name in McpToolCatalog.ReservedBootstrapToolNames)
        {
            Assert.DoesNotContain(McpToolCatalog.All, d => d.Name.Equals(name, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Every_catalog_entry_can_be_activated_through_di()
    {
        using var host = BuildHost(new McpToolProfileOptions { Profile = "full" });
        foreach (var entry in McpToolCatalog.All)
        {
            var instance = ActivatorUtilities.CreateInstance(host.Services, entry.HostType);
            Assert.NotNull(instance);
            Assert.IsType(entry.HostType, instance, exactMatch: false);
        }
    }

    [Fact]
    public void Lite_does_not_register_excluded_tools_in_sdk_collection()
    {
        using var host = BuildHost(new McpToolProfileOptions { Profile = "lite" });
        var registered = GetRegisteredToolNames(host.Services);

        Assert.Equal(
            McpToolCatalog.All.Where(d => d.InLiteCore).Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal),
            registered.OrderBy(n => n, StringComparer.Ordinal));
        Assert.DoesNotContain("decompile_type", registered);
        Assert.DoesNotContain("apply_patch", registered);
        Assert.Contains("list_tool_groups", registered);
        Assert.Contains("get_tool_help", registered);
        Assert.DoesNotContain("enable_tool_group", registered);
    }

    [Fact]
    public void Startup_groups_appear_in_first_sdk_tool_list()
    {
        using var host = BuildHost(new McpToolProfileOptions
        {
            Profile = "lite",
            Groups = "decompile",
        });

        var registered = GetRegisteredToolNames(host.Services);
        Assert.Contains("decompile_type", registered);
        Assert.Contains("explore_assembly", registered);
        Assert.Contains("get_mcp_server_info", registered);
        Assert.DoesNotContain("search_code", registered);
    }

    [Fact]
    public void Server_info_reports_active_profile_groups_and_count()
    {
        using var host = BuildHost(new McpToolProfileOptions
        {
            Profile = "lite",
            Groups = "files",
        });

        var surface = host.Services.GetRequiredService<McpToolSurface>();
        var info = McpServerInfoHelper.BuildInfoMarkdown(loadedSolution: null, surface);

        Assert.Contains("- **Tool profile:** `lite`", info, StringComparison.Ordinal);
        Assert.Contains("- **Startup tool groups:** `files`", info, StringComparison.Ordinal);
        Assert.Contains($"- **Registered MCP tools:** {surface.RegisteredToolCount}", info, StringComparison.Ordinal);
        Assert.DoesNotContain($"- **Registered MCP tools:** {McpToolCatalog.All.Count}", info, StringComparison.Ordinal);
        Assert.Equal(surface.RegisteredToolCount, host.Services.GetServices<McpServerTool>().Count());
    }

    [Fact]
    public void Options_are_registered_for_the_resolved_surface()
    {
        using var host = BuildHost(new McpToolProfileOptions { Profile = "lite", Groups = "runtime" });
        var options = host.Services.GetRequiredService<IOptions<McpToolProfileOptions>>().Value;
        Assert.Equal(McpToolProfileOptions.LiteProfile, options.Profile);
        Assert.Equal("runtime", options.Groups);
    }

    [Fact]
    public void Minified_tools_list_sizes_are_within_epoch2_budgets()
    {
        using var fullHost = BuildHost(new McpToolProfileOptions { Profile = "full" });
        using var liteHost = BuildHost(new McpToolProfileOptions { Profile = "lite" });

        var full = MeasureToolsList(fullHost.Services);
        var lite = MeasureToolsList(liteHost.Services);
        output.WriteLine($"full tools/list UTF-8 bytes={full.Utf8Bytes} count={full.Count}");
        output.WriteLine($"lite tools/list UTF-8 bytes={lite.Utf8Bytes} count={lite.Count}");

        Assert.Equal(McpToolCatalog.All.Count, full.Count);
        Assert.Equal(McpToolCatalog.All.Count(d => d.InLiteCore), lite.Count);
        Assert.True(
            full.Utf8Bytes <= FullCatalogBudgetBytes,
            $"full tools/list is {full.Utf8Bytes} bytes (lite {lite.Utf8Bytes}); budget is {FullCatalogBudgetBytes}.");
        Assert.True(
            lite.Utf8Bytes <= LiteCatalogBudgetBytes,
            $"lite tools/list is {lite.Utf8Bytes} bytes (full {full.Utf8Bytes}); budget is {LiteCatalogBudgetBytes}.");
    }

    [Fact]
    public void Description_character_count_is_recorded()
    {
        var all = McpToolCatalog.SumDescriptionCharacters();
        var withoutHelp = McpToolCatalog.SumDescriptionCharacters(
            McpToolCatalog.All.Where(d => d.Name is not ("list_tool_groups" or "get_tool_help")));
        output.WriteLine($"description chars all={all} withoutHelp={withoutHelp} epoch1={McpToolCatalog.Epoch1DescriptionCharacters}");

        var ceiling = (int)Math.Floor(McpToolCatalog.Epoch1DescriptionCharacters * 0.6);
        Assert.True(
            withoutHelp <= ceiling,
            $"Description text is {withoutHelp} chars; 40% reduction from {McpToolCatalog.Epoch1DescriptionCharacters} requires <= {ceiling}.");
    }

    [Fact]
    public void Every_registered_tool_has_a_non_empty_description()
    {
        using var host = BuildHost(new McpToolProfileOptions { Profile = "full" });
        foreach (var tool in host.Services.GetServices<McpServerTool>())
        {
            Assert.False(
                string.IsNullOrWhiteSpace(tool.ProtocolTool.Description),
                $"Tool '{tool.ProtocolTool.Name}' has an empty description.");
        }
    }

    [Fact]
    public void Public_tool_schemas_preserve_required_default_and_type()
    {
        using var host = BuildHost(new McpToolProfileOptions { Profile = "full" });
        var byName = host.Services.GetServices<McpServerTool>().ToDictionary(t => t.ProtocolTool.Name, StringComparer.Ordinal);

        foreach (var descriptor in McpToolCatalog.All)
        {
            Assert.True(byName.TryGetValue(descriptor.Name, out var tool), descriptor.Name);
            var schema = tool.ProtocolTool.InputSchema;
            Assert.Equal(JsonValueKind.Object, schema.ValueKind);

            var properties = schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object
                ? props
                : default;
            var required = schema.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.Array
                ? reqEl.EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            foreach (var parameter in McpToolHelpFormatter.ReadParameters(descriptor.Method))
            {
                Assert.Equal(JsonValueKind.Object, properties.ValueKind);
                Assert.True(
                    properties.TryGetProperty(parameter.Name, out var prop),
                    $"Tool '{descriptor.Name}' is missing schema property '{parameter.Name}'.");
                Assert.True(
                    HasJsonType(prop),
                    $"Tool '{descriptor.Name}' parameter '{parameter.Name}' lost JSON type information.");

                if (parameter.Required)
                {
                    Assert.Contains(parameter.Name, required);
                }

                if (!parameter.Required)
                {
                    Assert.True(
                        prop.TryGetProperty("default", out _),
                        $"Tool '{descriptor.Name}' parameter '{parameter.Name}' lost its default value.");
                }
            }
        }
    }

    private static bool HasJsonType(JsonElement property)
    {
        if (property.TryGetProperty("type", out var typeEl))
        {
            return typeEl.ValueKind is JsonValueKind.String or JsonValueKind.Array;
        }

        return property.TryGetProperty("anyOf", out _) || property.TryGetProperty("oneOf", out _);
    }

    internal static (int Count, int Utf8Bytes) MeasureToolsList(IServiceProvider services)
    {
        var tools = services.GetServices<McpServerTool>().Select(t => t.ProtocolTool).ToList();
        var result = new ListToolsResult { Tools = tools };
        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            document.RootElement.WriteTo(writer);
        }

        return (tools.Count, (int)stream.Length);
    }

    private static IReadOnlyList<string> GetRegisteredToolNames(IServiceProvider services) =>
        services.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ToArray();

    private static IHost BuildHost(McpToolProfileOptions options)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(options);
        return builder.Build();
    }
}
