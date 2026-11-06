using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using RoslynMcpServer.Hosting;
using Xunit;

namespace RoslynMcpServer.Tests.Hosting;

public sealed class EstimateAffectedTestsCatalogTests
{
    [Theory]
    [InlineData("full")]
    [InlineData("lite")]
    public void Estimate_is_a_core_process_tool_in_both_profiles_with_minimal_run_parameters(string profile)
    {
        var descriptor = Assert.Single(McpToolCatalog.CreateSurface(new McpToolProfileOptions { Profile = profile })
            .RegisteredTools, tool => tool.Name == "estimate_affected_tests");
        Assert.Equal(McpToolGroups.Core, descriptor.Group);
        Assert.True(descriptor.InLiteCore);
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools(new McpToolProfileOptions { Profile = profile });
        using var host = builder.Build();
        var tool = Assert.Single(host.Services.GetServices<McpServerTool>(), tool => tool.ProtocolTool.Name == descriptor.Name);
        Assert.False(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.True(descriptor.ExecutesProcess);
        var description = tool.ProtocolTool.Description!;
        Assert.Contains("approximate", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a guarantee", description, StringComparison.Ordinal);
        Assert.Contains("full test suite or CI", description, StringComparison.Ordinal);
        Assert.DoesNotContain("call graph", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("run=true", description, StringComparison.Ordinal);
        var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
        Assert.Equal(["includeDetails", "run", "timeoutSeconds"], properties.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.False(properties.GetProperty("includeDetails").GetProperty("default").GetBoolean());
        Assert.False(properties.GetProperty("run").GetProperty("default").GetBoolean());
        Assert.Equal(300, properties.GetProperty("timeoutSeconds").GetProperty("default").GetInt32());
    }
}
