using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class McpToolHelpTests
{
    private static readonly Regex AbsoluteDeveloperPath = new(
        @"(?:[A-Za-z]:\\|\\Users\\|\/Users\/|\/home\/)",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Help_catalog_covers_every_registered_tool()
    {
        var missing = McpToolCatalog.All
            .Select(d => d.Name)
            .Except(McpToolHelpCatalog.Entries.Keys, StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0, "Help catalog missing tools: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task Known_tool_help_reflects_parameters_and_group()
    {
        using var host = BuildHost(new McpToolProfileOptions { Profile = "full" });
        var tools = host.Services.GetRequiredService<ToolHelpTools>();
        var schema = host.Services.GetServices<McpServerTool>()
            .Single(t => t.ProtocolTool.Name == "load_workspace")
            .ProtocolTool.InputSchema;

        var help = await tools.GetToolHelp("load_workspace");

        Assert.Contains("# `load_workspace`", help, StringComparison.Ordinal);
        Assert.Contains("- Kind: process", help, StringComparison.Ordinal);
        Assert.Contains("- Group: `core` (active)", help, StringComparison.Ordinal);
        Assert.Contains("`workspacePath`", help, StringComparison.Ordinal);
        Assert.Contains("`configuration`", help, StringComparison.Ordinal);
        Assert.Contains("`platform`", help, StringComparison.Ordinal);
        Assert.Contains("`targetFramework`", help, StringComparison.Ordinal);
        Assert.Contains("required", help, StringComparison.Ordinal);
        Assert.Contains("default null", help, StringComparison.Ordinal);

        foreach (var name in schema.GetProperty("properties").EnumerateObject().Select(p => p.Name))
        {
            Assert.Contains($"`{name}`", help, StringComparison.Ordinal);
        }

        Assert.Equal("core", McpToolCatalog.All.First(d => d.Name == "load_workspace").Group);
    }

    [Fact]
    public async Task Unknown_tool_suggests_close_names()
    {
        using var host = BuildHost(new McpToolProfileOptions { Profile = "lite" });
        var tools = host.Services.GetRequiredService<ToolHelpTools>();

        var help = await tools.GetToolHelp("find_usage");

        Assert.Contains("Unknown tool `find_usage`", help, StringComparison.Ordinal);
        Assert.Contains("`find_usages`", help, StringComparison.Ordinal);
        Assert.DoesNotContain("# `find_usages`", help, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("full", null, true, true)]
    [InlineData("lite", null, true, false)]
    [InlineData("lite", "decompile", true, true)]
    public async Task Group_markdown_reports_active_state_and_startup_fallback(
        string profile,
        string? groups,
        bool coreActive,
        bool decompileActive)
    {
        using var host = BuildHost(new McpToolProfileOptions { Profile = profile, Groups = groups });
        var tools = host.Services.GetRequiredService<ToolHelpTools>();
        var markdown = await tools.ListToolGroups();

        Assert.Contains("# Tool groups", markdown, StringComparison.Ordinal);
        Assert.Contains($"Profile: `{profile}`", markdown, StringComparison.Ordinal);
        Assert.Contains("ROSLYN_MCP_TOOL_GROUPS=", markdown, StringComparison.Ordinal);
        Assert.Contains("ROSLYN_MCP_TOOL_PROFILE=lite", markdown, StringComparison.Ordinal);

        foreach (var group in McpToolGroups.All)
        {
            Assert.Contains($"## `{group}`", markdown, StringComparison.Ordinal);
            Assert.Contains($"- Purpose: {McpToolGroups.Describe(group)}", markdown, StringComparison.Ordinal);
        }

        Assert.Contains("`load_workspace`", markdown, StringComparison.Ordinal);
        AssertActive("core", coreActive, markdown);
        AssertActive("decompile", decompileActive, markdown);

        if (profile == "lite" && groups == "decompile")
        {
            Assert.Contains("Startup groups: `decompile`", markdown, StringComparison.Ordinal);
            Assert.Contains("`decompile_type`", markdown, StringComparison.Ordinal);
            AssertActive("files", false, markdown);
        }
    }

    [Fact]
    public async Task Help_output_contains_no_absolute_developer_paths()
    {
        using var host = BuildHost(new McpToolProfileOptions { Profile = "full" });
        var tools = host.Services.GetRequiredService<ToolHelpTools>();
        var groups = await tools.ListToolGroups();
        Assert.DoesNotMatch(AbsoluteDeveloperPath, groups);

        foreach (var descriptor in McpToolCatalog.All)
        {
            var help = await tools.GetToolHelp(descriptor.Name);
            Assert.False(
                AbsoluteDeveloperPath.IsMatch(help),
                $"Help for '{descriptor.Name}' contains a machine-specific path.");
        }

        Assert.DoesNotMatch(AbsoluteDeveloperPath, McpToolHelpCatalog.ServerInstructions);
    }

    [Fact]
    public async Task Inactive_lite_tool_help_still_lists_live_parameters()
    {
        using var host = BuildHost(new McpToolProfileOptions { Profile = "lite" });
        var tools = host.Services.GetRequiredService<ToolHelpTools>();
        var help = await tools.GetToolHelp("search_code");

        Assert.Contains("- Group: `files` (inactive)", help, StringComparison.Ordinal);
        Assert.Contains("`pattern`", help, StringComparison.Ordinal);
        Assert.Contains("`directoryPath`", help, StringComparison.Ordinal);
        Assert.Contains("Kind: read", help, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_instructions_are_one_short_discovery_sentence()
    {
        Assert.DoesNotContain("load_workspace", McpToolHelpCatalog.ServerInstructions, StringComparison.Ordinal);
        Assert.Contains("list_tool_groups", McpToolHelpCatalog.ServerInstructions, StringComparison.Ordinal);
        Assert.Contains("enable_tool_group", McpToolHelpCatalog.ServerInstructions, StringComparison.Ordinal);
        Assert.Contains("ROSLYN_MCP_TOOL_GROUPS", McpToolHelpCatalog.ServerInstructions, StringComparison.Ordinal);
        Assert.True(McpToolHelpCatalog.ServerInstructions.Length < 250);
        Assert.DoesNotContain('\n', McpToolHelpCatalog.ServerInstructions);
    }

    private static void AssertActive(string group, bool expected, string markdown)
    {
        var heading = $"## `{group}`";
        var start = markdown.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing group heading {heading}");
        var next = markdown.IndexOf("## `", start + heading.Length, StringComparison.Ordinal);
        var block = next < 0 ? markdown[start..] : markdown[start..next];
        Assert.Contains(expected ? "- Active: yes" : "- Active: no", block, StringComparison.Ordinal);
    }

    private static IHost BuildHost(McpToolProfileOptions options)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(options);
        return builder.Build();
    }
}
