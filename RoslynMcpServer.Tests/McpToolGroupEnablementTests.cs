using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class McpToolGroupEnablementTests
{
    [Fact]
    public void Unknown_group_returns_valid_names_and_does_not_notify()
    {
        using var host = BuildLite();
        var (activation, collection) = Resolve(host);
        var notifications = Subscribe(collection);

        var result = activation.EnableGroup("widgets");

        Assert.False(result.Changed);
        Assert.Empty(result.NewlyEnabledTools);
        Assert.Contains("Unknown group `widgets`", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("`files`", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("`list_tool_groups`", result.Markdown, StringComparison.Ordinal);
        Assert.Equal(0, notifications());
        Assert.Empty(activation.DynamicGroups);
    }

    [Fact]
    public void Group_name_is_normalized_case_insensitively()
    {
        using var host = BuildLite();
        var (activation, collection) = Resolve(host);

        var result = activation.EnableGroup(" FILES ");

        Assert.True(result.Changed);
        Assert.Equal(McpToolGroups.Files, Assert.Single(activation.DynamicGroups));
        Assert.Contains("Enabled `files`", result.Markdown, StringComparison.Ordinal);
        Assert.All(
            McpToolCatalog.All.Where(d => d.Group == McpToolGroups.Files),
            d => Assert.True(collection.TryGetPrimitive(d.Name, out _)));
    }

    [Fact]
    public void First_enable_adds_expected_group_tools()
    {
        using var host = BuildLite();
        var (activation, collection) = Resolve(host);
        var before = collection.Count;
        var files = McpToolCatalog.All.Where(d => d.Group == McpToolGroups.Files).Select(d => d.Name).ToArray();

        var result = activation.EnableGroup("files");

        Assert.True(result.Changed);
        Assert.Equal(files, result.NewlyEnabledTools);
        Assert.Equal(before + files.Length, activation.CurrentToolCount);
        Assert.Equal(["core", "files"], activation.ActiveGroups);
        Assert.Contains("`list_directory_tree`", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("Restart with ROSLYN_MCP_TOOL_GROUPS=files", result.Markdown, StringComparison.Ordinal);
        Assert.True(activation.IsToolActive("list_directory_tree"));
        Assert.False(activation.IsToolActive("decompile_type"));
    }

    [Fact]
    public void Repeated_enable_is_noop()
    {
        using var host = BuildLite();
        var (activation, collection) = Resolve(host);
        var notifications = Subscribe(collection);

        Assert.True(activation.EnableGroup("files").Changed);
        var afterFirst = activation.CurrentToolCount;
        var second = activation.EnableGroup("files");

        Assert.False(second.Changed);
        Assert.Empty(second.NewlyEnabledTools);
        Assert.Contains("already active", second.Markdown, StringComparison.Ordinal);
        Assert.Equal(afterFirst, activation.CurrentToolCount);
        Assert.Equal(1, notifications());
        Assert.Equal(["files"], activation.DynamicGroups);
    }

    [Fact]
    public void Concurrent_enables_do_not_duplicate_tools()
    {
        using var host = BuildLite();
        var (activation, collection) = Resolve(host);
        var notifications = Subscribe(collection);
        var files = McpToolCatalog.All.Count(d => d.Group == McpToolGroups.Files);
        var expected = collection.Count + files;

        Parallel.For(0, 8, _ => activation.EnableGroup("files"));

        Assert.Equal(expected, collection.Count);
        Assert.Equal(expected, collection.Select(t => t.ProtocolTool.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["files"], activation.DynamicGroups);
        Assert.Equal(1, notifications());
    }

    [Fact]
    public void Full_profile_enable_is_noop()
    {
        using var host = BuildHost(new McpToolProfileOptions { Profile = "full" });
        var (activation, collection) = Resolve(host);
        var notifications = Subscribe(collection);
        var before = collection.Count;

        var result = activation.EnableGroup("files");

        Assert.False(result.Changed);
        Assert.Contains("Profile `full`", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("no-op", result.Markdown, StringComparison.Ordinal);
        Assert.Equal(before, activation.CurrentToolCount);
        Assert.Empty(activation.DynamicGroups);
        Assert.Equal(0, notifications());
    }

    [Fact]
    public void Startup_group_enable_is_noop_without_notification()
    {
        using var host = BuildHost(new McpToolProfileOptions { Profile = "lite", Groups = "files" });
        var (activation, collection) = Resolve(host);
        var notifications = Subscribe(collection);

        var result = activation.EnableGroup("files");

        Assert.False(result.Changed);
        Assert.Contains("already active", result.Markdown, StringComparison.Ordinal);
        Assert.Empty(activation.DynamicGroups);
        Assert.Equal(0, notifications());
        Assert.True(activation.IsToolActive("list_directory_tree"));
    }

    [Fact]
    public async Task List_tool_groups_includes_dynamically_enabled_group()
    {
        using var host = BuildLite();
        var activation = host.Services.GetRequiredService<McpToolActivationService>();
        var help = host.Services.GetRequiredService<ToolHelpTools>();

        Assert.True(activation.EnableGroup("decompile").Changed);
        var markdown = await help.ListToolGroups();

        Assert.Contains("Dynamic groups: `decompile`", markdown, StringComparison.Ordinal);
        Assert.Contains("## `decompile`", markdown, StringComparison.Ordinal);
        var start = markdown.IndexOf("## `decompile`", StringComparison.Ordinal);
        var next = markdown.IndexOf("## `", start + 10, StringComparison.Ordinal);
        var block = next < 0 ? markdown[start..] : markdown[start..next];
        Assert.Contains("- Active: yes", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_info_reports_dynamic_groups_and_live_count()
    {
        using var host = BuildLite();
        var activation = host.Services.GetRequiredService<McpToolActivationService>();
        var before = activation.CurrentToolCount;

        Assert.True(activation.EnableGroup("nuget").Changed);
        var info = RoslynMcpServer.Services.McpServerInfoHelper.BuildInfoMarkdown(null, activation);

        Assert.Contains("- **Dynamic tool groups:** `nuget`", info, StringComparison.Ordinal);
        Assert.Contains($"- **Registered MCP tools:** {activation.CurrentToolCount}", info, StringComparison.Ordinal);
        Assert.True(activation.CurrentToolCount > before);
        Assert.DoesNotContain($"- **Registered MCP tools:** {before}\n", info + "\n", StringComparison.Ordinal);
    }

    private static IHost BuildLite() =>
        BuildHost(new McpToolProfileOptions { Profile = "lite" });

    private static IHost BuildHost(McpToolProfileOptions options)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(options);
        return builder.Build();
    }

    private static (McpToolActivationService Activation, McpRuntimeToolCollection Collection) Resolve(IHost host)
    {
        _ = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        return (
            host.Services.GetRequiredService<McpToolActivationService>(),
            host.Services.GetRequiredService<McpRuntimeToolCollection>());
    }

    private static Func<int> Subscribe(McpRuntimeToolCollection collection)
    {
        var count = 0;
        collection.Changed += (_, _) => Interlocked.Increment(ref count);
        return () => Volatile.Read(ref count);
    }
}
